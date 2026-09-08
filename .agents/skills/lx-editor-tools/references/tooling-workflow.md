# 人工工具与等价 CLI

只读询问 Godot 编辑器入口及等价命令时，先用下表定位。一般说明无需扫描全仓；涉及实际版本、运行异常或文档与界面不一致时，有界核对插件或 CLI 的相应实现。

| 需求 | Godot 编辑器 | 仓库外层命令 |
|---|---|---|
| 创建 UI 页面 | 底部 **LX 开发工具** → **创建内容…** → **UI 页面** | `./lx.ps1 create screen <ClassName> [id]` |
| 创建 UI 弹窗 | 底部 `LX Tools` 的 **LX 开发工具** → **创建内容…** → `UI 弹窗` | `./lx.ps1 create popup <ClassName> [id]` |
| 检查当前场景资源 | **LX 开发工具** → **场景依赖** | 无一对一命令；工程改动用 `./lx.ps1 check <changed-path> [...]`，完整门禁用 `./lx.ps1 validate` |
| 查看操作问题 | **问题与结果**；**执行详情（仅在排错时查看）** 提供原始日志 | 命令追加 `--json` 获取结构化诊断 |
| 比较 UI 视觉基准 | 普通工具栏不提供 | 框架：`./lx.ps1 visual compare ui_components`；产品迭代：`./lx.ps1 visual compare <target-id>`；完整门禁：`visual compare product` |

面板还提供 **生成策划数据** 和 **打开策划数据目录**。视觉基准只有人工确认设计变化后才运行 `visual approve`；Windows export templates 就绪后使用 `./lx.ps1 export windows`。
