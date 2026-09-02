---
name: lx-framework
description: 修改或审查 LXFramework.Core/LXFramework 内核、公开 API 与运行时契约；不处理 Game 产品。
---

# LXFramework 内核开发

根 `AGENTS.md` 是仓库级权威。本 Skill 只处理框架源码与公开 API，不处理具体 Game 产品实现。

按任务只读取必要 reference：模块/API 定位用 `references/architecture.md` 或 `references/modules.md`；抽象与通信取舍用 `references/design-decisions.md`；上下文、线程、生命周期和关闭用 `references/runtime-contracts.md`；内存诊断才读取 `references/memory-safety.md`。资源 API 与租约所有权改用 `$lx-resources`。

先取得一处直接源码证据再修改。保持 Core 纯 C#、Godot 适配层不依赖产品层，不引入第二套事件、时钟、生命周期、资源、场景、对象池或 UI 系统。公开 API 改动必须审查 `lx api check` 差异。

修改后把明确路径一次交给 `./lx.ps1 check`；达到根 `AGENTS.md` 的仓库级门禁时才运行 `./lx.ps1 validate`。
