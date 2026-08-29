---
title: LXFramework 0.1 范围与延后项
kind: decision
status: active
verified: 2026-08-29
sources:
  - godot_project/src/LXFramework/Camera/Camera2DController.cs
  - godot_project/src/LXFramework/UI/UIFadeTransition.cs
  - godot_project/src/LXFramework.Core/Actions/ActionRunner.cs
  - godot_project/api/LXFramework.PublicApi.txt
  - .github/workflows/validate.yml
---

# LXFramework 0.1 范围与延后项

## 决定

- 框架定位首先服务 Codex 全 AI 开发，其次服务人工编程；两条入口必须共享同一事实源与 `lx.ps1` 实现。
- 0.1 提供 Godot `LX Tools`、详细人工 README、Windows 导出入口、确定性视觉回归、统一 runtime diagnostics、Luban 二进制表链路和通用 UI 组件示例。
- 框架的枚举、枚举成员和公开常量必须提供详细语义注释，并由静态门禁强制。
- 干净的框架发行仓库是唯一公开发布源；产品工作区和产品专属内容不得上传到该仓库。
- 2D 相机采用“传入具体 `Camera2D`，返回局部控制器”的所有权模型，不建立全局当前相机；3D 将来使用独立扩展。
- 通用 UI 过场当前只固定可配置黑幕 `FadeOut`、`FadeIn`、`FadeOutIn`；`FadeOut` 完成后保持黑幕。
- `LX.Actions` 已作为生命周期化的中低频流程编排入口交付，不再属于延后项；它不替代 GameFlow、StateMachine、Scheduler 或 Tween。
- AI 控制面采用可恢复事务 journal、公开 API 基线、提交 CI、可选 soak 和标签/手动 Release export。严格 EventHub allocation gate 进入普通 `validate`；耗时 soak/export 不进入本地默认门禁。

## 明确延后

- 网络、下载、热更新。
- 音频模块的进一步扩展。
- 手柄/多绑定重绑 UI、字体 fallback 和位置音频。
- `Camera3DController`，以及 Fade/Slide/Scale 等逐页面默认动效策略。
- 未被用户单独选择的输入、音频、Feature、Toast 等便利 API。
- 可视化 runtime debugger；当前只固定统一诊断快照协议。
- 完整示例游戏；后续由用户构造打飞机项目。

这些延后项不是缺陷修复的默认授权范围；“暂缓”不等于永久拒绝，后续只有用户再次选择时才集成。已确认的正确性、生命周期或 Godot 4.7.2 引擎契约缺陷仍可在明确修复任务内处理。
