# 产品输入工作流

创建动作使用：

```powershell
.\lx.ps1 create input PauseGame pause_game Escape
```

命令更新 `content/input/input-manifest.json` 并刷新强类型 `InputCatalog`/`LXInputActions`；生成目录只能由工具写入。

名称、稳定 ID 和默认绑定已给出时走固定快路径：读取上游清单与产品消费文件最近的局部 `AGENTS.md`，运行一次 `create input`，将回显的全部变更路径一次交给 `check`，最后运行一次 `validate`。不运行 `inspect`、`--help`、`git status`、目录搜索、生成文件重读或重复差异确认。

只有还要修改玩法响应时才读取目标产品文件并额外使用 `$lx-game`。需要观察当前输入状态时，在活动 Editor/Debug 会话中使用 `$lx-runtime-observe` 查询 `runtime snapshot input --json`；没有活动会话时以确定性 smoke/validate 为证据。
