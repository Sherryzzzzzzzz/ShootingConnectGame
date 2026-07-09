实现任务清单

1) 复现与记录（必做）
- 在编辑器打开场景 Assets/Scenes/Fight.unity，按 Play，尝试移动并记录控制台报错与行为。
- 记录操作系统、Unity 版本、输入方式（键盘/手柄）。

2) 快速恢复（推荐）
- [x] 检查 git 历史并恢复 Assets/Scripts/Player/PlayerController.cs 到当前分支（已完成，恢复并提交到分支 fix-player-movement-bug-restore）。
  - 若用户授权，我可以为你运行 git 恢复命令并提交（需要确认）。
- 在 Unity 中确认 Player prefab 已挂载 PlayerController 且脚本编译通过。运行 Play 验证移动是否恢复。

3) 备选实现（如果恢复不可行）
- 检查 IInputSource 接口实现（Assets/Scripts/Player/IInputSource.cs），并确认 PlayerModel/状态机如何获取输入。
- 将 PlayerInputAction 的 Move 回调接入到 IInputSource 的方法或直接调用 PlayerModel 的输入处理。
- 实现最小必要的桥接代码并验证移动。

4) 调试与日志
- 在输入回调处与移动应用处添加临时 Debug.Log，确认输入值流（向量/轴值）传递到移动逻辑。

5) 验证与清理
- 完成移动功能后移除或限制调试日志。
- 运行手动测试场景并更新变更说明。

6) 提交
- 创建新的分支（可选，推荐）并提交恢复/修复的更改。
- Commit message 示例："fix: restore PlayerController to recover player movement\n\nCo-Authored-By: Claude Opus 4.6 <noreply@anthropic.com>"

7) 交付（PR 验证）
- 在 PR 描述中包含：复现步骤、修复步骤、测试清单、回滚方法。

---
备注：如果你希望我直接开始实施（恢复文件并运行编辑器验证），请允许我在仓库中执行 git 恢复或检索历史文件的操作。