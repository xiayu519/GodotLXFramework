# 产品结构

在干净框架基线上创建游戏：

```powershell
.\lx.ps1 create game MyGame
```

命令会创建产品局部 `AGENTS.md`、`GameRoot.cs` 和初始世界，更新 `content/game/game-manifest.json`，并刷新生成目录。添加结构使用：

```powershell
.\lx.ps1 create world Dungeon dungeon
.\lx.ps1 create feature Player player
.\lx.ps1 create node PlayerBody CharacterBody2D player_body
```

`create node` 用于不能继承 `LXNode` 的 Godot 原生节点，生成显式 `ILXContextReceiver` 注入；禁止改成全局上下文。稳定 ID 也是场景文件名。

参数不确定时先看对应 `create ... --help`；只有文档不足或命令异常才查脚手架源码。现有产品先读 `content/game/game-manifest.json`，用 `sourceRoot` 定位产品局部 `AGENTS.md`，不猜产品根；合并读取 `godot_project/AGENTS.md`、`content/AGENTS.md`、`scene/AGENTS.md` 等必要局部规则。

`create node <Class> <GodotBase> <id>` 输出位于 `sourceRoot/Nodes/<Class>.cs` 与 `scene/nodes/<id>.tscn`，生成显式 `ILXContextReceiver` 注入。命令成功后无需为了重复确认路径再做全仓搜索；需要审查注入、合并用户改动或解释失败时仍应检查实际文件和 diff。

首次 `check` 前为所有受影响的运行时路径确定验证映射，包括为登记而修改的 `content/game/game-manifest.json`。活动行为映射到 smoke/visual；纯未接入脚手架可用精确路径和可审查理由登记 `staticCheckPaths`。不得用宽泛通配或静态声明跳过真实玩法测试。路径合并后一次 `check`；用户明确要求最终验证或达到根门禁条件时运行 `validate`，已通过且无相关新改动的检查不重复。
