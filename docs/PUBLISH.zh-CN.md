# 发布 BatteryPet v0.5.0

本地发布文件在 `artifacts/releases/v0.5.0/`。仅使用该目录的新包；旧 REVIEW ZIP 和旧「上传准备」不代表本版。当前工作区不是 Git checkout，打包不会自动提交或推送 GitHub。

## 文件用途

- `BatteryPet-v0.5.0-windows-x64-preview.zip`：给使用者的 Windows 程序。
- `BatteryPet-v0.5.0-source.zip`：给开发者的源码；解压后内容也可用于上传仓库。
- `SHA256SUMS.txt`：压缩包校验值。
- `RELEASE_AUDIT.json`：逐文件清单、散列、许可类别及打包核对。
- `RELEASE_NOTES.md`：可直接粘贴的发布说明。

## 更新仓库源码

1. 从 GitHub 仓库拉取当前 main，建立 `release/v0.5.0` 分支。
2. 解压新的 source ZIP，将 BatteryPet 内的文件同步到仓库根目录。不要把 ZIP 本身提交为源码，不要覆盖已有 Git 历史。
3. 对照文件清单审查删除项：旧来源不明的 `hid_win.py` 必须被新版替换；旧第三方角色美术和旧构建目录不要保留在当前发布树。
4. 提交说明可用 `Prepare BatteryPet v0.5.0 preview release`，推送并创建 PR，合并审核后的代码。
5. 不使用 Git 命令时，可通过网页 Add file → Upload files 上传解压后的源码内容，并通过网页删除旧的、清单外的文件。网页上传不会自动删除已有文件。

## 创建 Release

1. 打开 [新建 Release](https://github.com/sadwind227/BatteryPet/releases/new)。
2. Tag 填 `v0.5.0`，Target 指向包含本版源码的提交；标题填 `BatteryPet v0.5.0 — Preview`。
3. 粘贴 `RELEASE_NOTES.md` 正文，上传两个 ZIP、`SHA256SUMS.txt` 和 `RELEASE_AUDIT.json`。
4. 勾选 **This is a pre-release**，先保存 draft，核对附件和说明后发布。
5. 下载已上传附件核对 SHA-256，确认 README 图片、下载入口和许可文件可见。

GitHub 自动生成的 Source code 附件取自标签对应的仓库内容；确保标签源码与本地 source ZIP 一致。发布已有版本后，不移动旧标签；后续修复使用新版本号。

## 本地复现

```powershell
powershell -ExecutionPolicy Bypass -File scripts/package.ps1 -Version 0.5.0
```

脚本从审核清单创建干净源码副本，测试、发布、核对文件并计算校验值。已有同名 ZIP 时拒绝覆盖。代码、图片和 .NET 启动器的许可依据见 [权利检查](COPYRIGHT_AUDIT.zh-CN.md)。

官方步骤：[添加文件](https://docs.github.com/en/repositories/working-with-files/managing-files/adding-a-file-to-a-repository)、[管理 Release](https://docs.github.com/en/repositories/releasing-projects-on-github/managing-releases-in-a-repository)。
