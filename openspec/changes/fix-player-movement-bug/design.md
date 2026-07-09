设计（如何修复）

目标：在最小影响下恢复玩家移动功能，优先策略为恢复被误删的控制器实现；若不可行则将输入桥接到现有的 PlayerModel/状态机。

1) 初步排查清单（必做）
- 在 Unity 编辑器中复现：启动场景（Assets/Scenes/Fight.unity），按下 Play，观察控制台错误与输入响应。
- 检查 Player prefab（Assets/Prefabs/Player.prefab）是否存在、是否有运动相关组件（CharacterController / Rigidbody / Collider）。
- 检查输入系统资源：Assets/InputSystem/PlayerInputAction.inputactions 和生成代码 Assets/InputSystem/PlayerInputAction.cs，确认动作绑定（Move、Look、Jump、Fire 等）。
- 检查代码变更：注意到 PlayerController.cs 在当前工作树被删除（请查看 git 历史恢复或对比最后一个正常提交）。

2) 可能的根因假设
- PlayerController 脚本被删除或未挂载 -> 移动逻辑缺失（高概率，git 状态显示已删除）。
- 输入事件没有正确绑定到新的输入抽象 IInputSource -> 动作触发无效。
- 物理/碰撞或状态机阻止移动（如处于不可移动状态、重力/卡住）。

3) 优先实现方案（推荐顺序）
A. 恢复已删除的 PlayerController.cs（最快、风险最低）
   - 从 git 历史恢复该文件到当前分支（会产生一个新提交），运行编辑器验证。若文件在仓库历史中存在，即可直接恢复。
B. 若无法恢复或代码不再兼容，则桥接输入到现有抽象
   - 利用已存在的 IInputSource 接口（Assets/Scripts/Player/IInputSource.cs）把 PlayerInputAction 的回调转发到 PlayerModel 或 PlayerState。
   - 确保状态机（PlayerState、PlayerGroundState/PlayerSkyState、AnimationState 等）能读取输入并应用移动。
C. 增加调试与日志
   - 在输入接收与移动应用处添加临时日志（仅调试用），方便验证信号流。

4) 验证与测试
- 编辑器：Play 模式下手动测试移动、跳跃、转向。记录测试步骤与结果。
- 回归：确认相关场景与玩家控制不受影响。

5) 提交与说明
- 创建一条清晰的 commit（说明恢复或修改内容），在 PR 描述中列出复现步骤与验证清单。

重要文件参考
- Assets/Scripts/Player/PlayerController.cs (已删除 — 恢复优先)
- Assets/Scripts/Player/IInputSource.cs (新增/修改)
- Assets/Scripts/Player/PlayerModel.cs
- Assets/InputSystem/PlayerInputAction.inputactions 和生成代码
- Assets/Prefabs/Player.prefab

