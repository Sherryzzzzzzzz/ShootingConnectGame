# ShootingConnectGame

Unity 6 + .NET 8 多人联网第三人称射击游戏（TPS），自定义 ECS 架构 + 客户端预测 + 服务端延迟补偿。

## 技术栈

| 层 | 技术 |
|---|---|
| 客户端渲染 | Unity 6 (6000.1.9f1), URP, Cinemachine 3, Toon Shader |
| 服务端 | .NET 8, KCP/UDP/TCP 双层传输 |
| 序列化 | Protobuf (proto3) + 手写 PacketReader/Writer |
| 同步模型 | 60Hz Tick, 服务端权威 + 客户端预测 + Reconciliation |
| 网络同步 | ServerRpc/ClientRpc/SyncVar/SyncComponent 声明式特性 + NetVar |
| 架构 | 自定义 ECS (256 实体 / 64 组件), 共享代码双端复用 |
| 配置管线 | Unity 编辑器导出 JSON/collision.bin -> 服务端加载同一份数值 |

## 核心特性

- **三层游戏流程** — StartScene（登录/大厅）-> HeroSelectScene（选英雄）-> Fight（战斗）
- **双层传输** — 大厅 TCP (7778) 做登录/匹配/房间；战斗 UDP+KCP (7777) 做实时状态同步与可靠事件投递
- **客户端预测** — `PredictionService` + `PredictionContext`，输入即时响应，服务端权威快照回传后 Reconciliation 纠正
- **延迟补偿** — 服务端 `WorldHistory` 环形缓冲保存 60Hz 世界快照，开火时回滚到对应 Tick 做 Hitscan 判定
- **动态追帧** — 根据网络延迟自适应调整客户端 Tick 追赶策略
- **网络同步框架** — `[ServerRpc]` / `[ClientRpc]` / `[SyncVar]` / `[SyncComponent]` 声明式同步，`RpcTransactionService` 事务化 RPC，`NetSimulator` 可注入丢包/延迟/抖动做健壮性测试
- **技能系统** — GameplayTag 驱动的通用技能框架（Fire / Jump / Reload / Dash / Shield / Stealth / Charge / MarkShot / Sprint），带冷却与生命周期管理
- **共享物理** — 纯 C# AABB/Capsule 碰撞 + KinematicMover + HitscanResolver，引擎无关，客户端/服务端共用同一套代码
- **状态机** — 玩家 Ground / Sky / Aim 三态切换 + 动画状态机驱动
- **玩法模式** — 团队歼灭（TDM）/ 死斗（FFA），击杀目标数、时间限制、人数均可配置
- **配置导出管线** — 编辑器工具导出 heroes/guns/abilities JSON、collision.bin、SpawnPoints.json，服务端 `GameConfigLoader` 加载，保证数值唯一来源

## 项目结构

```
├── Assets/
│   ├── Scenes/
│   │   ├── StartScene.unity          # 登录 / 大厅
│   │   ├── HeroSelectScene.unity     # 英雄选择
│   │   └── Fight.unity               # 战斗
│   ├── StreamingAssets/collision.bin # 客户端导出的场景碰撞数据
│   └── Scripts/
│       ├── Core/                     # SceneBootstrapper, ManagerHub, ProcedureManager, UIManager, ObjectPool...
│       ├── Network/
│       │   ├── Core/                 # NetVar 网络变量
│       │   ├── Server/               # 编辑器内 Host 模式服务端 (ServerTickLoop, HostBattleServer...)
│       │   └── Shared/               # 双端共享代码（同步至 Server/ShootingGame.Shared）
│       │       ├── ECS/              # 自定义 Entity-Component (EntityManager, ECSBridge)
│       │       ├── Ability/          # GameplayTag 技能系统
│       │       ├── Attributes/       # ServerRpc/ClientRpc/SyncVar/SyncComponent 特性
│       │       ├── Physics/          # AABB/Capsule 碰撞, CollisionWorld, KinematicMover, HitscanResolver
│       │       ├── Protocol/         # UDP/KCP/TCP 传输, PacketReader/Writer, Protobuf, NetSimulator
│       │       ├── Simulation/       # 游戏常量, InputFrame, PlayerSimulation, WorldSnapshot
│       │       ├── Prediction/       # PredictionService, PredictionContext
│       │       ├── Math/             # Vec2/Vec3/Quat（引擎无关）
│       │       ├── StateMachine/     # 玩家状态机 (Ground/Sky/Aim)
│       │       ├── Hero/             # 英雄 / Gun 配置
│       │       ├── GameplayTags/     # 64 位标签系统
│       │       └── Network/          # RpcMethodRegistry, NetIdRegistry
│       ├── ECS/                      # 客户端 ECS 系统（输入/预测/对账/插值/开火/子弹/命中/动画/视觉同步）
│       ├── Player/                   # 玩家控制器, 动画状态, BodyPartHitbox
│       ├── UI/                       # Login/Lobby/HeroSelect/Battle UI
│       ├── Editor/                   # 编辑器工具（配置导出/碰撞导出/出生点/服务器启动器/标签代码生成...）
│       ├── GameplayTags/             # 标签配置资产
│       ├── Generated/                # 代码生成产物
│       ├── ScriptsObject/            # ScriptableObject 定义（PlayerAnimationSet）
│       ├── Utils/                    # 对象池 / 音频池 / 特效池
│       └── Base/                     # StateMachine / 单例基类
├── Server/
│   ├── ShootingGame.Server/          # .NET 8 服务端（LobbyServer, MatchMaker, RoomManager, BattleRoom, WorldHistory, ServerECSWorld）
│   ├── ShootingGame.Shared/          # .NET Standard 2.1 共享库（与客户端 Shared 同步）
│   ├── ShootingGame.Tests/           # xUnit 单元测试
│   └── *.json                        # 编辑器导出的英雄/枪械/技能/出生点配置
├── SourceGenerators/                 # 代码生成器（GameplayTag 等）
└── tools/                            # protoc 等脚本工具
```

## 快速开始

### 服务端

```bash
cd Server
dotnet run --project ShootingGame.Server
```

常用参数（均有默认值）：

| 参数 | 默认 | 说明 |
|---|---|---|
| `--lobby-port` | `7778` | 大厅 TCP 端口 |
| `--battle-port` | `7777` | 战斗 UDP+KCP 端口 |
| `--players` | `2` | 每局人数 |
| `--mode` | `0` | `0`=团队歼灭，`1`=死斗(FFA) |
| `--kill-target` | `10` | 击杀目标数 |
| `--time-limit` | `300` | 时间限制（秒） |
| `--collision` | 自动探测 | collision.bin 路径 |
| `--spawn-config` | 默认 | 出生点配置路径 |
| `--config-dir` | `.` | heroes/guns/abilities JSON 所在目录 |

```bash
# 单元测试
cd Server && dotnet test ShootingGame.Tests
```

### 客户端

1. Unity Hub 打开项目（Unity 6000.1.9f1+）
2. 打开 `Assets/Scenes/StartScene.unity`，点击 Play
3. 登录 -> 匹配/建房 -> 选英雄 -> 进入战斗

> 默认连接 `127.0.0.1`，可在 SceneBootstrapper Inspector 中修改。
> 开发时可用编辑器菜单 **游戏 -> 编译并启动**（ServerLauncher）直接在编辑器内起本地服务端。

### 配置导出（修改数值后执行）

Unity 编辑器菜单 **游戏 -> 配置 / 碰撞** 导出，服务端重启后读取：

- 英雄/枪械/技能 -> `Server/*.json`
- 场景碰撞体 -> `Assets/StreamingAssets/collision.bin` + `Server/collision.bin`
- 出生点 -> `Server/SpawnPoints.json`

## 环境要求

- Unity 6000.1.9f1+, URP
- .NET 8 SDK
- Windows 10/11（开发）
- Python 3（可选，`check-health.sh` 需要）

## 开发规范

提交前运行 `bash check-health.sh` 做健康检查（git 卫生 / 版本一致 / 资产引用 / YAML / C# 括号），详细约定见 [CONTRIBUTING.md](CONTRIBUTING.md)。
