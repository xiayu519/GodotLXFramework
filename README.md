# PlaneFight 示例

`sample` 分支用一个完整的飞机战斗第一关展示和验证 LXFramework 的实际产品开发能力。LXFramework 的定位、环境配置、通用 API 和 Codex 开发方式以 [`main` 分支](https://github.com/xiayu519/LXFramework/tree/main)为准；本文只介绍 PlaneFight 的示例范围、框架能力映射、代码入口和验证方式。

## 示例范围

- 开始、退出、战斗 HUD 和胜负结算页面。
- 第一关完整战斗流程，包括敌机、Boss、掉落、武器切换和自动射击。
- 导弹、冰冻导弹、核弹、护盾及对应表现和音效。
- 胜利、死亡、重新开始和退出闭环。
- 连续重开时的 UI、Feature、音频、资源租约、运行时节点和对象池闭合检查。
- 独立的 Framework Lab smoke，用不污染主玩法的方式验证更多公开 API。

## 运行与操作

使用 Godot 4.7.2 .NET 打开 `godot_project/project.godot` 后按 `F5`，也可以从仓库根目录运行：

```powershell
.\lx.ps1 run
```

默认操作：

| 操作 | 按键 |
|---|---|
| 移动 | `W` / `A` / `S` / `D` 或方向键 |
| 导弹 | `Q` |
| 冰冻导弹 | `E` |
| 核弹 | `Space` |
| 护盾 | `Shift` |

输入提示由 `InputCatalog` 和当前绑定生成，不在 UI 中重复维护按键文本。

## 使用的框架能力

| 框架能力 | 示例中的用途 | 入口 |
|---|---|---|
| Game / World | 产品启动、游戏循环和第一关世界 | `godot_project/script/PlaneFight/GameRoot.cs` |
| Feature | 独立创建和释放战斗模块 | `godot_project/script/PlaneFight/Features/LevelOneBattle/LevelOneBattleFeature.cs` |
| UI | 开始、HUD、结算页面和强类型返回值 | `godot_project/script/PlaneFight/UI/` |
| Luban / Content | 第一关、玩家、敌人、Boss、掉落和数值配置 | `game_design/schema/plane_fight.xml`、`game_design/data/plane_fight_level.json` |
| ResCatalog / AssetLease | 美术、音频和场景资源的类型化加载与释放 | `godot_project/content/res/res-manifest.json` |
| NodePool | 子弹、尾焰、掉落、敌机、Boss、爆炸和冰冻特效复用 | `LevelOneBattleFeature.cs`、`PooledBattleViews.cs` |
| InputCatalog / InputContext | 移动、武器输入、上下文约束和按键提示 | `godot_project/content/input/input-manifest.json` |
| Audio | 音乐、射击、爆炸、Boss、胜负和 UI 音效 | `LevelOneBattleFeature.cs`、`GameRoot.cs` |
| Lifetime / Events / Scheduler / Actions | 战斗生命周期、事件、Boss 调度和核弹表现编排 | `GameRoot.cs`、`LevelOneBattleFeature.cs` |
| Random / Metrics / Diagnostics | 确定性玩法、业务指标、连续重开和资源闭环证据 | `GameRoot.cs`、`LevelOneBattleFeature.cs` |
| Framework Lab | GameFlow、StateMachine、Action 树、暂停时钟、WorldEvents、资源/场景预载、AssetBinding 和 PackedScene 生命周期验证 | `godot_project/script/PlaneFight/Showcase/PlaneFightApiShowcase.cs` |

Framework Lab 只由 `api_showcase` product smoke 运行，用来炫示和验证不适合强行塞进第一关主玩法的能力。

## 主要入口

```text
godot_project/script/PlaneFight/GameRoot.cs
  └─ 开始页面 → 创建第一关 Feature → HUD → 结算 → 重开/退出

godot_project/script/PlaneFight/Features/LevelOneBattle/
  ├─ LevelOneBattleFeature.cs   战斗流程与玩法
  ├─ BattleActors.cs            运行时战斗数据
  └─ PooledBattleViews.cs       可复用 Godot 节点

godot_project/script/PlaneFight/Showcase/
  └─ PlaneFightApiShowcase.cs   独立 Framework Lab smoke

godot_project/script/PlaneFight/Nodes/
  └─ ShowcasePulse.cs           LX 上下文注入的 PackedScene/池化节点

game_design/
  ├─ schema/plane_fight.xml      Luban schema
  └─ data/plane_fight_level.json 第一关策划数据
```

产品事实源：

- `godot_project/content/game/game-manifest.json`
- `godot_project/content/features/feature-manifest.json`
- `godot_project/content/ui/ui-manifest.json`
- `godot_project/content/input/input-manifest.json`
- `godot_project/content/res/res-manifest.json`
- `game_design/schema/plane_fight.xml`
- `game_design/data/plane_fight_level.json`

生成的 Catalog、UI bindings、Luban C# 和 `.bytes` 不应手动修改。

## 验证方式

`game-manifest.json` 声明了三个会自行退出的 Debug/headless product smoke：

| smoke | 验证内容 |
|---|---|
| `level_one` | 第一关战斗、武器能力、确定性胜利、对象池和资源释放 |
| `game_flow` | 开始/退出、连续胜利与死亡、重开、UI/Feature/音频/资源闭环 |
| `api_showcase` | 独立 Framework Lab 的公开 API 与生命周期闭环 |

从仓库根目录运行：

```powershell
.\lx.ps1 smoke product all
.\lx.ps1 inspect --product-coverage
.\lx.ps1 validate
```

`validate` 会生成并校验 Luban 数据、编译项目、运行 Core 测试、Godot framework smoke、三个 PlaneFight product smoke 和已登记视觉目标。当前 PlaneFight 尚未登记人工批准的产品视觉基准，因此 product visual 会明确显示为 skipped。

安装与 Godot 4.7.2 完全匹配的 Mono export templates 后，可以额外运行：

```powershell
.\lx.ps1 export windows
```

Windows 产物写入 `build/windows/`，导出验收会复用框架与产品 smoke 契约。
