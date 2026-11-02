# 小鲸鱼桌宠的设计参考

本版参考以下项目公开描述的交互，使用 C# / GDI+ 独立实现本应用的角色绘制、输入状态机、透明窗口和设置界面。

- [DeepSeek Balance Whale Widget](https://github.com/MeteorNOX/DeepSeek-Balance-Whale-Widget)：参考点击气泡、角色大小设置、右键菜单、拖动及位置记忆。其 README 明确说明美术和音效不在代码 MIT 许可覆盖范围内；本版未引入其图片、动图、音效或程序代码。
- [Coopanion 桌宠说明](https://github.com/Pal-AI-Lab/Coopanion/blob/main/packages/cortico-world-desktop-pet/README.md)：参考角色、气泡与桌面窗口分离，点击与拖动分开判断，以及外观设置。未引入其 AGPL 源码或角色素材。

本应用的角色由 `PetRenderer` 使用贝塞尔曲线、渐变和图形绘制，不依赖下载素材。薄荷海、晴空蓝、暮光紫共用一套形状和动作。电量采集与角色渲染分离，之后可以新增自有角色渲染器。

用户交互规则：启动、悬停、自动刷新和拖动均不显示电量气泡；左键点击角色显示电量并请求刷新，气泡到期或点击气泡后收起；低电量系统提醒沿用设备设置，不通过自动弹出电量气泡打断用户。

## 0.3.0 可选示例素材

0.3.0 开发期间曾加入 Coopanion 静态形象预览图作为可选素材。该素材已在首次公开版本发布前移除，当前随附角色改为项目维护者提供的像素风鲸鱼女仆图片，见 `Examples/CustomPet/SOURCE.md`。默认角色仍是 BatteryPet 自绘小鲸鱼。

DeepSeek-Balance-Whale-Widget 的 PROVENANCE.md 明确将 assets 限定为运行原插件使用，因此本版未复制其图片与音效；内置轻触音由本程序合成。
