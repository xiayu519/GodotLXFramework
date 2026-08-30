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

参数不确定时只运行对应 `create ... --help`，不读脚手架源码。现有产品的参数完整时，先读 `content/game/game-manifest.json`，用 `sourceRoot` 定位产品局部 `AGENTS.md`；不要猜 `src/Game` 或搜索产品根。连同 `godot_project/AGENTS.md`、`content/AGENTS.md`、`scene/AGENTS.md` 一次读取必要局部规则后，直接运行命令。

`create node <Class> <GodotBase> <id>` 的输出固定落在 `sourceRoot/Nodes/<Class>.cs` 与 `scene/nodes/<id>.tscn`，命令已保证显式 `ILXContextReceiver` 注入。成功回显完成存在性和重复校验后，不再运行 `inspect`、`git status`、`rg --files`，也不搜索或重读生成的脚手架。把回显路径一次交给 `check`，最后运行 `validate`。
