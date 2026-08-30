# 架构

LXFramework 由两个运行时层和两个开发层组成：

- `godot_project/src/LXFramework.Core`：不依赖引擎的纯 C# 原语，包含事件、生命周期、时间、持久化、对象池、确定性随机、数据、诊断、音频编码和区块坐标。
- `godot_project/src/LXFramework`：Godot 适配与服务，包含资源、音频、内容、诊断、输入、本地化、场景、设置、UI 和世界流式加载。
- `godot_project/tools/LXFramework.Tools`：确定性的紧凑检查、脚手架、生成、校验、运行和无窗口冒烟测试；根 `lx.ps1 check` 负责选择最小迭代检查组合。
- `godot_project/tests/LXFramework.Core.Tests`：纯 C# 核心测试。

`godot_project/scene/main.tscn` 只持有一个 `LXHost`。Host 创建根生命周期和每种共享服务的唯一实例，通过 `LXContext` 显式组合，并注册生成的世界、功能、输入、UI 和资源目录。产品节点取得注入上下文后，使用 `LX.UI.*`、`LX.Res.*` 等入口；`LX` 不是全局静态对象或服务定位器。

`lx create game` 默认在 Godot 根内的 `script/<GameName>` 创建产品代码与局部指令，并把准确的 `sourceRoot` 和根命名空间写入游戏清单。内容清单和 Godot 场景仍使用各自的引擎目录。LXFramework 永远不能依赖产品层；即使 Godot 把脚本编译到同一程序集，`validate` 仍会检查依赖方向。

动态创建的世界、功能、区块和 UI 树都在进入活动场景树前递归注入 `LXContext`。产品代码使用生成的 ID 和目录，不直接拼动态资源路径。

生命周期顺序见 `runtime-contracts.md`，抽象放置与通信选择见 `design-decisions.md`。
