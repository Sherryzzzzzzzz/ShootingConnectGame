# 开发规范（工程实践）

本文档是项目的工程实践约束。**每次提交前必须通过 `check-health.sh`**。

## 1. 提交前检查清单

```bash
bash check-health.sh   # 检查 git 卫生 / 版本一致 / 资产引用 / YAML / C# 括号
```

通过后，再手动验证（以下两条缺一不可）：

1. **Unity 能编译** —— 打开编辑器无 Console 红色错误
2. **Play 冒烟** —— 从 StartScene 进 Fight，游戏能跑，无异常刷屏

> 教训：本项目多次出现"工具写完不验证编译"（如 `DepthNormals` 错误挂了数周）、"改完不跑"导致 prefab 引用已删除脚本。**改任何代码/资产，必须编译 + Play 验证后才算完成。**

## 2. 提交规范

- 提交信息格式：`<type>: <中文/英文摘要>`
  - `fix:` 修复 bug（如 `fix: 描边后处理黑屏`）
  - `feat:` 新功能
  - `refactor:` 重构（不改变行为）
  - `chore:` 工具/配置/清理（如 `chore: 清理 bin/obj 出仓库`）
  - `docs:` 文档
- **禁止**无意义提交信息（如 "1"、"update"）
- 一次提交只做一件事；`git add` 精确到文件，不要 `git add .`

## 3. 禁止提交进仓库的内容

已被 `.gitignore` 覆盖，新增时注意不要 `git add -f`：

- `**/bin/`、`**/obj/`（.NET 编译产物）
- `*.dll`、`*.exe`、`*.pdb`（`Assets/Plugins/**` 和 `Packages/**` 的依赖二进制除外）
- `.idea/`、`.vscode/`、`*.slnx`（本地 IDE 配置，Unity 会自动重新生成）
- `Library/`、`Temp/`、`Logs/`、`Build/`、`UserSettings/`

## 4. 版本锁定

- `Packages/manifest.json` 必须与 `packages-lock.json` 一致（`check-health.sh` 会校验 URP 版本）
- 不要手改 manifest 版本号后不重新解析；升级包时让 Unity 重新解析并提交 lock
- 教训：项目曾 manifest 写 URP 14.0.8 实际跑 17.1.0，一旦 clean checkout 整个渲染层崩坏

## 5. 资产修改规则

- **场景/预制体/资产修改优先用编辑器脚本或 Inspector**，不要直接文本编辑 `.unity`/`.asset`
  - Unity 反序列化时会用内存状态覆盖文件，手工 YAML 修改会被冲掉（教训：EVM 相机被还原）
  - 需要批处理时，写 Editor 工具（参考 `Assets/Scripts/Editor/` 现有工具）
- **删除脚本类时，必须同步清理所有引用它的 prefab/场景**（missing script 会让对象静默失效）
- **重构必须清残留**：新系统替换旧系统后，删除旧代码、旧 prefab 引用、旧 feature

## 6. 渲染管线约定

- 渲染管线：URP 17.1.0 + **Render Graph 开启**（`m_EnableRenderGraph: 1`，禁止改回经典模式）
- 全屏后处理用 `FullScreenPassRendererFeature`，注意：
  - `fetchColorBuffer` 必须为 `true`（shader 采样 `_BlitTexture`）
  - 必须写 `m_Version: 1`，否则 URP 17 迁移逻辑会重置 `fetchColorBuffer`
- 后处理描边：`OutlinePost_PostFX`；像素化：`PixelatePost_PostFX`；卡通 Profile：`Resources/DefaultVolumeProfile`
- 场景只保留**一套**相机 rig（Cinemachine），运行时由 `BattleManager.EnsureSingleCameraAndListener` 兜底清理

## 7. 数据质量门槛

- `collision.bin` 体积异常（>5MB）说明碰撞导出失控（当前 10MB / 44 万盒子，出生点全部判定不合法）
- 导出碰撞后必须验证：出生点合法、玩家能移动、Hitscan 命中正确

## 8. 新增文件规范

- 新脚本必须有 `.meta`（由 Unity 生成，勿手写 GUID）
- 编辑工具放 `Assets/Scripts/Editor/`，运行时脚本按职责放 `Assets/Scripts/` 对应子目录
- 共享代码（客户端/服务端双端）放 `Assets/Scripts/Network/Shared/` 并同步到 `Server/ShootingGame.Shared/`
