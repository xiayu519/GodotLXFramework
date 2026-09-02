# 产品 UI 工作流

创建页面使用：

```powershell
.\lx.ps1 create screen MainMenu main_menu
```

页面通过注入的 `LX.UI` 导航。全屏页面使用 `UILayer.Screen` 与 `NavigateAsync`；叠加弹窗使用 `UILayer.Popup` 与 `OpenAsync`，按需求决定 `Modal` 输入拦截和是否暂停游戏。

层级、暂停语义或数据接口未确定时，只问会改变结构的 1–2 个问题并停止。若确需确认现有产品/UI 结构，最多运行一次 `inspect`；不要运行 `create`、`--help`、重复 `inspect`、全仓 API 搜索或预先设计未确认的数据层。

UI 优先接收只读 payload/view data，不直接绑定尚未确定的领域实现。普通 payload 不触发 Luban；只有用户明确要求配置表或上游 schema 时才使用 `$lx-data`。

已确定实现后只编辑产品页面、场景和清单，不手改生成 Catalog。状态型 UI 在活动 Editor/Debug 会话中可用 `$lx-runtime-observe` 查询 `runtime snapshot ui --json`；视觉目标登记到 `visualTargets`，然后运行：

```powershell
.\lx.ps1 check <changed-path> [...]
.\lx.ps1 visual compare <target-id>
.\lx.ps1 validate # 仅在根 AGENTS.md 定义的仓库级门禁运行
```

普通 UI 迭代不运行 `visual compare product`；它只用于内容冻结或仓库级门禁。视觉基准只有人工确认设计变化后才可 `visual approve`。
