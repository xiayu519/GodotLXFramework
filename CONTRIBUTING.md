# Contributing

## 开发约定

1. 使用 Godot 4.6 .NET 和 .NET SDK 8.0。
2. 从仓库外层运行 `.\lx.ps1`；不要在生成目录直接修改文件。
3. 新世界、Feature、UI、输入、资源和节点优先使用 `.\lx.ps1 create ...`。
4. 保持 `LXFramework.Core → 无 Godot`、`产品 → LXFramework → LXFramework.Core` 的单向依赖。
5. 公开枚举、每个枚举成员和公开常量写明使用语义与取舍。
6. 迭代时运行一次针对变更路径的 `check`，提交前运行 `.\lx.ps1 validate`。

## UI 基准

先运行 `.\lx.ps1 visual compare ui_components`。只有确认差异符合设计后才运行 `visual approve`，并在变更说明中解释基准为何变化。

## Luban

只修改 `game_design/schema` 与 `game_design/data`。运行 `.\lx.ps1 data` 后提交需要进入仓库的 `.bytes`、manifest 和产品生成代码；不要手改它们。

## 提交

提交应聚焦一个可验证结果，并更新 `CHANGELOG.md`。不要提交 `.godot/`、`.tools/`、`.lx/`、`bin/`、`obj/` 或本地导出产物。
