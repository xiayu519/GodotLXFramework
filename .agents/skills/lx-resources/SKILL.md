---
name: lx-resources
description: 处理 LX.Res 租约、动态绑定、PackedScene/节点池与释放闭环；静态注册不触发。
---

# LX 资源生命周期

按任务只读取必要 reference：获取入口、租约持有者和路径规范用 `references/lease-ownership.md`；动态材质、纹理或 UI 图集用 `references/dynamic-bindings.md`；PackedScene 实例和节点池用 `references/packed-scene-pools.md`；释放与重开证据用 `references/resource-validation.md`。

所有权必须绑定明确 `Lifetime`，裸 `Resource` 不得比租约活得更久；重开后活动租约与池借出回到基线。

任务只有普通 JSON 内容或静态 `create res` 登记时改用 `$lx-content`，不加载本 Skill。

修改框架资源 API 时同时使用 `$lx-framework`；修改玩法或 UI 消费代码时只追加对应的 `$lx-game` 或 `$lx-ui`。禁止建立第二套资源注册表或动态 `GD.Load`/`ResourceLoader.Load*`。

修改后运行相关 `check`，需要重开闭环时检查资源与节点指标；达到根 `AGENTS.md` 的仓库级门禁时才运行 `validate`。
