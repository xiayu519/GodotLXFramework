# 内容清单规则

- 本目录中的 manifest 是注册与代码生成事实源；ID 必须稳定、唯一，路径使用 `res://`。
- 结构单元优先使用 `./lx.ps1 create ...` 创建，手动修改后把 manifest 路径交给 `./lx.ps1 check`。
- 禁止直接修改清单对应的 `src/**/Generated` 输出。
