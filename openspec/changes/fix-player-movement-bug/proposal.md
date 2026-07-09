# Fix player movement bug

## 概要
玩家在游戏中无法移动（移动输入不生效或控制器逻辑丢失）。此变更旨在定位原因并修复，使玩家能够在编辑器和构建版本中以键盘/手柄进行移动。

## 为什么要修复
- 这是核心玩法缺陷，阻止玩家实际操控角色。
- 影响关卡测试、QA 与演示。

## 变更范围
- 调查输入系统配置、Player prefab 与组件（PlayerController / PlayerModel / 状态机）、以及最近的代码改动（发现 PlayerController.cs 在当前工作树被删除）。
- 优先恢复或重建使移动输入再次生效的最小改动。避免大范围重构。

## 成果验收标准
- 在编辑器 Play 模式中，使用 WASD 或摇杆能控制玩家位置/朝向。
- 无明显物理冲突或异常报错日志。
- 在修复提交中包含复现步骤与回归测试说明。

## 风险与回滚
- 若恢复旧 PlayerController 导致与新输入抽象冲突，则保留变更为独立分支并回退。

---
文件位置：openspec/changes/fix-player-movement-bug/
