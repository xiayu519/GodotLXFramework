# LXFramework 工作区指令

使用中文沟通；代码标识、命令、路径、API 和原始日志保持原样。

LXFramework 是 Codex 优先的 Godot 4.7.2 C# 框架。

## 工作区布局

- `godot_project/` 是唯一 Godot 工程根，`project.godot`、`res://`、框架源码、产品源码和内容清单都以它为边界。
- 外层根目录保存 Git、Codex 工作流、Project Knowledge 与公开文档；`game_design/` 是与 `godot_project/` 同级的 Luban 策划数据源，不放入 Godot 资源树。
- 从外层统一运行 `./lx.ps1`；包装器会转发到 `godot_project/lx.ps1`。`check` 的相对路径可写成 `godot_project/src/...` 或相对 Godot 根的 `src/...`。

## 仓库不变量

- `godot_project/src/LXFramework.Core` 是纯 C#；`godot_project/src/LXFramework` 是 Godot 适配层且禁止依赖产品层；产品代码只能反向依赖 LXFramework。
- 禁止反射发现、服务定位器，以及第二套事件总线、时钟、资源注册表、生命周期容器、对象池、场景管理器或 UI 管理器。
- 产品实现前映射 `LX` 能力；禁止只转发框架 API 的产品管理器。可调数据使用 Luban，高频节点使用 `NodePool<TNode>`。
- 新建、移植或复刻游戏先确定驱动架构；同构流程用脚本/数据驱动通用模块，禁止逐内容硬编码。源码可读时优先语义复用合理架构；切片只验证架构契约，随后批量验证。
- 可重开流程连续覆盖死亡、胜利和重开；每轮验证 UI、Feature、音频、资源租约及池借出闭合，预热后节点、资源和产品资产不持续增长。
- `godot_project/content/` 清单是运行时内容事实源；Luban 表以 `game_design/schema` 与 `game_design/data` 为上游事实源，并通过 `./lx.ps1 data` 生成到 `content/data/luban` 和产品 `Generated/Luban`。禁止手改任何生成输出。
- 产品节点通过注入上下文调用 `LX.UI.*`、`LX.Res.*` 等服务；禁止全局上下文和游戏代码中的动态 `GD.Load`/`ResourceLoader.Load*`。
- `godot_project/scene/main.tscn` 及其 UID 是固定入口；自动化 Godot 使用 `--headless --audio-driver Dummy`。
- 框架公开枚举、枚举成员和公开常量必须说明语义；UI 视觉变化先 `visual compare`，只有人工确认设计变化后才可 `visual approve`。

## 最短执行与完成

- 新结构使用 `./lx.ps1 create game|world|feature|screen|content|input|res|node`；任意 Godot 原生节点使用 `create node <Class> <GodotBase> [id]` 保留显式 LX 上下文注入。
- PowerShell 读取中文文本显式指定 UTF-8。
- 迭代时把本次明确变更路径一次传给 `./lx.ps1 check <changed-path> [...]`；跨模块且需结构概览时先运行 `./lx.ps1 inspect`。
- 修改交付前运行一次 `./lx.ps1 validate`。
- `./lx.ps1 export windows` 依赖同版本 Godot export templates，不属于无模板环境的默认 `validate`；完整工作流 outcome eval 会产生外部额度消耗，未获得确认只运行 preflight。
