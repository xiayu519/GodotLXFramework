# 人工工具与等价 CLI

只读询问 Godot 编辑器入口及等价命令时，本文件就是完整契约；按下表回答后停止，不搜索 README、eval、插件或工具源码。

| 需求 | Godot 编辑器 | 仓库外层命令 |
|---|---|---|
| 创建 UI 页面 | 底部 `LX Tools` 的 **LX 开发工具** → **创建内容…** → `UIScreen` | `./lx.ps1 create screen <ClassName> [id]` |
| 检查当前场景资源 | **LX 开发工具** → **场景依赖** | 日常 `./lx.ps1 check <changed-path> [...]`；完整门禁 `./lx.ps1 validate` |
| 比较 UI 视觉基准 | 普通工具栏不提供 | 框架：`./lx.ps1 visual compare ui_components`；产品迭代：`./lx.ps1 visual compare <target-id>`；完整门禁：`visual compare product` |

面板还提供 **生成策划数据** 和 **打开策划数据目录**。视觉基准只有人工确认设计变化后才运行 `visual approve`；Windows export templates 就绪后使用 `./lx.ps1 export windows`。
