---
name: lx-game
description: 开发 Game 产品的玩法、世界、Feature、场景与产品结构；UI、输入和框架内核不触发。
---

# LXFramework 产品开发

按任务只读取必要 reference：创建游戏、世界、Feature 或原生节点用 `references/product-structure.md`；玩法、纵向切片、重开和产品 smoke 用 `references/gameplay-validation.md`。

普通 JSON 内容与静态资源登记改用 `$lx-content`；页面、导航与视觉证据改用 `$lx-ui`；输入清单与动作绑定改用 `$lx-input`；动态资源租约、材质/图集、PackedScene 或池闭环改用 `$lx-resources`。只有任务真实跨越这些职责时才组合 Skill。

产品代码只依赖 LXFramework，通过注入的 `LX` 调用服务；不建立产品管理器转发框架 API，不使用全局上下文或动态 `GD.Load`。新结构使用 `./lx.ps1 create ...`，事实源和非生成代码由人工维护，生成输出交给工具。

迭代时一次运行 `./lx.ps1 check <changed-path> [...]`，交付前运行 `./lx.ps1 validate`。
