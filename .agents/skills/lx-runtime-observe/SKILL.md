---
name: lx-runtime-observe
description: 维护或诊断 Editor/Debug 运行时状态桥、会话校验与有界 snapshot；普通游戏代码和 Capability 目录不触发。
---

# LX 运行时观测

完整读取 `references/runtime-observation.md`。桥保持只读、有界，所有响应必须匹配活动进程、心跳、`sessionId` 与 `generation`；内部传输 JSON 不能代替公开 CLI。

产品任务只在已有持续运行会话且实时状态与验收相关时追加本 Skill。修改协议后运行相关 `check` 和受影响的运行时 smoke；达到根 `AGENTS.md` 的仓库级门禁时才运行 `validate`。
