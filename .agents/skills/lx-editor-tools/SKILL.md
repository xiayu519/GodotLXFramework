---
name: lx-editor-tools
description: 维护 Godot 编辑器 LX Tools 面板及 CLI 等价入口；不处理游戏场景。
---

# LX Godot 编辑器工具

完整读取 `references/tooling-workflow.md`。编辑器只呈现人工开发者需要的入口，复用现有清单和 CLI，不建立第二套生成或验证逻辑。

修改插件后验证后台进程、程序集重载后的结果恢复和中文诊断；运行明确路径的 `check` 与最终 `validate`。
