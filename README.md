# ShootingConnectGame

Unity 6 + .NET 8 多人联网第三人称射击游戏（TPS），自定义 ECS 架构 + 客户端预测 + 服务端延迟补偿。

## 技术栈

| 层 | 技术 |
|---|---|
| 客户端渲染 | Unity 6, URP, Cinemachine 3 |
| 服务端 | .NET 8, KCP/UDP/TCP |
| 序列化 | Protobuf |
| 同步模型 | 60Hz Tick, 客户端预测 + 和解 (Reconciliation) |
| 架构 | 自定义 ECS (Entity-Component-System), 共享代码双端复用 |

## 核心特性

- **双层传输** — 大厅 TCP (7778) 做登录/匹配，战斗 UDP+KCP (7777) 做实时状态同步与事件可靠投递
- **客户端预测** — 输入即时响应，服务端权威状态回传后 Reconciliation
- **延迟补偿** — 服务端保存世界状态历史快照，开火时回滚到对应时刻做 Hitscan 判定
- **动态追帧** — 根据网络延迟自适应调整客户端 Tick 追赶策略
- **技能系统** — GameplayTag 驱动的通用技能框架（Fire/Jump/Reload/Dash/Shield/Stealth/Charge/MarkShot）
- **共享物理** — 纯 C# AABB 碰撞检测，引擎无关，客户端/服务端共用同一套代码
- **状态机** — 玩家 Ground/Sky/Aim 三态切换，动画状态机驱动

## 项目结构

```
├── Assets/Scripts/
│   ├── Network/                  # 客户端网络层 (BattleClient, BattleManager, LobbyClient)
│   │   ├── Server/               # 编辑器内 Host 模式服务端
│   │   └── Shared/               # 双端共享代码（同步到 Server）
│   │       ├── ECS/              # 自定义 Entity-Component 框架 (256 实体 / 64 组件)
│   │       ├── Ability/          # GameplayTag 技能系统
│   │       ├── Physics/          # AABB 碰撞, CollisionWorld, KinematicMover
│   │       ├── Protocol/         # UDP 传输, KCP 可靠通道, Protobuf 消息
│   │       ├── Simulation/       # 游戏常量, Hitscan, 玩家模拟
│   │       ├── Math/             # Vec2/Vec3/Quat (引擎无关)
│   │       ├── StateMachine/     # 玩家状态机
│   │       ├── Hero/             # 英雄/Gun 配置
│   │       └── GameplayTags/     # 64 位标签系统
│   ├── Player/                   # 玩家控制器, 动画, 输入
│   ├── ECS/                      # 客户端 ECS 系统
│   ├── UI/                       # UI 管理
│   └── Debug/                    # 网络诊断工具
├── Server/
│   ├── ShootingGame.Server/      # .NET 8 服务端
│   ├── ShootingGame.Shared/      # .NET Standard 2.1 共享库
│   └── ShootingGame.Tests/       # xUnit 单元测试
└── SourceGenerators/             # 代码生成器
```

## 快速开始

### 服务端

```bash
cd Server
dotnet run --project ShootingGame.Server --lobby-port 7778 --battle-port 7777
```

```bash
# 测试
dotnet test ShootingGame.Tests
```

### 客户端

1. Unity Hub 打开项目 (Unity 6000.1.9f1+)
2. 打开 `Assets/Scenes/StartScene.unity`
3. 点击 Play，在大厅登录并开始匹配

> 默认连接 `127.0.0.1`，可在 SceneBootstrapper Inspector 中修改。

## 环境要求

- Unity 6000.1.9f1+, URP
- .NET 8 SDK
- Windows 10/11（开发）
