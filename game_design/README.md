# game_design

这里保存与 `godot_project/` 同级的策划数据事实源。集成固定使用 Luban `v4.10.2`，以 XML 描述 schema、以 JSON 保存可审查的数据源，并生成 C# 强类型代码与二进制 `.bytes` 运行时表。

Windows 可直接双击 `build.bat`。脚本从自身目录定位工作区，首次运行自动安装固定提交的 Luban，然后把代码和数据安装到工程对应目录；成功或失败都会保留窗口供查看。命令行与 CI 使用：

```powershell
.\game_design\build.bat --no-pause
.\lx.ps1 data
.\lx.ps1 validate
```

生成位置：

- 有产品层时：必须在 `content/game/game-manifest.json` 显式声明 `sourceRoot`；代码写入 `<sourceRoot>/Generated/Luban/`，二进制表写入 `godot_project/content/data/luban/*.bytes`。工具不扫描或猜测 `script/` 下的文件夹。
- 干净框架基线没有产品层时：代码与数据只写入 `godot_project/.lx/luban/generated/`，用于验证工具链，不污染框架运行时内容。

当前示例包含 `design_probe`、`design_category` 和 `design_item`。它覆盖 int/float/double/long/bool/string、enum、嵌套 bean、list、set、map、nullable 和跨表引用；产品启动时通过 `LX.Content.LoadLubanTables` 实际读取三张 `.bytes`，不是只验证文件存在。`fixtures/invalid/missing_reference.json` 必须被 Luban 拒绝，用于证明跨表引用约束没有退化。产品可通过可选的 `validation.json` 声明自己的非法数据 fixture、目标文件和期望错误词，框架脚本不写死产品表名或字段名。

每次构建会执行两轮生成并比较输出哈希，然后在隔离 `.csproj` 编译生成代码。`.lx/luban/report.json` 的 `generatedCodeCompiled`、`negativeReferenceRejected`、`negativeProductDataRejected` 和 `outputHash` 是验证器读取的机器事实，任一缺失都会使 `validate` 失败。

首次运行会把固定提交的 Luban 源码克隆并构建到外层 `.tools/luban/v4.10.2/`。可用 `LX_LUBAN_DLL` 指向已验证的 `Luban.dll`，供离线或 CI 环境复用。
