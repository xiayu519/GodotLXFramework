---
title: 同构目录的同步取舍
kind: decision
status: active
scope: LXFramework 工作区布局与下游同步
verified: 2026-09-09
sources:
  - AGENTS.md
---

选择外层工作区与内层 Godot 工程的原因，是把 Git、工作流和上游生成工具隔离出资源导入树，同时让干净框架与下游工作区保持同构相对路径，降低同步时的路径改写成本。

这解释既有布局的动机，不冻结未来目录调整；当前路径、生成位置与入口以 AGENTS 和实际工程为准。
