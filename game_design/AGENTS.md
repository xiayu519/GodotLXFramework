# 策划数据指令

- `schema/`、`data/`、`luban.conf` 与 `toolchain.json` 是 Luban 事实源；保持文本格式、UTF-8 和确定顺序。
- 使用根目录 `./lx.ps1 data` 生成，不手改 `godot_project/content/data/luban` 或产品根目录下的 `Generated/Luban`。
- 运行时输出固定为 Luban `cs-bin` + `.bytes`；生成代码必须继续通过 `LX.Content.LoadLubanTables` 读取，不创建全局配置单例或第二套内容服务。
- 修改本目录后运行 `./lx.ps1 check game_design/<changed-path>`；交付前运行 `./lx.ps1 validate`。
