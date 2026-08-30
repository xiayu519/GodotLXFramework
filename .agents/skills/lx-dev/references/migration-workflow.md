# 游戏升级、移植与复刻

当任务要把旧 LX 游戏、其他 Godot 项目或其他引擎游戏带到当前 LXFramework 时读取。本流程迁移游戏语义与产品所有内容，不用旧项目覆盖当前框架、工具或 Codex 工作流。

## 选择模式

- `upgrade`：源是旧 LXFramework 或旧 Godot 产品；保留产品事实、源码、场景和授权资产，在当前 checkout 重新生成并适配 API。
- `port`：源代码和资源可读，但使用其他 Godot 架构或其他引擎；提取玩法、UI、输入、数据、存档、音频和资源语义，再映射到 LX。
- `remake`：主要依据成品、行为记录或跨引擎设计复刻；不机械翻译引擎代码，只复现已确认且获授权的行为与内容。

来源、模式或资产授权会改变结果且用户未给出时，先集中确认；否则直接生成只读计划：

```powershell
.\lx.ps1 capabilities migration --json
.\lx.ps1 migrate plan --source <directory|git-ref> --mode upgrade|port|remake
```

计划只写 `.lx/migration/`，按产品事实、产品源码、场景、资产、集成文件、生成物、构建产物、框架所有和人工审查分类；它不切换分支、不复制文件、不改源项目。当前 checkout 的确定性派生文件仍使用 `upgrade --plan|--apply`，不要把两种 upgrade 混为一谈。

## 实施边界

1. 新目标从当前 `main` 建产品分支；框架、工具、根文档和 Codex 工作流以目标 checkout 为准。
2. `upgrade` 迁移产品所有输入，丢弃生成物和构建产物；生成后编译定位手写 API 漂移。
3. `port`/`remake` 先形成 LX 能力映射和一个可玩的纵向切片；禁止把来源的服务定位器、事件总线、生命周期、资源、场景、对象池或 UI 管理器复制成第二套系统。
4. 跨引擎场景和脚本是语义输入，不是可直接提交的 LX 结构；资产只有在来源与许可明确时复用。
5. 存档格式、坐标/单位、时钟、随机性、输入边沿、动画时序和碰撞规则必须显式记录，否则只能标为未验证差异。

需要跨模块映射时运行 `inspect --product-coverage`。结果只说明静态使用情况；未使用的服务不是缺陷。目标为框架展示时，把非自然能力放入独立 Framework Lab 或 smoke，不污染主玩法。

## 验收闭环

迁移先完成一个纵向切片，再扩展剩余内容：

```powershell
.\lx.ps1 check <changed-path> [...]
.\lx.ps1 smoke product all
.\lx.ps1 visual compare product
.\lx.ps1 validate
```

状态型产品应通过现有 `LX.Metrics`/结构化日志暴露少量业务事实，例如游戏状态、生命、分数、关键库存/冷却、活动实体和池借出数；不新建诊断服务。用户或开发会话已经启动可持续运行的 Editor/Debug 实例时，AI 应主动运行 `runtime status --json`，再按任务读取 `runtime snapshot ui|features|resources|input|metrics --json`，而不是只凭截图或日志推测；运行时桥只读并要求当前 `sessionId/generation`。没有活动会话时明确说明未取得实时快照，并以可退出的 product smoke 验证确定场景，不能声称已经观察运行状态。

产品 smoke 由 `game-manifest.json` 的 `productSmokes` 声明，Debug 与 export 共用同一 marker/timeout 契约；旧清单的 `exportSmokes` 仍兼容，但不能与新字段同时声明。产品视觉由 `visualTargets` 声明，基准只有人工确认后才能 approve。

交付证据至少包括：框架所有文件未被来源覆盖、生成/构建产物未迁移、纵向切片行为通过、产品 smoke、需要的实时快照、产品视觉结果、重开资源闭合和最终 `validate`。无法合法读取来源、关键行为无判定标准或资产授权不明时停止并请求方向。
