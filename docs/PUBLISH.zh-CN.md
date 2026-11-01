# 上传到 sadwind227/BatteryPet

当前远程仓库已有 main 分支及 MIT LICENSE。以下步骤不需要重新创建仓库，也不要用强制推送覆盖现有历史。

## 当前交付

- `上传准备/BatteryPet/`：按白名单复制的源码、文档、测试、示例和打包脚本。不含旧构建目录或个人数据。
- `CHANGELOG.md`：本地里程碑记录，最新版本 0.4.1。
- `RELEASE_NOTES.md`：首次预览版发布说明草稿。
- `artifacts/releases/v0.4.1/`：本地 Windows 程序审阅 ZIP 和 SHA256 校验值。

## 先确认完整代码的发布条件

根目录 MIT 文件与远程一致，但不能覆盖第三方内容。`hid_win.py` 的再分发许可仍待确认，相关证据与处理路径在 THIRD_PARTY_NOTICES.md。

完整源码和带此文件的 EXE/ZIP 暂不要直接公开发布。可以先上传原创 README、版本记录、发布状态说明及自绘预览图；完整代码待授权确认或替换通信层并验证后再上传。不要通过删掉第三方声明来规避这个问题。

## 网页上传源码（发布条件解决后）

1. 打开 https://github.com/sadwind227/BatteryPet 并登录自己的账号。
2. 进入 **Code → Add file → Upload files**。
3. 在本机打开 `上传准备/BatteryPet`，把里面的文件和目录拖进去。不要把最外层 BatteryPet 文件夹整体再套一层，不要上传 ZIP 当源码。
4. 确认网页顶层有 README.md、README.zh-CN.md、CHANGELOG.md、BatteryPet.csproj、docs 等。LICENSE 与仓库原文件一致即可。
5. 提交说明填：`Prepare BatteryPet v0.4.1 preview source and documentation`。
6. 可选择新建分支 `prepare-v0.4.1` 并提交，在网页比较文件无误后创建 Pull request，合并到 main；个人仓库也可在确认后直接提交 main。
7. 查看 README 的语言切换、图片和文档链接是否正常。首次只有 v0.4.1 对应的新提交，不需要给旧里程碑补造标签。

当前源码包文件数低于网页一次上传 100 个文件的限制；程序 ZIP 放 Releases，不放源码区。

## 创建程序 Release（完整包许可与文件检查完成后）

1. 在仓库右侧进入 **Releases → Draft a new release**。
2. Tag 填 `v0.4.1`，选择创建该标签，Target 选已合并源码的 `main`。
3. Title 填 `BatteryPet v0.4.1 — Preview`。
4. 从 RELEASE_NOTES.md 复制正文；授权事项已解决时同步更新正文和第三方声明，不能保留“已解决”与“待确认”互相矛盾的表述。
5. 附件上传 Windows ZIP 和 SHA256SUMS.txt。当前生成的 REVIEW 包只供本地审阅，确认可公开分发后重新打包或改名，并重新生成校验值。
6. 勾选 **This is a pre-release**。先 **Save draft** 检查附件与说明，最后由你点击 **Publish release**。

用户应下载程序 ZIP，GitHub 自动生成的 Source code ZIP 是源码，不是安装程序。

## 后续版本

1. 同步更新 BatteryPet.csproj 的 Version、CHANGELOG 和发布说明，例如修复版 0.4.2。
2. 构建、测试并运行 `scripts/package.ps1 -Version 0.4.2`。
3. 提交源码、推送，创建对应的 `v0.4.2` 标签和新 Release，不移动已经发布的旧标签。
4. 旧 ZIP 保留在旧 Release 中；不要把多个“修复版/美化版”目录加入源码仓库。

官方步骤：[网页添加文件](https://docs.github.com/en/repositories/working-with-files/managing-files/adding-a-file-to-a-repository)、[创建与管理 Release](https://docs.github.com/en/repositories/releasing-projects-on-github/managing-releases-in-a-repository)。
