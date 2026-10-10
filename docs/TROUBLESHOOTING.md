# 排错（TROUBLESHOOTING）

## 先看日志

```
%LOCALAPPDATA%\Programs\AppleMusicLosslessKeeper\keeper.log
```

正常的一次修复长这样：

```
20:17:56.123  pid 12345: lossless enabled (lossless Off->On; 流播放=set | window was at 152,152 when we took over)
```

带 `-VerboseLog`（或直接跑 `Keeper.ps1 -Once -VerboseLog`）还能看到各阶段耗时：

```
timing: home content at 5405 ms
timing: settings menu at 6721 ms
timing: playback page at 7286 ms
```

## 程序根本没在跑

```powershell
# 看进程
Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" |
  Where-Object { $_.CommandLine -like '*Keeper.ps1*' } | Select-Object ProcessId,CommandLine

# 看自启动
Get-ScheduledTask -TaskName AppleMusicLosslessKeeper | Select-Object TaskName,State
```

如果任务不存在，重跑一次 `Install.cmd`（会自动回退到「启动」文件夹）。

## 手动验证一次

双击 `Fix-Now.cmd`（等于 `Keeper.ps1 -Once`）。若日志里出现
`lossless enabled (…)` 就说明逻辑正常，问题只在"常驻"环节。

## 日志里的常见信息

| 日志 | 含义 / 处理 |
| --- | --- |
| `another instance is already running; exiting` | 已经有一个守护在跑（正常，不会重复） |
| `attempt N failed (…)` | 界面还没就绪，程序会自动重试；只要最后出现 `lossless enabled` 就没问题 |
| `lossless toggle not found` | 播放页里没找到开关：多半是 Apple 改了界面，或系统语言不在 `src/strings.json` 里（结构回退也应命中，若仍失败请看下一条） |
| `Settings menu item not found` | 侧栏菜单结构改变（自动化 ID：`MoreButton` / `SettingMenu`） |
| `window was off screen - put it back at …` | 应用自己把窗口丢到屏幕外，程序及时拉回（正常保护动作） |
| `reset stored window state 2 -> 0` | 修复了 `settings.dat` 里被写坏的窗口状态 |
| `prefs template missing` | 旧版本的遗留日志，可忽略（现行版本不再使用文件方式） |

## Apple 更新后失效怎么办

程序依赖三个界面锚点：

1. 侧栏「更多/边栏操作」按钮 —— 自动化 ID `MoreButton`
2. 菜单里的「设置」 —— 自动化 ID `SettingMenu`
3. 设置页里的「播放」分节 —— 自动化 ID `PlaybackNavigationViewItem`

以及 `src/strings.json` 里的文案。失效时的排查方法：

```powershell
# 打印当前界面树，确认 ID 还在不在
# （把 tools 里的 uia.ps1 复制到一个临时目录即可，或在仓库里直接跑）
powershell -File .\tools\uia.ps1 -DumpTo tree.txt
Select-String -Path tree.txt -Pattern 'MoreButton|SettingMenu|PlaybackNavigationViewItem'
```

* ID 变了 → 改 `src/Keeper.cs` 里对应的字符串；
* 只是文案变了 → 改 `src/strings.json`，无需改代码。

## 会不会影响音质/下载

* 只改 `流播放`（流式播放音质）与 `无损音频` 总开关。
* `下载` 质量不动，下载仍然是应用自己设定的值（默认 AAC 256 kbps）。
* 若想连下载一起管，把 `src/Keeper.cs` 里的 `SetDownloadQuality` 改成 `true`。

## 想恢复原状

* 只停掉这一次：任务管理器里结束那个 powershell 进程（命令行含 `Keeper.ps1`），
  或 `Uninstall.cmd`。
* 完全移除：双击 `Uninstall.cmd`。

## 杀毒软件/SmartScreen 提示

仓库里没有任何可执行二进制 —— 只有 `.ps1`、`.cmd`、`.vbs` 和 `.cs` 文本。
如果 SmartScreen/杀软对"启动文件夹里的 vbs"或"计划任务"有提示：

* 选择"仍要运行"；
* 或者改用 `Install.ps1` 里回退到的启动文件夹方式（把 vbs 手动放进
  `shell:startup`）；
* 或直接忽略自启动，需要时手动双击 `Fix-Now.cmd`。

## 内存/性能

一个隐藏的 PowerShell 进程常驻，约 50–80 MB 内存；空闲时每秒一次窗口枚举，CPU 占用可忽略。
