# ShootingConnectGame

一款基于 Unity 客户端 + .NET 8 自定义服务端的多人联网第三人称射击游戏 (TPS)。

## 项目概述

本项目实现了一套完整的 CS 架构多人射击游戏框架，包含大厅匹配、战斗同步、客户端预测、服务端回溯延迟补偿等核心联网功能。

- **客户端**: Unity 6 (6000.1.9f1), URP 渲染管线
- **服务端**: .NET 8 独立可执行程序
- **同步频率**: 60Hz Tick Rate
- **网络协议**: TCP (大厅) + UDP/KCP (战斗)

## 核心特性

### 网络架构

| 层 | 协议 | 端口 | 用途 |
|---|---|---|---|
| 大厅 (Lobby) | TCP | 7778 | 登录认证、匹配队列、房间管理 |
| 战斗 (Battle) | UDP + KCP | 7777 | 实时状态同步、输入传输、命中判定 |

- **双层传输**: UDP 发送非可靠快照数据 (高频状态同步)，KCP 在 UDP 之上提供可靠消息传输 (伤害事件、连接请求等)
- **客户端预测**: 客户端立即执行输入并在本地预测结果，收到服务端权威状态后进行和解 (Reconciliation)
- **延迟补偿**: 服务端保存世界状态历史快照，收到开火请求时回滚到对应时刻进行命中判定
- **动态追帧**: 根据网络延迟自适应调整客户端的 Tick 追赶策略

### ECS 框架

自定义轻量级 Entity-Component-System 架构，客户端和服务端共享代码：

- 最大 256 个实体、64 种组件类型
- 密集数组存储，基于位掩码的实体查询
- 系统组 (System Group) 管线化执行
- 组件类型 ID 通过静态泛型分配，零运行时开销

### 技能系统

基于 GameplayTag 的通用技能框架：

| 技能 | 说明 |
|---|---|
| FireWeapon | 开火，消耗弹药，生成子弹实体 |
| Jump | 跳跃 |
| ReloadWeapon | 换弹 |
| Dash | 冲刺位移 |
| Shield | 护盾防护 |
| Stealth | 潜行隐身 |
| Charge | 蓄力攻击 |
| MarkShot | 标记射击 |

技能通过 `AbilityConfig` 配置数据驱动，支持冷却、标签条件检查、生命周期回调。

### 角色系统

- 玩家状态机 (Ground/Sky/Aim 三态切换)
- 身体部位命中盒 (BodyPartHitbox)
- 动画状态机驱动 (Idle/Move/Turn/Jump/Fall/Aim)
- 英雄注册表 (HeroRegistry) 支持多英雄

### 物理与碰撞

- 纯 C# AABB 碰撞检测 (引擎无关，客户端/服务端共享)
- 射线-AABB 交叉检测
- 场景碰撞数据导出为 `collision.bin`
- KinematicMover 运动求解器

## 目录结构

```
ShootingConnectGame/
├── Assets/
│   ├── Scripts/
│   │   ├── Core/                  # 场景启动引导
│   │   ├── Network/               # 客户端网络层 (LobbyClient, BattleClient, BattleManager)
│   │   │   └── Shared/            # 两端共享代码
│   │   │       ├── Math/          # Vec2, Vec3, Quat (引擎无关)
│   │   │       ├── Physics/       # AABB, CollisionWorld, KinematicMover
│   │   │       ├── ECS/           # 自定义 Entity-Component 框架
│   │   │       ├── Simulation/    # 游戏常量, 玩家模拟, Hitscan
│   │   │       ├── Protocol/      # UDP传输, KCP可靠通道, 消息序列化
│   │   │       ├── Ability/       # 技能系统
│   │   │       ├── Hero/          # 英雄配置
│   │   │       ├── GameplayTags/  # 64位标签系统
│   │   │       └── StateMachine/  # 玩家状态机
│   │   ├── Player/                # 玩家控制器, 动画状态, 输入
│   │   ├── ECS/                   # 客户端ECS系统
│   │   ├── UI/                    # UI 管理
│   │   ├── Editor/                # 编辑器工具
│   │   ├── GameplayTags/          # GameplayTag配置资源
│   │   └── Debug/                 # 网络诊断工具
│   ├── Scenes/                    # 场景文件
│   ├── StreamingAssets/           # collision.bin 等流式资源
│   └── Settings/                  # URP 管线配置
├── Server/
│   ├── ShootingGame.Server/       # .NET 8 服务端程序
│   ├── ShootingGame.Shared/       # .NET Standard 2.1 共享库
│   └── ShootingGame.Tests/       # 单元测试 (xUnit)
├── Packages/
│   └── com.shootinggame.network/  # 自定义 Unity Package
├── SourceGenerators/              # 代码生成器                         
```

## 环境要求

### 客户端
- Unity 6000.1.9f1 或更高版本
- Windows 10/11 (开发环境)
- Universal Render Pipeline (URP)

### 服务端
- .NET 8 SDK
- Windows / Linux / macOS 均可运行

## 快速开始

### 1. 启动服务端

```bash
cd Server
dotnet run --project ShootingGame.Server --lobby-port 7778 --battle-port 7777
```

可选: 指定碰撞数据文件

```bash
dotnet run --project ShootingGame.Server --collision ./ShootingGame.Server/collision.bin
```

### 2. 启动客户端

1. 使用 Unity Hub 打开项目 (Unity 6)
2. 打开 `Assets/Scenes/StartScene.unity` 场景
3. 点击 Play 运行
4. 在大厅界面登录并开始匹配

> SceneBootstrapper 会自动创建所有必要的网络组件。默认连接 `127.0.0.1`，可在 Inspector 中修改服务器地址。

### 3. 运行测试

```bash
cd Server
dotnet test ShootingGame.Tests
```

## 架构说明

### 数据流

```
玩家输入 → InputBuffer → BattleClient (序列化) → UDP → 服务端
                                                          ↓
                                           BattleRoom (60Hz Tick)
                                                          ↓
客户端 ← (反序列化) BattleClient ← UDP/KCP ← WorldSnapshot + DamageEvent
    ↓
NetPlayerController (预测+和解) / RemotePlayerController (插值)
    ↓
角色动画 + 相机渲染
```

### 双层端口设计

- **大厅端口 (7778, TCP)**: 处理低频可靠消息 -- 登录、匹配、房间列表、战斗准备
- **战斗端口 (7777, UDP)**: 处理高频实时数据 -- 玩家输入、世界快照、伤害事件

进入战斗后，大厅连接保持活跃用于心跳和状态管理。

## 技术栈

| 技术 | 用途 |
|---|---|
| Unity 6 (URP) | 客户端渲染 |
| .NET 8 | 服务端程序 |
| Protobuf | 消息序列化 |
| KCP | 可靠UDP传输 |
| Unity New Input System | 玩家输入 |
| Cinemachine 3 | 摄像机控制 |
| TextMeshPro | UI 文本渲染 |
| xUnit | 服务端单元测试 |

## License

待定
