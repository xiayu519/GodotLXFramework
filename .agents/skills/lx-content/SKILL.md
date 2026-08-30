---
name: lx-content
description: 创建或修改 Game 产品普通 JSON 内容表与静态资源登记/Catalog；Luban 和动态资源生命周期不触发。
---

# LX 产品内容登记

完整读取 `references/content-registration.md`。普通内容与资源事实只改上游清单并通过 `lx create content|res` 刷新强类型 Catalog，禁止手改 Generated。

仅在已知产品文件加入生成 Catalog 的固定获取代码仍属于本 Skill，不追加玩法 Skill。只有继续改变玩法或 UI 行为时才追加对应 `$lx-game` 或 `$lx-ui`。Luban 使用 `$lx-data`；动态绑定、PackedScene 实例、租约诊断与释放闭环使用 `$lx-resources`。

修改后把命令回显路径一次交给 `check`，交付前运行 `validate`。
