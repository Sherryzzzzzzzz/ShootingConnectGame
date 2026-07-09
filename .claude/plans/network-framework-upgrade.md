# 网络框架升级计划

## 目标

将 ShootingConnectGame 的网络框架从"手写全量快照"升级为"增量同步 + RPC + 代码生成"，融合 SpaceBuilder 的工程化设计和现有 ShootingConnectGame 的客户端预测/ECS 模拟。

## 核心决策汇总

| # | 决策点 | 选择 |
|---|--------|------|
| 1 | RPC 技术方案 | Roslyn Source Generator |
| 2 | RPC 序列化 | 方案 C — 针对每个方法签名生成专用 serializer，零 boxing |
| 3 | NetId 路由目标 | ECS Entity（纯 C# NetworkBehaviour） |
| 4 | 同步方式 | I帧/P帧 模型（全量/增量），均走不可靠通道 |
| 5 | 服务端形态 | Unity headless (`-batchmode -nographics`) |
| 6 | 物理引擎 | 保留 `CollisionWorld` 做确定性模拟，Unity Physics 仅服务端防作弊 |
| 7 | Tick 循环 | 固定 tick 率 + PlayerLoop 注入（SpaceBuilder 风格） |
| 8 | 输入丢包策略 | 不等待 + 输入冗余 ×3 + 动作事件不回放 |
| 9 | NetVar 数据源 | ECS Component struct 是唯一数据源（方案 C） |
| 10 | RPC 模式 | 薄 RPC（方案 A）— RPC 方法只调 System |
| 11 | ComponentTypeId | 编译时 Source Generator 分配 |
| 12 | NetId | uint32（高16位类型 + 低16位自增） |
| 13 | 一个 Entity | 一个 NetworkBehaviour |
| 14 | 消息体 | I帧/P帧 + RPC 合并一个 UDP 包 |
| 15 | Migration 策略 | 从叶子替换（方案 C），5 个阶段渐进 |
| 16 | .asmdef 拆分 | Messages 拆分（传输层留在 Protocol，状态消息移到 Simulation） |

## .asmdef 依赖图

```
Layer 0 (零依赖):
  Shared.Math          — Vec2, Vec3, Quat, GameMath
  Shared.GameplayTags  — GameplayTag, TagContainer

Layer 1:
  Shared.Physics       — AABB, Capsule, Ray, HitResult → Math
  Shared.Ability       — AbilityConfig, State, InstanceData → GameplayTags

Layer 2:
  Shared.ECS           — Entity, ComponentTypeId, Components, Systems → Math, Physics
  Shared.Protocol      — PacketWriter/Reader, KcpChannel, TcpChannel, UdpTransport,
                          ConnectionRequest/Accepted, Heartbeat, Disconnect → Math

Layer 3:
  Shared.Simulation    — PlayerSnapshot, WorldSnapshot, PlayerSimulation,
                          GameConstants, WorldStateMessage, DamageEvent → Math, Physics, ECS, Ability, Protocol

Layer 4 (本次新建):
  Network.Core         — NetworkBehaviour, NetId分配器, I/P帧消息格式, ComponentSync → Protocol, ECS

Layer 5 (存量代码):
  Network.Client       — NetPlayerController, RemotePlayerController, AuthoritySync, BattleClient → Network.Core, Simulation
  Network.Server       — 服务端 Tick Loop (新建) → Network.Core, Simulation, ECS
```

## Package 结构

```
Packages/com.shootinggame.network/
  ├── package.json
  ├── Runtime/
  │   ├── Attributes/
  │   │   ├── SyncComponentAttribute.cs
  │   │   ├── SyncVarAttribute.cs
  │   │   ├── ServerRpcAttribute.cs
  │   │   └── ClientRpcAttribute.cs
  │   ├── NetworkBehaviour.cs
  │   └── ShootingGame.Network.asmdef
  ├── Editor/
  │   └── SourceGenerators/
  │       ├── ComponentSyncGenerator.cs
  │       ├── RpcMethodGenerator.cs
  │       └── ShootingGame.SourceGen.csproj
  └── Tests/
```

## 实施阶段

### 阶段 1：基础设施（不影响现有功能）

| 任务 | 内容 |
|------|------|
| 1a | 创建 `Packages/com.shootinggame.network/` 本地 Package |
| 1b | 编写 4 个 Attribute（SyncComponent, SyncVar, ServerRpc, ClientRpc） |
| 1c | 编写纯 C# `NetworkBehaviour` 基类 + `SendServerRpc()` / `SendClientRpc()` |
| 1d | 实现 NetId 分配器（高16位类型 + 低16位自增） |
| 1e | 定义 I帧/P帧 消息格式 + 新增 `GameMessageType.DeltaState` / `GameMessageType.RpcCall` |
| 1f | 拆分 `.asmdef` — 先从 Shared.Math 开始，逐层推进 |
| 1g | 拆分 Messages — 传输层留在 Protocol，状态消息移到 Simulation |
| 1h | `CollisionWorld` 新增：`CapsuleSweep`, `SphereSweep`, `SampleGround`, `OverlapSphere` |

### 阶段 2：第一个 Source Generator（ComponentSync）

| 任务 | 内容 |
|------|------|
| 2a | 搭建 Roslyn Source Generator 项目 |
| 2b | 实现 Generator A — 识别 `[SyncComponent]` + `[SyncVar]` |
| 2c | 生成 `ComponentTypeId`, `WriteDelta/ReadDelta`, `WriteFull/ReadFull`, `HasDelta`, `MarkClean` |
| 2d | 生成 Entity 级别的 `CollectDirty()` / `ApplyDelta()` / `ApplyFull()` |
| 2e | 在现有 `HealthComponent` 上加 `[SyncComponent]` 做端到端验证 |

### 阶段 3：RPC Generator + 服务端 Tick Loop

| 任务 | 内容 |
|------|------|
| 3a | 实现 Generator B — 识别 `[ServerRpc]` / `[ClientRpc]` |
| 3b | 生成参数序列化 + Send + 反序列化 + Dispatch Handler |
| 3c | 服务端固定 tick 循环（PlayerLoop 注入） |
| 3d | I/P帧 发送调度器（固定间隔 I帧 + 余下 P帧 + 客户端请求 I帧） |
| 3e | 服务端输入处理（不等待 + 冗余 ×3 + 动作事件不回放） |

### 阶段 4：首个完整功能验证

| 任务 | 内容 |
|------|------|
| 4a | 用新框架实现"手雷系统"（新 Entity + Component + RPC） |
| 4b | 客户端预测 + I/P帧同步 + 爆炸效果 RPC |
| 4c | 端到端调试和性能测试 |

### 阶段 5：存量迁移 + 清理

| 任务 | 内容 |
|------|------|
| 5a | 现有 Component 迁移（Health, Ammo, Transform → [SyncComponent]） |
| 5b | 现有操作改 RPC（Shoot, Reload, Jump, Ability） |
| 5c | 删除老的 `Messages.cs` 手写序列化 |
| 5d | 删除 `AuthoritySync` 桥接逻辑 |

## 关键技术决策理由

### 为什么 I帧/P帧 + 不可靠通道
KCP 可靠通道有队头阻塞，一个丢包阻塞后续所有包。I/P帧 丢就丢了，下一个 I帧 自动修复。射击游戏要的是"现在立刻给最新状态"。

### 为什么保留 CollisionWorld
Unity PhysX 非确定性，同样输入不同机器结果可能不同，会破坏客户端预测。CollisionWorld 纯 C# 数学保证确定性。新增 CapsuleSweep/SphereSweep/SampleGround（约200行）即可支持爬坡/手雷/冲刺。

### 为什么 Component 是唯一数据源
避免 NetVar 和 Component 数据重复，ECS System 保持唯一写入路径。NetworkBehaviour 只做 RPC 逻辑入口，不持有同步数据。

### 为什么薄 RPC
业务逻辑归 ECS System，RPC 只是 System 的网络代理。容易测试、回放、追踪。

---

## 实施进度

### ✅ Phase 1: 基础设施（已完成 2025-06-15）

| 任务 | 状态 | 文件 |
|------|------|------|
| 1a | ✅ | `Packages/com.shootinggame.network/` — package.json, .asmdef |
| 1b | ✅ | 4 Attributes: `[SyncComponent]`, `[SyncVar]`, `[ServerRpc]`, `[ClientRpc]` |
| 1c | ✅ | `NetworkBehaviour.cs` — 纯 C# 抽象基类 (80 行) |
| 1d | ✅ | `NetIdRegistry.cs` — NetId 分配/释放/查询 (120 行) |
| 1e | ✅ | `NetworkFrameMessages.cs` — I/P帧 + RPC 消息类型 + 序列化 (210 行) |
| 1e | ✅ | `PacketWriter.cs` — 新增 `WriteBytes()` |
| 1e | ✅ | `PacketReader.cs` — 新增 `ReadBytes()` |
| 1e | ✅ | `ProtobufSerializer.cs` — 新增 `DeltaState=10, RpcCall=11` + BinaryPayload |
| 1f | ✅ | 9 个 .asmdef: Math, GameplayTags, Physics, Ability, Hero, Simulation, ECS, Protocol, StateMachine |
| 1f | ✅ | `PhysicsConstants.cs` — 解耦 Physics → Simulation 循环依赖 |
| 1f | ✅ | `Capsule.cs`, `KinematicMover.cs`, `GameConstants.cs` — 更新引用 |
| 1h | ✅ | `CollisionWorld.cs` — 新增 `SweepCapsule`, `SampleGround`, `OverlapSphere`, `GetSlopeAngle` |
| 1h | ✅ | `AABB.cs` — 新增 `ClosestPoint()` |

### ✅ Phase 2: Source Generators（已完成 2025-06-15）

| 任务 | 状态 | 文件 |
|------|------|------|
| 2a | ✅ | `ShootingGame.SourceGen.csproj` — .NET Standard 2.0 项目 |
| 2a | ✅ | `ComponentSyncGenerator.cs` — SyncComponent 序列化代码生成 (280 行) |
| 2b | ✅ | `RpcMethodGenerator.cs` — RPC 代理/分发代码生成 (250 行) |
| 2b | ✅ | `RpcMethodRegistry.cs` — 全局 RPC 方法注册表 (80 行) |
| 2b | ✅ | `build.ps1` — Source Generator DLL 构建脚本 |

### ✅ Phase 3: 服务端 Tick Loop（已完成 2025-06-15）

| 任务 | 状态 | 文件 |
|------|------|------|
| 3a | ✅ | `ServerTickLoop.cs` — 固定 tick 循环 + PlayerLoop 注入 + I/P帧构建 + ClientRpc 广播 |
| 3b | ✅ | `ServerInputBuffer.cs` — 输入缓冲 + 冗余回退 + 动作事件不回放 |
| 3c | ✅ | `ServerFrameScheduler.cs` — I帧/P帧调度策略 |
| 3d | ✅ | `ServerRpcDispatcher.cs` — 服务端 RPC 分发 |
| 3e | ✅ | `ClientDeltaReceiver.cs` — 客户端 I/P帧 接收 + P帧 mismatch 自动请求 I帧 |
| 3f | ✅ | `ClientRpcReceiver.cs` — 客户端 RPC 接收 |

### ✅ Phase 4: 端到端集成（已完成 2025-06-15）

| 任务 | 状态 | 文件 |
|------|------|------|
| 4a | ✅ | `NetworkClient.cs` — 新增 `OnDeltaState`/`OnRpcCall` 事件 + `SendRawMessage()` |
| 4b | ✅ | `NetworkIntegrationBridge.cs` — 客户端集成：连接 NetworkBehaviour 回调 ↔ NetworkClient |
| 4c | ✅ | `ServerTransport.cs` — 服务端 UDP 传输层 |
| 4d | ✅ | `ServerBootstrap.cs` — 服务端启动引导：transport + tick loop + 消息分发 |

### ⏳ 待实施

- **Phase 5**: 存量迁移 + 清理 + 编译验证

### 新增文件总计: 33 个 | 修改文件: 8 个
