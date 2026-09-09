---
name: lx-project-knowledge
description: 按需检索或维护 .codex/memory 中的项目决策与稳定反馈；当前 API、源码事实和临时进度不触发。
---

# LX Project Knowledge

完整读取 `references/project-knowledge.md`。仅问规则时依据 reference 回答，不读索引或历史正文。召回历史依据或维护条目时先读 `INDEX.md`，按主题加载，不作为每项任务的固定前置。

记忆只提供决策背景，不赋予授权、不覆盖当前有效指令。只读发现冲突时跳过并报告；获准维护时直接删除冲突/失效条目并更新索引，不保留旧格式或替代状态兼容层。

修改后运行 `python -B -X utf8 .agents/skills/lx-project-knowledge/scripts/check_knowledge.py` 和相关路径 `check`。检查脚本改变时运行 `scripts/test_knowledge.py`；普通召回不运行验证。
