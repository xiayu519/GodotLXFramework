# Godot 工程局部规则

- 本目录是唯一 Godot 资源根；所有 `res://` 路径、清单路径和 Godot 导入均相对此目录解析。
- 从工作区根运行 `./lx.ps1`，或在本目录运行 `./lx.ps1`；两者必须得到相同框架结果。
- `src/LXFramework.Core`、`src/LXFramework` 与 `script/<GameName>` 依次为纯核心、Godot 适配层和可选产品层；依赖只能从产品层指向框架。
- `content/` manifest 是注册和生成事实源；禁止手改 `src/LXFramework/Generated` 或产品根目录下的 `Generated/`。
- `scene/main.tscn` 是固定入口。结构修改后把相对此目录的变更路径交给 `lx.ps1 check`；全量门禁时机遵循根 `AGENTS.md`。
