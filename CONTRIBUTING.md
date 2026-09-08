# Contributing

## 开发约定

1. 使用 Godot 4.7.2 .NET 和 .NET SDK 8.0。
2. 从仓库外层运行 `.\lx.ps1`；不要在生成目录直接修改文件。
3. 新世界、Feature、UI、输入、资源和节点优先使用 `.\lx.ps1 create ...`。
4. 保持 `LXFramework.Core → 无 Godot`、`产品 → LXFramework → LXFramework.Core` 的单向依赖。
5. 公开枚举、每个枚举成员和公开常量写明使用语义与取舍。
6. 按 `AGENTS.md` 的风险边界选择本地 `check` 或完整 `validate`；提交本身不增加验证范围，不重复已通过且相关输入未变的检查。
7. 有意改变公开 API 时审查基线差异并运行 `.\lx.ps1 api update`；不要用更新基线掩盖可避免的破坏性变化。

## UI 基准

先运行 `.\lx.ps1 visual compare ui_components`。只有确认差异符合设计后才运行 `visual approve`，并在变更说明中解释基准为何变化。

## Luban

只修改 `game_design/schema` 与 `game_design/data`。运行 `.\lx.ps1 data` 后提交需要进入仓库的 `.bytes`、manifest 和产品生成代码；不要手改它们。

## 提交

提交应聚焦一个可验证结果，并更新 `CHANGELOG.md`。不要提交 `.godot/`、`.tools/`、`.lx/`、`bin/`、`obj/` 或本地导出产物。

GitHub Actions 仅保留 `workflow_dispatch`，普通 push、PR、标签和定时任务不启动云端验证。需要独立云端验收时人工运行工作流，可额外选择 soak/export；本地安装的工具与凭据不需要复制到 GitHub。

## Codex 工作流维护

模型与档位目录在 `.agents/skills/lx-model-eval/evals/evals.json`，项目默认配置必须与其一致。`AGENTS.md` 维护稳定工程边界，Skill 维护独立领域规则；说明文档不再复制一套强制指令。修改使用方式时同步检查 `README.md`、`Books/AI-Development-Workflow.md` 和 `CHANGELOG.md`。

修改 Skill 后运行结构校验和工作流检查；修改 eval schema 或 runner 后先运行以下离线检查，再按需要申请真实模型评测额度：

```powershell
.\.agents\skills\lx-model-eval\scripts\test-eval-contract.ps1
.\.agents\skills\lx-model-eval\scripts\run-model-evals.ps1 -PreflightOnly
```

不要把真实模型调用加入未获授权的提交或 CI 流程。提交版本化的精简验收基线，原始日志和隔离副本保留在 `.lx/model-evals/`；报告必须区分配置支持、路由、实现/行为通过与效率告警，不用部分用例、旧模型报告或模型自述冒充完整验收。修改验证门禁时运行完整 `.\lx.ps1 validate`，仅修改说明不触发模型或引擎验收。
