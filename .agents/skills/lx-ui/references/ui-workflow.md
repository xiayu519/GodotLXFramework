# 产品 UI 工作流

创建页面使用：

```powershell
.\lx.ps1 create screen MainMenu main_menu
```

页面通过注入的 `LX.UI` 导航。全屏页面使用 `UILayer.Screen` 与 `NavigateAsync`；叠加弹窗使用 `UILayer.Popup` 与 `OpenAsync`，按需求决定 `Modal` 输入拦截和是否暂停游戏。

## 响应式布局

`UIScreen` 与弹窗场景根 `Control` 必须使用 `FullRect`，固定层级与布局写在 `.tscn`，不在代码中创建固定控件或硬编码位置。全屏页面默认按需要组合下列定位层，不创建无用途的空层：

- `FullLayer`：`FullRect`，承载背景、全屏内容或随可用分辨率展开的区域。
- `TopLayer`：`TopWide`，承载顶部状态和导航。
- `MidLayer`：`FullRect` 的 `CenterContainer`，承载居中内容。
- `BottomLayer`：`BottomWide`，承载底部操作区。
- `Overlay`：`FullRect`，只用于覆盖提示、特效或局部遮罩。

这些层是允许互相覆盖的定位槽。页眉、正文、页脚必须互不覆盖时，改用 `VBoxContainer`：页眉与页脚按内容或最小尺寸，正文使用 `Expand + Fill`；横向和网格布局同理使用 `HBoxContainer`、`GridContainer`，不要手算子节点坐标。面向移动端或异形屏时，在定位层外增加由产品安全区数据驱动的 `SafeArea`/`MarginContainer`。同时检查窄屏、宽屏、本地化长文本和用户 UI Scale，不把设计分辨率当作唯一尺寸。

## 弹窗结构与过渡

新弹窗使用：

```powershell
.\lx.ps1 create popup ConfirmPurchase confirm_purchase
```

弹窗内容只占中间区域，但场景仍必须保留全屏外壳：`PopupRoot(FullRect) -> Scrim(FullRect) + CenterContainer(FullRect) -> MotionPivot -> DialogPanel`。`PopupRoot` 承担生命周期和模态输入，`Scrim` 承担背景变暗与空白区域命中；不得只创建一个局部居中面板，以免输入穿透或遮罩不能覆盖变化后的分辨率。

默认弹窗继承 `UIPopupScreen`：进入时只对 `MotionPivot` 做 `0.92 -> 1.0` 的缩放与透明度渐入，同时渐入 `Scrim`；退出时做更短的轻微缩小与淡出。不得缩放全屏根节点或遮罩。标准过渡使用真实经过时间、观察取消令牌，并在布局完成后把 pivot 设到内容中心；UIService 会在进入完成后交付焦点。只有设计明确要求时才覆盖默认时长或曲线；产品提供减少动态效果选项时，将过渡时长置零。

弹窗关闭必须继续走 `RequestClose`、强类型结果或 `LX.UI.RequestBackAsync`，不自行释放节点。修改弹窗动效后，视觉捕获通过 `IVisualCaptureReady` 等到确定的最终状态，不能截取过渡中间帧。

层级、暂停语义或数据接口未确定时，只问会改变结构的 1–2 个问题并停止。若确需确认现有产品/UI 结构，最多运行一次 `inspect`；不要运行 `create`、`--help`、重复 `inspect`、全仓 API 搜索或预先设计未确认的数据层。

UI 优先接收只读 payload/view data，不直接绑定尚未确定的领域实现。普通 payload 不触发 Luban；只有用户明确要求配置表或上游 schema 时才使用 `$lx-data`。

已确定实现后只编辑产品页面、场景和清单，不手改生成 Catalog。状态型 UI 在活动 Editor/Debug 会话中可用 `$lx-runtime-observe` 查询 `runtime snapshot ui --json`；视觉目标登记到 `visualTargets` 并用 `checkPaths` 声明影响范围。纯 Control 快速门禁使用确定性 `SemanticControl`；需要 `Sprite2D`、shader、真实字体、hover、`VideoStream` 或 Godot 合成结果时使用 `RenderedViewport`，通过 `IVisualCaptureReady` 固定异步/视频状态，并按目标声明 pointer、像素容差与最大变化比例。两种结果不得互相冒充，然后运行：

```powershell
.\lx.ps1 check <changed-path> [...]
.\lx.ps1 visual compare <target-id>
.\lx.ps1 validate # 仅在根 AGENTS.md 定义的仓库级门禁运行
```

普通 UI 迭代不运行 `visual compare product`；它只用于内容冻结或仓库级门禁。视觉基准只有人工确认设计变化后才可 `visual approve`。
