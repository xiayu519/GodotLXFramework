---
name: lx-input
description: 创建或修改 Game 产品输入清单、动作绑定与生成入口；玩法、UI 和 Luban 不触发。
---

# LX 产品输入

完整读取 `references/input-workflow.md`。输入事实只改上游清单并通过 `lx create input` 刷新生成绑定，禁止手改 Generated。

产品代码通过注入的 `LX.Input`/`LX.Actions` 消费动作，不建立第二套输入映射。修改框架输入 API 才额外使用 `$lx-framework`；修改使用该动作的玩法才额外使用 `$lx-game`。

修改后把命令回显路径一次交给 `check`；达到根 `AGENTS.md` 的仓库级门禁时才运行 `validate`。
