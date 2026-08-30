# 产品内容与静态资源登记

普通 JSON 内容表与已知路径的静态资源分别使用：

```powershell
.\lx.ps1 create content Item items
.\lx.ps1 create res player_sprite Texture2D res://content/art/player.png Cached
```

命令更新上游清单并生成强类型 Catalog；禁止直接编辑生成目录。Luban schema、策划数据或二进制表改用 `$lx-data`，动态绑定与释放闭环改用 `$lx-resources`。

已给出资源 ID、类型和路径时走固定快路径。若目标产品文件未指明，只读 `content/game/game-manifest.json`，使用其 `sourceRoot/GameRoot.cs`，不搜索产品目录。读取 `godot_project/AGENTS.md` 与目标产品目录最近的 `AGENTS.md`，运行一次 `create res`，按命令回显的 `ResCatalog.<Property>` 修改目标文件，然后一次 `check`、一次 `validate`。

不要运行 `inspect`、`--help`、全目录 API/文件搜索、脚手架源码搜索、生成后枚举或 `git status`。在目标文件加入下面这种固定 Catalog 获取代码只是内容接线，仍只使用 `$lx-content`；只有请求还改变实际玩法或 UI 行为时才追加相应 Skill。

产品节点通过注入上下文获取资源，并让其自身 `Lifetime` 持有租约：

```csharp
_scene = Lifetime.Own(LX.Res.Acquire(ResCatalog.FrameworkEntry)).Resource;
```

禁止 `GD.Load`、`ResourceLoader.Load*` 或第二套资源注册表。仅静态登记和上述固定消费方式不触发 `$lx-resources`；修改框架 Catalog/API 才额外使用 `$lx-framework`。
