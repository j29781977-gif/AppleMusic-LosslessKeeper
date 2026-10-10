# 发布到 GitHub 全流程

假设仓库名 `AppleMusicLosslessKeeper`，你的 GitHub 用户名 `YOURNAME`。
下面命令在仓库根目录（本文件上一级）里执行。

## 0. 准备

* 安装 Git for Windows（已装可跳过）：<https://git-scm.com/download/win>
* 注册 GitHub 账号：<https://github.com/signup>
* 设置提交身份（只需一次）：

```powershell
git config --global user.name  "YOURNAME"
git config --global user.email "you@example.com"
```

## 1. 本地初始化并提交

```powershell
cd <仓库目录>
git init -b main
git add .
git commit -m "AppleMusicLosslessKeeper 1.2.0"
```

想先检查会提交哪些文件、有没有把日志/构建产物带上：

```powershell
git status --short
git ls-files          # 提交后看清单
```

`.gitignore` 已经排除了 `dist/`、`*.zip`、`keeper.log`、`captures/`。

## 2. 在 GitHub 上创建空仓库

网页方式：<https://github.com/new> → Repository name 填 `AppleMusicLosslessKeeper`
→ 选 Public → **不要**勾选 "Add a README"（已有）→ Create repository。

命令行方式（装了 GitHub CLI 的话）：

```powershell
winget install --id GitHub.cli      # 安装 gh
gh auth login                       # 浏览器授权
gh repo create AppleMusicLosslessKeeper --public --source . --remote origin --push
```

## 3. 推送

```powershell
git remote add origin https://github.com/YOURNAME/AppleMusicLosslessKeeper.git
git push -u origin main
```

首次推送会弹出浏览器让你登录 GitHub（Git Credential Manager 会记住凭据）。

## 4. 打标签并发布 Release（把 ZIP 作为附件）

先本地生成发布包：

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Build-Release.ps1 -Version 1.2.0
# 产物：dist\AppleMusicLosslessKeeper-1.2.0.zip
```

打标签并推送：

```powershell
git tag v1.2.0
git push origin v1.2.0
```

然后在网页上创建 Release：仓库 → **Releases** → *Draft a new release* →
Choose a tag 选 `v1.2.0` → Title 填 `1.2.0` → 描述里放 CHANGELOG 里对应的条目 →
把 `dist\AppleMusicLosslessKeeper-1.2.0.zip` 拖到 *Attach binaries* → **Publish release**。

或用 gh：

```powershell
gh release create v1.2.0 .\dist\AppleMusicLosslessKeeper-1.2.0.zip --title "1.2.0" --notes-file CHANGELOG.md
```

### 自动打包（可选）

仓库里带了 `.github/workflows/release.yml`：**推送 `v*` 标签时**会在 GitHub 的
Windows runner 上执行 `tools/Build-Release.ps1`，并把生成的 ZIP 自动附加到 Release。
用这个流程时，第 4 步只需要 `git tag` + `git push origin v1.2.0`，网页上不用手动上传。

## 5. 别人怎么用

1. Releases 页面下载 `AppleMusicLosslessKeeper-1.2.0.zip`；
2. 解压；
3. 双击 `Install.cmd`（不需要管理员）；
4. 打开 Apple Music 即可。

Windows 可能对"从网上下载的 zip"标记来源，解压后首次运行 `.cmd` 时若被 SmartScreen 拦，
点"更多信息 → 仍要运行"。仓库里只有文本脚本，没有任何可执行文件。

## 6. 后续维护

```powershell
git add -A
git commit -m "fix: ..."
git push
```

Apple 更新导致界面变化时的定位方法见 [TROUBLESHOOTING.md](TROUBLESHOOTING.md)。

## 7. 建议的仓库设置

* **Topics**：`apple-music`、`windows`、`ui-automation`、`powershell`、`lossless`
* **Description**：`Keeps Lossless audio enabled in Apple Music for Windows (the app forgets it on every launch)`
* **Issues**：保持开启，README 里请用户附上 `keeper.log` 与 Apple Music 版本号
* 若收到"这是 Apple 的 bug，为什么不去反馈"的 issue：README 的"原理"一节已经说明
  这是应用侧缺陷且已实测过外部无法修复
