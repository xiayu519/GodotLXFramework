---
name: lx-ui
description: 创建或修改 Game 产品页面、导航、UI payload 与视觉证据；玩法、输入和框架 UI 内核不触发。
---

# LX 产品 UI

完整读取 `references/ui-workflow.md`。先沿用明确的需求和现有 UI 约定；只有无法从现有事实解决、且会改变导航、模态/暂停或数据契约的选择才询问，未决部分不先创建。

产品 UI 通过注入上下文使用 `LX.UI`，不建立 UI 管理器或全局上下文。修改框架 UI 公开 API 才额外使用 `$lx-framework`；真正涉及 Luban 上游表时才使用 `$lx-data`。

修改后运行相关 `check` 并只比较受影响的产品视觉 target；达到根 `AGENTS.md` 的仓库级门禁时才运行 `validate`。只有人工确认设计变化后才可 approve 基准。
