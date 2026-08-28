# LXFramework 工作区指令

使用中文沟通；代码标识、命令、路径、API 和原始日志保持原样。

LXFramework 是 Codex 优先的 Godot 4.6 C# 框架。默认基线为 `gpt-5.6-terra` + `high`，Plan mode 使用 `xhigh`；同时支持 `gpt-5.6-terra` + `xhigh` 与 `gpt-5.6-sol`。更低模型或 reasoning 不保证。

## 工作区布局

- `godot_project/` 是唯一 Godot 工程根，`project.godot`、`res://`、框架源码、产品源码和内容清单都以它为边界。
- 外层根目录保存 Git、Codex 工作流、Project Knowledge 与公开文档；`game_design/` 是与 `godot_project/` 同级的 Luban 策划数据源，不放入 Godot 资源树。
- 从外层统一运行 `./lx.ps1`；包装器会转发到 `godot_project/lx.ps1`。`check` 的相对路径可写成 `godot_project/src/...` 或相对 Godot 根的 `src/...`。

## 授权边界

- 回答、解释、审查、诊断或规划：读取必要材料并报告，不实施未请求的修改。
- 修改、构建或修复：直接完成范围内的本地修改和非破坏性验证。
- 存在会改变目录、架构、行为或结果且无法从事实源消除的歧义时集中提问；外部写入、破坏性操作、付费行为或实质扩展范围先确认。

## Codex 原生路由

- 修改目标前读取沿途最近的 `AGENTS.md`；较近规则优先。
- LXFramework/Godot C# 框架或游戏开发使用 `lx-dev`；Codex 指令、Skill、Project Knowledge、模型配置或 eval 使用 `lx-codex-workflow`。
- Skill 创建或修改使用 `skill-creator`；OpenAI/Codex/模型事实或提示词变更先使用 `openai-docs`。
- Skill 命中后只读其 `SKILL.md` 和当前任务必需的 reference，不预加载整个仓库文档。

## 仓库不变量

- `godot_project/src/LXFramework.Core` 是纯 C#；`godot_project/src/LXFramework` 是 Godot 适配层且禁止依赖产品层；产品代码只能反向依赖 LXFramework。
- 禁止反射发现、服务定位器，以及第二套事件总线、时钟、资源注册表、生命周期容器、对象池、场景管理器或 UI 管理器。
- `godot_project/content/` 清单是运行时内容事实源；Luban 表以 `game_design/schema` 与 `game_design/data` 为上游事实源，并通过 `./lx.ps1 data` 生成到 `content/data/luban` 和产品 `Generated/Luban`。禁止手改任何生成输出。
- 产品节点通过注入上下文调用 `LX.UI.*`、`LX.Res.*` 等服务；禁止全局上下文和游戏代码中的动态 `GD.Load`/`ResourceLoader.Load*`。
- `godot_project/scene/main.tscn` 及其 UID 是固定入口；自动化 Godot 使用 `--headless --audio-driver Dummy`。
- 框架公开枚举、枚举成员和公开常量必须说明语义；UI 视觉变化先 `visual compare`，只有人工确认设计变化后才可 `visual approve`。

## 最短执行与完成

- 新结构使用 `./lx.ps1 create game|world|feature|screen|content|input|res|node`；任意 Godot 原生节点使用 `create node <Class> <GodotBase> [id]` 保留显式 LX 上下文注入。
- Windows shell 每次只运行一个 `rg`、`Get-Content` 或框架命令；不用 `;`、循环或串接多个读/搜命令，读取中文文本显式指定 UTF-8。
- 迭代时把本次明确变更路径一次传给 `./lx.ps1 check <changed-path> [...]`；跨模块且需结构概览时先运行 `./lx.ps1 inspect`。
- 修改任务交付前运行一次 `./lx.ps1 validate`。完成表示结果存在、必要验证通过或无法验证项已说明，且范围内没有剩余必做工作；满足后停止。
- `./lx.ps1 export windows` 依赖同版本 Godot export templates，不属于无模板环境的默认 `validate`；模型完整矩阵会产生外部额度消耗，未获得确认只运行 preflight。
