---
name: lx-project-knowledge
description: 维护版本化 .codex/memory Project Knowledge；不记录源码事实或临时进度。
---

# LX Project Knowledge

完整读取 `references/project-knowledge.md`。只读询问分类、索引或状态规则时依据 reference 直接回答后停止，不读 `INDEX.md` 或历史正文。实际读取/写入 Project Knowledge 时才先读 `INDEX.md`，并只加载与当前决策相关的 1–3 条。

临时进度、可搜索代码事实、生成报告和未经验证的猜测不进入 memory。写入后更新索引；替代旧结论时保留历史并标记 `superseded`。
