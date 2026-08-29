# Luban 数据工作流

只读询问新增表的目录、命令、输出和格式时，本文件就是完整契约；回答后停止，不再读取 schema、配置、生成目录、构建脚本或 `ContentService` 实现。

- 上游事实源：在 `game_design/schema/` 修改 XML schema，在 `game_design/data/` 修改 UTF-8 JSON 数据；工具版本由 `game_design/toolchain.json` 固定。
- 统一命令：从仓库外层运行 `./lx.ps1 data`；人工也可双击 `game_design/build.bat`。
- 产品输出：Luban `cs-bin` C# 写入产品根 `Generated/Luban/`，`.bytes` 写入 `godot_project/content/data/luban/`。干净框架尚无产品层时，确定性中间结果位于 `godot_project/.lx/luban/generated/`。
- 运行时：通过 `LX.Content.LoadLubanTables(loader => new GameData.Tables(loader))` 读取；内容服务使用 Godot `FileAccess` 取得 `.bytes` 并交给 Luban `ByteBuf`。
- 所有权边界：不手改生成输出，不建立静态 `Tables` 单例或第二套内容服务。普通小型 JSON 内容仍使用 `lx create content`。

`data` 会做双次确定性生成、生成 C# 编译和负向引用验证，报告位于 `.lx/luban/report.json`。
