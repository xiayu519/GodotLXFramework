---
name: lx-game
description: 开发 Game 产品的驱动架构、玩法、世界、Feature、场景与产品结构；UI、输入和框架内核不触发。
---

# LXFramework 产品开发

按任务只读取必要 reference：新游戏驱动架构、事件脚本、通用模块、批量内容或产品缺陷归因用 `references/product-architecture.md`；创建游戏、世界、Feature 或原生节点用 `references/product-structure.md`；玩法、架构契约切片、重开和产品 smoke 用 `references/gameplay-validation.md`。

只读驱动判断先给结论并保持有界：读取本 Skill 与对应的一份 reference 后，只有现有产品事实会改变结论时才查看入口或清单；证据足够即停止，不为提出方案遍历 UI、输入、场景、Public API 或加载其他 Skill。无重复流程和批量内容的小游戏应明确写出“不需要事件脚本”，给出少量类型化模块或确定性状态方案后停止。

普通 JSON 内容与静态资源登记改用 `$lx-content`；页面、导航与视觉证据改用 `$lx-ui`；输入清单与动作绑定改用 `$lx-input`；动态资源租约、材质/图集、PackedScene 或池闭环改用 `$lx-resources`。只有任务真实跨越这些职责时才组合 Skill。

产品代码只依赖 LXFramework，通过注入的 `LX` 调用服务；不建立产品管理器转发框架 API，不使用全局上下文或动态 `GD.Load`。新结构使用 `./lx.ps1 create ...`，事实源和非生成代码由人工维护，生成输出交给工具。

迭代时一次运行 `./lx.ps1 check <changed-path> [...]`，并只补充受影响的产品场景；达到根 `AGENTS.md` 的仓库级门禁时才运行 `./lx.ps1 validate`。
