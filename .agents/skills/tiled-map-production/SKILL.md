---
name: tiled-map-production
description: 制作、扩展或修正 Tiled TMX/TSX 正交与等距地图，侧重素材复用、布局和遮挡验收；不用于单张概念图或无关玩法开发。
---

# Tiled Map Production

交付可编辑、资源可复用的地图，不以整图伪装成 tileset。

## 选路

- 分析请求只读；实施才修改。读取现有地图、人工改动和素材来源；明确投影、格尺寸、角色尺度与交付范围，可逆细节沿用已有约定。
- 新制/扩展素材读 [production.md](references/production.md)。生成或重绘使用可用的 imagegen Skill/工具；技术处理遵守当前授权与图像工具规则，历史许可不自动继承。
- 新图或调整排列、道路、农田、商铺读 [scene-review.md](references/scene-review.md)。新图先验收小范围布局与素材样例，不让局部修复重启全流程。
- 建筑/桥/人物遮挡或引擎验收读 [occlusion.md](references/occlusion.md)。内容登记、玩法与正式导入器实现另用对应领域 Skill，不自动扩大任务。
- 实施后的检查/交付读 [verification.md](references/verification.md)，按风险选最小充分验证集。
- 仅延续松溪村或其偏好时读 [songxi-profile.md](references/songxi-profile.md)，不默认套用到其他风格。

## 核心约束

- 地面与可独立摆放的点缀分离；重复物体共享原型。必要纹理、接边与接触阴影不必全拆。预算不固定为 16 格，分开报告原型、实例、文件及字节数。
- 场景用途/通行优先于机械避挡；入口和主要交互点保持可用，次要摊位可合理局部遮挡。
- 修共享源并查复用实例。保真拆件核对重组、原点及透明边缘；连通分量只是残片候选，不能自动删除。
- alpha 控制像素可见性，接地锚点控制深度，碰撞控制通行；不能共用图片矩形替代三者。
- 保留原生编辑能力。自定义属性明确导入器消费者；标签、水面动画按需求，不自动增加玩法标记。
- 清理前查整包及共享引用；保留回退，不覆盖人工编辑。不从本 Skill 推断提交/推送授权。
- 参考用于提炼视觉语言，不挪用未获许可的图像、标识或独特布局；记录来源，不保证绝对零版权风险。

## 工具与完成

Python 3.10+；alpha 工具另需 Pillow，不自动安装依赖。脚本只读，JSON 输出到 stdout：

```powershell
python <skill>/scripts/audit_tmx.py <map.tmx> --asset-root <delivery-assets>
python <skill>/scripts/audit_alpha.py <changed-prop.png>
```

`--asset-root` 可省略。退出码 0 为范围内通过，1 为输入/检查失败，2 为工具不可用或参数错误。警告不授权删除；脚本不证明布局、美术权利或运行时遮挡。

交付成品路径、相关近景/报告和未验证范围。人工验收时打开准确版本，不关闭未保存地图。Tiled 可打开不等于游戏验收通过；隔离 QA 不冒充正式导入器，多角色、新风格与新投影分别验证。
