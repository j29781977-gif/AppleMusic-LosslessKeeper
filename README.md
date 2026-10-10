# AppleMusicLosslessKeeper

**Keeps "Lossless audio" switched on in Apple Music for Windows.**

[English](#english) · [中文](#中文)

---

## English

### The problem

Apple Music for Windows (`AppleInc.AppleMusicWin`) does **not** remember the audio
quality settings. Every time you start it:

* `Lossless audio` is back to **off**, and
* `Streaming quality` is back to **High Quality (AAC 256 kbps)**.

This is an app bug, not a misconfiguration: the app writes its defaults into its
own preferences **before** it reads them back, so the stored value never wins.
No configuration file can be patched from the outside (see
[docs/HOW-IT-WORKS.md](docs/HOW-IT-WORKS.md) for the experiments).

### What this tool does

A small background keeper that watches for Apple Music's window and, once the
app's own settings UI is responsive, turns `Lossless audio` on and sets
`Streaming quality` to **Hi-Res Lossless (ALAC up to 24-bit/192 kHz)**.

* The **download quality is never touched** (it keeps whatever Apple Music set).
* The window is **never moved or hidden** in the default mode: Apple Music opens
  at its normal speed (measured: window appears after 1.19–1.24 s), and the
  setting is applied in the background about 6 seconds later, taking ~1.3 s
  (you may briefly see the settings page).
* If you prefer *zero* visible change, switch to `stealth` mode: the window is
  moved off screen before it is ever painted and put back afterwards, so nothing
  is visible — at the cost of the window appearing ~8–10 s late.

### Install

1. Download `AppleMusicLosslessKeeper-<version>.zip` from
   [Releases](../../releases).
2. Extract it anywhere.
3. Double-click **`Install.cmd`** (no administrator rights needed).

That copies the tool to `%LOCALAPPDATA%\Programs\AppleMusicLosslessKeeper`,
registers it to start at logon and starts it immediately. Open Apple Music and
you are done.

**Uninstall:** double-click `Uninstall.cmd` (also installed with the tool).

### Usage

| File | What it does |
| --- | --- |
| `Install.cmd` | install for the current user |
| `Uninstall.cmd` | remove everything again |
| `Fix-Now.cmd` | run one check/fix right now |
| `Switch-Mode.cmd` | toggle between `direct` (instant window) and `stealth` (no visible change) |
| `Edit-Config.cmd` | open `run-hidden.vbs` in Notepad (the `-Mode` switch lives there) |
| `keeper.log` | what it did (in the install folder) |

Modes:

```powershell
Keeper.ps1 -Mode direct      # default: window opens normally, settings page flashes ~1.3 s
Keeper.ps1 -Mode stealth     # nothing visible, but the window appears ~8-10 s late
Keeper.ps1 -Once             # one check/fix, then exit
Keeper.ps1 -VerboseLog       # log every step
```

### Requirements

* Windows 10 / 11
* Apple Music for Windows (the MSIX app, `AppleInc.AppleMusicWin`)
* Windows PowerShell 5.1 (built into Windows — no install needed)
* No admin rights, no third-party modules, no compiled binaries in this repo

The UI labels are matched case-insensitively against `src/strings.json`
(11 locales included). If your language is not listed the tool falls back to the
*structure* of the Playback page (the 4th switch, the first combo box below it,
the last entry of the dropdown), so it should still work.

### Development

```powershell
git clone https://github.com/<you>/AppleMusicLosslessKeeper.git
cd AppleMusicLosslessKeeper
powershell -File .\Keeper.ps1 -Once -VerboseLog     # run straight from the clone
powershell -File .\tools\Build-Release.ps1         # build the release ZIP
```

`Keeper.ps1` compiles `src/Keeper.cs` **in memory** (no DLL is written), with the
localized labels turned into `\uXXXX` escapes so the source stays pure ASCII.
It has to run inside a PowerShell process: on the machine this was built on, a
standalone `.exe` asking UI Automation for the app window only sees the top-level
window (6 nodes) while PowerShell sees the whole XAML tree (1000+ nodes).

See [docs/HOW-IT-WORKS.md](docs/HOW-IT-WORKS.md) for the reverse-engineering
notes, [docs/TROUBLESHOOTING.md](docs/TROUBLESHOOTING.md) when something does not
work, and [docs/PUBLISHING.md](docs/PUBLISHING.md) to publish your own release.

### License

MIT — see [LICENSE](LICENSE).

---

## 中文

### 问题

Apple Music for Windows（`AppleInc.AppleMusicWin`）**不会记住音质设置**。每次启动：

* `无损音频` 回到**关闭**；
* `流播放` 回到**高质量 (AAC 256 kbps)**。

这是应用自身的缺陷：它**启动时先把默认值写回自己的偏好存储、然后才读取**，
所以存进去的值永远不生效。外部任何配置文件都改不动（实验过程见
[docs/HOW-IT-WORKS.md](docs/HOW-IT-WORKS.md)）。

### 这个程序做什么

一个后台小守护：监听 Apple Music 的窗口，等它**自己的设置界面可用**后，打开
`无损音频`，并把 `流播放` 设为 **高解析度无损（ALAC 最高 24 位 / 192 kHz）**。

* **完全不碰「下载」质量**（保持应用自己的值）。
* 默认模式下**不移动、不隐藏窗口**：Apple Music 以正常速度打开（实测窗口 1.19–1.24 秒出现），
  设置在约 6 秒后于后台完成，耗时约 1.3 秒（期间可能看到设置页短暂出现）。
* 若你要求**零可见变化**，切到 `stealth` 模式：窗口在被绘制前就移出屏幕、改完再放回，
  屏幕上看不到任何变化 —— 代价是窗口晚约 8–10 秒才出现。

### 安装

1. 到 [Releases](../../releases) 下载 `AppleMusicLosslessKeeper-<version>.zip`；
2. 解压到任意目录；
3. 双击 **`Install.cmd`**（无需管理员）。

它会安装到 `%LOCALAPPDATA%\Programs\AppleMusicLosslessKeeper`，注册登录自启并立即运行。
之后正常打开 Apple Music 即可。

**卸载**：双击 `Uninstall.cmd`。

### 使用

| 文件 | 作用 |
| --- | --- |
| `Install.cmd` | 为当前用户安装 |
| `Uninstall.cmd` | 完全卸载 |
| `Fix-Now.cmd` | 立刻检查并修正一次 |
| `Switch-Mode.cmd` | 在 `direct`（窗口秒开）与 `stealth`（零可见变化）之间切换 |
| `Edit-Config.cmd` | 用记事本打开 `run-hidden.vbs`（`-Mode` 参数在这里） |
| `keeper.log` | 运行日志（在安装目录里） |

### 环境要求

* Windows 10 / 11
* Apple Music for Windows（MSIX 应用 `AppleInc.AppleMusicWin`）
* 系统自带的 Windows PowerShell 5.1（无需安装任何东西）
* 不需要管理员权限、不依赖第三方模块、仓库内没有任何编译产物

界面文案按 `src/strings.json` 做**大小写不敏感的子串匹配**（内置 11 种语言）。
如果你的语言不在其中，程序会退回到按**结构**定位（播放页第 4 个开关、其下方的第一个下拉框、
下拉框的最后一项），通常仍然可用。

### 开发

```powershell
powershell -File .\Keeper.ps1 -Once -VerboseLog     # 直接在仓库里运行
powershell -File .\tools\Build-Release.ps1         # 生成发布用 ZIP
```

`Keeper.ps1` 会**在内存里**编译 `src/Keeper.cs`（不生成 DLL），界面文案被转成 `\uXXXX`
转义，因此源码始终是纯 ASCII，不受代码页影响。它必须运行在 PowerShell 进程内：
在本机上独立 `.exe` 通过 UI Automation 只能看到应用顶层窗口（6 个节点），
而 PowerShell 能看到完整 XAML 树（1000+ 节点）。

### 许可证

MIT，见 [LICENSE](LICENSE)。
