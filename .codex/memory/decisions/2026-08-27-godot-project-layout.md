---
title: 外层工作区与 godot_project 工程边界
kind: decision
status: active
verified: 2026-08-27
sources:
  - https://docs.godotengine.org/en/stable/tutorials/best_practices/project_organization.html
  - https://learn.chatgpt.com/docs/agent-configuration/agents-md
  - AGENTS.md
---

仓库采用外层工作区与 `godot_project/` 内层 Godot 工程的固定布局。外层保存 Git、Codex 工作流、Project Knowledge、公开文档和未来 Luban 等外部工具；`project.godot`、`res://`、框架源码、产品源码及内容清单只存在于 `godot_project/`。

内层名称使用 Godot 官方建议的 `snake_case`。根 `lx.ps1` 是稳定包装入口，保证从外层工作；内层保留同名入口，保证直接在 Godot 工程目录工作。Peachwind 与干净 LXFramework 基线维持相同相对结构，以便框架和工作流按对应路径同步。
