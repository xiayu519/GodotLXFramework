# Sol/high 模型评测

## 保证配置

唯一必须通过的配置是 `gpt-5.6-sol` + `high`。普通任务与 Plan mode 均使用该配置。

## 评测方法

- 每次评测使用相同提示、相同隔离副本、相同授权范围和相同验收器。
- 路由题验证是否读取正确的 `AGENTS.md`/Skill、是否遵守只读与重要歧义边界。
- outcome 题必须执行真实仓库操作，并由确定性文件断言与 `./lx.ps1 validate` 判定，不以模型自述成功为准。
- 当前 schema 共 13 项：两项 smoke，以及 UI 重要歧义、原生节点上下文、纵向切片、资源注册、生成输入纪律、Luban 路由、运行时诊断、人工工具发现、存档迁移审查、旧 LX 升级规划和跨引擎复刻规划。
- 每次保存模型、reasoning、用例、成功率、输入/输出/总 token、工具调用、重试、延迟与失败原因。

## 通过标准

- Sol/high 必须通过全部硬性用例与效率预算；否则工作流不具备兼容性。
- 节省 token 只有在任务结果和证据同时合格时才记为改进。
- 静态检查通过不等于模型兼容；未运行真实模型时只能写“待实测”。

Sol/high 完整套件用于工作流发布或用户明确要求的最终验证；普通局部文档变更只跑静态检查，影响路由、授权、Skill 触发或完成语义时至少跑 smoke eval。

先执行不调用模型的确定性预检：

```powershell
.\.agents\skills\lx-codex-workflow\scripts\run-model-evals.ps1 -Suite full -PreflightOnly
```

只有预检通过且已确认外部额度消耗，才去掉 `-PreflightOnly` 运行 Sol/high 完整套件。旧三项结果不能自动外推为扩展后 11 项通过。
