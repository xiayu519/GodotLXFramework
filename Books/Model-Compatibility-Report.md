# Terra / Sol 模型兼容性量化报告

验证日期：2026-08-27。Codex CLI：`0.144.1`。框架环境：Godot 4.6.3 .NET、.NET SDK 8.0.416。

## 结论

`gpt-5.6-terra/high`、`gpt-5.6-terra/xhigh`、`gpt-5.6-sol/high` 在 2026-08-27 的三项隔离基线上均为 3/3，通过率 100%，所有行为与效率预算均通过。因此该基线证明 LXFramework 达到最低 Terra/high 保证，并兼容 Terra/xhigh 与 Sol/high。

这不是对所有未来游戏需求的绝对证明。当前 eval schema 已扩展为 11 项，增加原生节点上下文、跨 world/feature/UI/input 纵向切片、资源注册、生成文件纪律、Luban、统一诊断、人工工具发现和存档审查。新增用例本轮只执行不调用模型的确定性 preflight；三配置完整矩阵尚未重新付费执行，因此不能把旧 3/3 写成新 11/11。

## 最终矩阵

| 配置 | 通过 | 输入 token | 其中缓存 | 未缓存输入 | 输出 token | 工具调用 | 重试 | 总时长 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| Terra/high | 3/3 | 253,023 | 193,792 | 59,231 | 3,754 | 15 | 0 | 148.65 s |
| Terra/xhigh | 3/3 | 360,572 | 297,472 | 63,100 | 5,751 | 19 | 1 | 189.81 s |
| Sol/high | 3/3 | 283,020 | 217,088 | 65,932 | 4,428 | 17 | 0 | 171.71 s |

写入用例的三个配置都创建了 7 个变更文件，包含当时根布局下的 `script/EvalGame/AGENTS.md`、`script/EvalGame/GameRoot.cs` 与初始世界；模型进程退出码和独立 `./lx.ps1 validate` 均为 0。2026-08-27 改为 `godot_project/` 内层工程后，eval schema 已同步为新路径。当前 11 项矩阵运行前仍会先做固定 Luban、`lx.command-report/v1`、`LX_ARCH_003` 负向探针、任意原生 node 脚手架及最终场景矩阵的确定性预检；未通过预检时不会启动付费任务。

2026-08-28，扩展后的 11 项 schema 已分别在产品工作区和干净框架工作区通过 `-Suite full -PreflightOnly`；该结果只证明夹具与确定性验收器可用，不代表新增模型用例已经通过。

评测器会先在受控临时副本执行“清空产品层、创建探针游戏、完整验证”的确定性预检；框架/产品清单项使用 `Framework|Product` scope，预检只移除产品项。夹具错误因此不会被计为模型失败，也不会重复启动付费任务。

## 优化 A/B

`Sol/high` 的重要 UI 歧义用例曾在用户已明确列出未决选择后继续读取实现。`lx-dev` 加入窄早停规则后，同一用例的前后结果为：

| 指标 | 早期 | 最终 | 变化 |
|---|---:|---:|---:|
| 总 token（输入 + 输出） | 151,444 | 25,366 | -83.3% |
| 未缓存输入 token | 38,184 | 24,399 | -36.1% |
| 输出 token | 2,668 | 967 | -63.8% |
| 工具调用 | 9 | 1 | -88.9% |
| 时长 | 78.23 s | 37.37 s | -52.2% |
| 重试 | 0 | 0 | 不变 |

最终行为是只读取命中的 `lx-dev` Skill，然后集中提问并停止；没有运行 `inspect`、读取 reference、搜索实现或修改文件。Terra/high 与 Terra/xhigh 的同一复测也都只使用 1 次工具调用，分别为 24,830 与 25,052 总 token。

## 方法与复现

- 每个模型/用例使用相同提示和全新 Git 隔离副本；已有产品仓库先在副本中恢复空产品基线。
- 写入用例在隔离副本内预授权本地操作；模型完成后由外部确定性断言与完整框架门禁评分。
- Apps、插件、浏览器、图像和多代理工具在评测中关闭，避免无关工具初始化污染结果。
- token 来自 Codex `turn.completed.usage`；兼容通过不依赖模型自述。

复现命令：

```powershell
.\.agents\skills\lx-codex-workflow\scripts\run-model-evals.ps1 -Suite full
```

任务、配置和预算事实源为 `.agents/skills/lx-codex-workflow/evals/evals.json`。本次当前工作流的原始本地报告为：smoke 两用例 `.lx/model-evals/20260827-034919/summary.json`，Terra 歧义复测 `.lx/model-evals/20260827-040203/summary.json`，Sol 歧义复测 `.lx/model-evals/20260827-040055/summary.json`。`.lx/` 是可再生报告目录，不进入版本库。
