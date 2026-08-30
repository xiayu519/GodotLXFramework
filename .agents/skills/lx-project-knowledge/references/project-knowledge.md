# Project Knowledge 规则

`.codex/memory/` 保存无法从当前源码、清单、生成报告或官方文档直接重建，但会影响未来决策的稳定知识。

## 读取

1. 先读 `INDEX.md`。
2. 只有当前任务与历史知识有关时，读取最相关的 1-3 条正文。
3. 权威顺序为：当前源码与工具输出、当前生效的 `AGENTS.md`/Skill/reference、已核验官方资料、Project Knowledge。
4. 遇到版本、路径、API 或其他易过期事实，使用当前权威来源复核。

## 分类

- `problems/`：会复现、仍需规避或值得保留诊断路径的问题。
- `decisions/`：无法仅从代码看出原因的架构或工作流取舍。
- `feedback/`：用户反复确认的稳定偏好与验收标准。
- `references/`：外部资料的本项目结论、适用版本和复核日期；不镜像整篇资料。

## 写入

只在知识会改变后续任务决策、且不能由仓库现状可靠推导时写入。正文使用中文，文件名使用日期与短标识，例如 `2026-08-30-sol-high-baseline.md`，并包含：

```yaml
---
title: 简短标题
kind: problem | decision | feedback | reference
status: active | superseded | resolved
verified: 2026-08-27
sources:
  - 相对路径或官方 URL
---
```

写入后更新 `INDEX.md`。临时进度、任务计划、生成清单、可搜索的代码事实、未经验证的猜测都不进入 Project Knowledge。被替代的条目保留历史并标记 `superseded`，同时指向新条目。
