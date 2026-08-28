# 场景目录规则

- `scene/main.tscn` 及既有 UID 是固定入口，禁止另建平行启动场景。
- 需要代码绑定的 UI 节点设置 `unique_name_in_owner = true`，场景脚本使用仓库内稳定 `res://` 路径。
- 自动验证使用 headless 与 Dummy 音频；只有用户明确要求时才打开可见 Godot 窗口。
