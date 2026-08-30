# PlaneFight 产品层规则

- 本目录是当前游戏的产品代码与专用工作流，只能依赖 LXFramework 公开 API；框架层禁止反向依赖本目录。
- 通过注入的 `LX` 上下文调用 `LX.UI`、`LX.Res` 等模块；禁止全局上下文、服务定位器和直接动态 `GD.Load`/`ResourceLoader.Load*`。
- 新结构使用 `./lx.ps1 create world|feature|screen|content|input|res`；`Generated/` 禁止手改。
- 产品行为变更后运行相关 `./lx.ps1 check <changed-path> [...]`，交付前运行 `./lx.ps1 validate`。
