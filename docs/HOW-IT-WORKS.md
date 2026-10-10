# 原理与取证（HOW IT WORKS）

本文记录"Apple Music for Windows 丢失无损设置"这个问题的完整分析过程，
以及为什么最终只能通过操作应用自己的界面来解决。全部结论都在一台真实机器上验证过
（Windows 11 + Apple Music for Windows 1.6.4.90）。

## 1. 症状

每次启动 Apple Music：

* `设置 → 播放 → 音频质量 → 无损音频` 回到**关闭**；
* `流播放`、`下载` 回到**高质量 (AAC 256 kbps)**。

## 2. 排查过的存储位置（都不是）

| 位置 | 检查方式 | 结果 |
| --- | --- | --- |
| `…\Packages\AppleInc.AppleMusicWin_<hash>\Settings\settings.dat` | `RegLoadAppKey` 加载 UWP 设置库，对比"关闭/开启"两态 | 46 个值完全一致，只有时间戳变化 —— **不含音质字段** |
| `…\SystemAppData\Helium\User.dat`（应用的 silo 注册表 hive，内含 `Software\Apple Inc.\AppleMusic`） | 字节级 diff | 只差一个 64 字节的 `PreferenceMACs` 摘要 |
| `…\Apple Music Library.musiclibrary\Library Preferences.musicdb` | 两态目录快照 + 哈希对比 | 每次写入整体重加密（`hfma` 容器），尺寸在 724–729 字节间浮动 |
| `.iTunes Preferences.plist`（在应用二进制里确认存在） | 全盘搜索 | 磁盘上不存在，应用也从不创建 |
| 应用 ETW 日志 | `tracerpt` 转 CSV 后检索 | 出现 `eGetPrefs failed to return the … preference!`，键名为 `LosslessAudioEnabled`、`StreamingAudioQuality`、`DownloadAudioQuality`、`DolbyAtmosSetting` |

## 3. 关键实验：外部文件改不动

把上述候选文件在**应用全部进程已退出、silo hive 已卸载**的状态下还原成
"无损开启"版本，再启动应用 —— 依然显示关闭。

进一步的定向实验（每个组合都重跑过）：

| 实验 | 结果 |
| --- | --- |
| 只把 `Library Preferences.musicdb` 写成"开启"版本 | 启动后仍是关闭 |
| 同上 + 设为**只读** | 启动后仍是关闭（只读属性会被应用很快清掉） |
| 还原全部候选文件（含 `User.dat`、`settings.dat`） | 仍是关闭 |
| 还原全部候选文件 + 只读 | 仍是关闭 |

由此得到的模型是：**应用在启动时先把默认值写回自己的偏好存储，然后才读取**，
所以外部写进去的值在下一次启动时必然被覆盖。程序能做的只有：等它界面可用后，
用它自己的设置界面改一次（这与你在界面上手动改一次的效果完全一致）。

> 说明：开发过程中曾有一次实验"看起来成功了"，后来确认那是当时仍在运行的旧版
> 守护进程（UI Automation 路径）修好的，属于误判。文件路线最终被判定为不可行。

## 4. 为什么用 UI Automation，而且要跑在 PowerShell 里

* 打开 `设置 → 播放` 并拨动开关，是应用自己的代码路径，写入的值立即生效（当前会话）。
* 一个实测到的平台怪异点：在目标机器上，**独立 `.exe`** 通过 UI Automation 只能看到
  应用顶层窗口（6 个节点），而 **PowerShell 进程**能看到完整 XAML 树（1000+ 节点）。
  CLR 版本、UIAutomationClient/UIAutomationTypes 程序集、进程位数、应用程序清单、
  STA/MTA 全部排除过；把逻辑编成 DLL 再 `LoadFrom` 也会失败。
  因此 `Keeper.ps1` 在 PowerShell 进程内**在内存中编译** C# 源码并直接调用。

## 5. 如何做到"看不见"

* **WinEvent 钩子**（`EVENT_OBJECT_CREATE` … `EVENT_OBJECT_LOCATIONCHANGE`）监听主窗口：
  窗口刚被创建、还没绘制出来时就能捕获。
* `stealth` 模式：立刻把窗口移到屏幕外（保持"已显示"状态，UIA 依然可用），
  60 ms 的守护线程在修复期间持续把它按回去，改完再放回应用自己选的位置，
  并连续校验 5 次确认回到屏幕内。实测窗口在屏幕上出现 0～1 帧。
* `direct` 模式（默认）：完全不碰窗口。窗口以正常速度出现（实测 1.19–1.24 秒），
  程序等应用首页内容加载完成（约 5.4 秒）后再动手，只开一次侧栏菜单，
  约 1.3 秒完成整段操作。
* **窗口位置守护**：实测应用自己会把窗口丢到屏幕外（`-21333,-21333`，多半是上一次
  隐藏会话留下的坏值）。事件驱动的守护会在约 15 ms 内把它拉回原位，
  并修复 `settings.dat` 里被写坏的 `InitialWindowPosition*` / `InitialWindowState`。

## 6. 界面语言适配

`src/strings.json` 里列出 11 种语言的文案（`无损音频` / `Lossless Audio` / …），
匹配方式是**大小写不敏感的子串匹配**。若某个语言没有命中，程序退回按结构定位：

* 无损开关 = 播放页第 4 个 `ToggleSwitch`
  （顺序：交叉渐入渐出 → 声音增强器 → 音量平衡 → 无损音频）；
* 流播放 = 该页第一个 `ComboBox`（无损打开后才出现，其后是"下载"和"杜比全景声"）；
* 目标选项 = 下拉框的**最后一项**（质量从低到高排列，最高即最后）。

## 7. 实测数据（供参考）

| 指标 | 数值 |
| --- | --- |
| Apple Music 窗口出现（`direct`） | 1.19 – 1.24 秒 |
| 首页内容加载完成 | 约 5.4 秒 |
| 整段设置操作 | 约 1.3 秒 |
| `stealth` 下窗口在屏幕上出现 | 0 / 0 / 1 / 1 个采样点（每点 5–16 ms） |
| 窗口被应用移走后拉回 | 约 15 ms |
