using System;
using System.Collections.Generic;
using ShootingGame.Shared.ECS;
using ShootingGame.Shared.Math;
using ShootingGame.Shared.Protocol;
using ShootingGame.Shared.Physics;
using ShootingGame.Shared.Simulation;
using ShootingGame.Network;
using UnityEngine;
using UnityEngine.LowLevel;

namespace ShootingGame.Network.Server
{
    /// <summary>
    /// 服务端固定 tick 循环。
    ///
    /// 流程（每个 tick）：
    /// 1. 从 ServerInputBuffer 获取所有客户端输入
    /// 2. 对所有玩家运行 ECS 模拟
    /// 3. 对每个客户端构建 I帧 或 P帧
    /// 4. 广播世界状态
    ///
    /// 使用 Unity PlayerLoop 注入，在 EarlyUpdate 接收网络数据，PostLateUpdate 发送。
    /// </summary>
    public class ServerTickLoop
    {
        /// <summary>Tick 率（Hz）</summary>
        public int TickRate { get; }

        /// <summary>每 tick 的 deltaTime（秒）</summary>
        public float TickDelta { get; }

        /// <summary>当前 tick 号</summary>
        public int CurrentTick { get; private set; } = 1;

        /// <summary>是否正在运行</summary>
        public bool IsRunning { get; private set; }

        // 核心组件
        private readonly ServerInputBuffer _inputBuffer;
        private readonly ServerFrameScheduler _scheduler;
        private readonly EntityManager _entityManager;
        private readonly CollisionWorld _collisionWorld;
        private readonly Dictionary<int, Entity> _players = new Dictionary<int, Entity>();
        private readonly Dictionary<Entity, int> _entityToPlayerId = new Dictionary<Entity, int>();

        // 模拟——使用 PlayerSimulation 和 ECS System 组
        private float _accumulator;

        // 网络回调
        private Action<int, byte[]>? _sendToClient;
        private Action<byte[]>? _onInputReceived;

        /// <summary>设置发送到指定客户端的回调</summary>
        public void SetSendCallback(Action<int, byte[]> sendCallback) => _sendToClient = sendCallback;

        /// <summary>设置收到输入时的回调</summary>
        public void SetInputReceivedCallback(Action<byte[]> onInput) => _onInputReceived = onInput;

        public ServerTickLoop(int tickRate = 20, CollisionWorld? collisionWorld = null)
        {
            TickRate = tickRate;
            TickDelta = 1f / tickRate;
            _inputBuffer = new ServerInputBuffer();
            _scheduler = new ServerFrameScheduler { IFrameInterval = 10 };
            _entityManager = new EntityManager();
            _collisionWorld = collisionWorld ?? new CollisionWorld();
        }

        /// <summary>
        /// 启动服务端 tick 循环。注入 PlayerLoop。
        /// </summary>
        [RuntimeInitializeOnLoadMethod]
        private static void AutoInit()
        {
            // 仅在 headless 模式自动启动
            if (Application.isBatchMode)
            {
                var loop = Create(20);
                loop.Start();
            }
        }

        /// <summary>
        /// 手动启动服务端 tick 循环。
        /// </summary>
        public void Start()
        {
            if (IsRunning) return;

            IsRunning = true;

            // 连接 NetworkBehaviour 的 ClientRpc 广播回调
            NetworkBehaviour.SendClientRpcTransport = OnSendClientRpc;

            InjectPlayerLoop();
            Debug.Log($"[ServerTickLoop] Started at {TickRate}Hz (delta={TickDelta:F4}s)");
        }

        /// <summary>
        /// 处理 NetworkBehaviour 发起的 ClientRpc 广播调用。
        /// rpcPayload 由 Source Generator 填充（含 NetId + MethodHash + ReqId + Args）。
        /// </summary>
        private void OnSendClientRpc(uint netId, byte[] rpcPayload, ClientRpcTarget target)
        {
            // 包装为 GameMessage 发送
            var gameMsg = new GameMessage
            {
                MsgType = GameMessageType.RpcCall,
                BinaryPayload = rpcPayload
            };
            var bytes = ProtobufSerializer.SerializeGameMessage(gameMsg);

            switch (target)
            {
                case ClientRpcTarget.All:
                    BroadcastToAll(bytes);
                    break;
                case ClientRpcTarget.Owner:
                    var ownerEntity = NetIdRegistry.GetEntity(netId);
                    if (_entityToPlayerId.TryGetValue(ownerEntity, out int ownerId))
                        _sendToClient?.Invoke(ownerId, bytes);
                    break;
                case ClientRpcTarget.Others:
                    var excludeEntity = NetIdRegistry.GetEntity(netId);
                    if (_entityToPlayerId.TryGetValue(excludeEntity, out int excludeId))
                        BroadcastToAllExcept(bytes, excludeId);
                    break;
            }
        }

        /// <summary>
        /// 广播二进制消息给所有已连接的客户端。
        /// </summary>
        private void BroadcastToAll(byte[] binaryPayload)
        {
            foreach (var playerId in _players.Keys)
                _sendToClient?.Invoke(playerId, binaryPayload);
        }

        /// <summary>
        /// 广播给除指定客户端外的所有客户端。
        /// </summary>
        private void BroadcastToAllExcept(byte[] binaryPayload, int excludePlayerId)
        {
            foreach (var playerId in _players.Keys)
                if (playerId != excludePlayerId)
                    _sendToClient?.Invoke(playerId, binaryPayload);
        }

        /// <summary>
        /// 停止服务端 tick 循环。
        /// </summary>
        public void Stop()
        {
            IsRunning = false;
            Debug.Log("[ServerTickLoop] Stopped");
        }

        /// <summary>
        /// 添加玩家到模拟中。
        /// </summary>
        public Entity AddPlayer(int playerId, Vec3 spawnPosition)
        {
            var snap = PlayerSnapshot.Default(spawnPosition);
            var entity = ECSBridge.CreatePlayerEntity(_entityManager, snap);

            _players[playerId] = entity;
            _entityToPlayerId[entity] = playerId;
            _scheduler.RegisterClient(playerId);

            // 分配 NetId
            NetIdRegistry.Allocate(NetObjectType.Player, entity);

            Debug.Log($"[ServerTickLoop] Player {playerId} added at spawn ({spawnPosition.x:F1}, {spawnPosition.y:F1}, {spawnPosition.z:F1})");
            return entity;
        }

        /// <summary>
        /// 移除玩家。
        /// </summary>
        public void RemovePlayer(int playerId)
        {
            if (_players.TryGetValue(playerId, out var entity))
            {
                _entityManager.DestroyEntity(entity);
                _players.Remove(playerId);
                _entityToPlayerId.Remove(entity);
            }
            _inputBuffer.ClearClient(playerId);
            _scheduler.UnregisterClient(playerId);
        }

        /// <summary>
        /// 接收来自客户端的输入帧（由传输层调用）。
        /// </summary>
        public void ReceiveInput(int playerId, InputFrame[] frames)
        {
            _inputBuffer.StoreInputs(playerId, frames);
        }

        /// <summary>
        /// 客户端请求 I帧。
        /// </summary>
        public void RequestIFrame(int playerId)
        {
            _scheduler.RequestIFrame(playerId);
        }

        /// <summary>
        /// 主 tick 函数（由 PlayerLoop 每帧调用）。
        /// </summary>
        public void Tick()
        {
            if (!IsRunning) return;

            _accumulator += Time.deltaTime;

            while (_accumulator >= TickDelta)
            {
                ExecuteTick(CurrentTick);
                _accumulator -= TickDelta;
                CurrentTick++;
            }
        }

        /// <summary>
        /// 执行单个 tick。
        /// </summary>
        private void ExecuteTick(int tick)
        {
            _scheduler.Tick(tick);

            // 1. 获取所有玩家的输入并写入 ECS
            foreach (var (playerId, entity) in _players)
            {
                var input = _inputBuffer.GetInput(playerId, tick);
                if (input.HasValue)
                {
                    ECSBridge.WriteInput(_entityManager, entity, input.Value);
                }
            }

            // 2. 对所有玩家执行 ECS 模拟
            foreach (var (playerId, entity) in _players)
            {
                var input = _inputBuffer.GetInput(playerId, tick);
                if (input.HasValue)
                {
                    PlayerSystemGroup.TickPlayer(_entityManager, entity, input.Value, TickDelta, _collisionWorld);
                }
            }

            // 3. 构建并发送世界状态给每个客户端
            foreach (var (playerId, entity) in _players)
            {
                if (_scheduler.ShouldSendIFrame(playerId))
                {
                    SendIFrame(playerId);
                    _scheduler.MarkIFrameSent(playerId);
                }
                else
                {
                    SendPFrame(playerId);
                }
            }
        }

        /// <summary>
        /// 发送 I帧（全量快照）给指定客户端。
        /// </summary>
        private void SendIFrame(int playerId)
        {
            var worldState = BuildWorldState(CurrentTick, isFull: true);
            var w = new PacketWriter();
            NetworkFrameSerializer.WriteDeltaState(w, worldState);

            var gameMsg = new GameMessage
            {
                MsgType = GameMessageType.DeltaState,
                BinaryPayload = w.ToArray()
            };
            var payload = ProtobufSerializer.SerializeGameMessage(gameMsg);
            _sendToClient?.Invoke(playerId, payload);
        }

        /// <summary>
        /// 发送 P帧（增量变更）给指定客户端。
        /// </summary>
        private void SendPFrame(int playerId)
        {
            var worldState = BuildWorldState(CurrentTick, isFull: false);
            // P帧无变更则跳过
            bool hasAnyChange = false;
            foreach (var entity in worldState.Entities)
            {
                if (entity.Components.Count > 0)
                {
                    hasAnyChange = true;
                    break;
                }
            }

            if (!hasAnyChange) return;

            var w = new PacketWriter();
            NetworkFrameSerializer.WriteDeltaState(w, worldState);

            var gameMsg = new GameMessage
            {
                MsgType = GameMessageType.DeltaState,
                BinaryPayload = w.ToArray()
            };
            var payload = ProtobufSerializer.SerializeGameMessage(gameMsg);
            _sendToClient?.Invoke(playerId, payload);
        }

        /// <summary>
        /// 构建全量或增量世界状态。
        /// 目前复用 PlayerSnapshot 的全量构建；增量支持待 Source Generator 运行后自动出现。
        /// </summary>
        private DeltaStateMsg BuildWorldState(int tick, bool isFull)
        {
            var msg = new DeltaStateMsg
            {
                ServerTick = tick,
                IsFull = isFull,
                BaseFrameId = isFull ? tick : tick - 1
            };

            foreach (var (playerId, entity) in _players)
            {
                var entityDelta = new EntityDelta
                {
                    NetId = NetIdRegistry.GetNetId(entity)
                };

                if (isFull)
                {
                    // 为每个 Component 构建全量快照
                    // 这里简化为复用 PlayerSnapshot 的全量构建
                    var snap = ECSBridge.BuildSnapshot(_entityManager, entity, tick);
                    BuildFullComponentDeltas(entityDelta, snap);
                }
                else
                {
                    // 增量：后续 Source Generator 自动生成的 HasAnyDelta 驱动
                    // 目前 P帧 暂不发 Component 级别增量
                }

                msg.Entities.Add(entityDelta);
            }

            return msg;
        }

        /// <summary>
        /// 构建全量 Component 增量列表（简化：目前在 EntityDelta 中附加大包数据）。
        /// 后续 Source Generator 生成 Component 级别增量后替换。
        /// </summary>
        private static void BuildFullComponentDeltas(EntityDelta entityDelta, PlayerSnapshot snap)
        {
            // 简化实现：使用现有的 PlayerStateMsg 格式作为过渡
            // 完整实现由 ComponentSyncGenerator 生成的 WriteFull/ReadFull 替代
            var w = new PacketWriter();
            // 写入临时格式：序列化 PlayerSnapshot 作为整包
            w.WriteInt32(snap.Tick);
            w.WriteVec3(snap.Position);
            w.WriteQuat(snap.Rotation);
            w.WriteVec3(snap.Velocity);
            w.WriteFloat(snap.VerticalVelocity);
            w.WriteBool(snap.IsGrounded);
            w.WriteByte((byte)snap.State);
            w.WriteFloat(snap.FireCooldown);
            w.WriteByte(snap.Health);
            w.WriteInt32(snap.CurrentAmmo);
            w.WriteBool(snap.IsReloading);
            w.WriteFloat(snap.ReloadTimer);
            w.WriteInt64(snap.TagBitmask);

            entityDelta.Components.Add(new ComponentDelta
            {
                ComponentTypeId = 0, // Full-state placeholder
                IsFull = true,
                Data = w.ToArray()
            });
        }

        // ==================== PlayerLoop 注入 ====================

        private void InjectPlayerLoop()
        {
            var playerLoop = PlayerLoop.GetCurrentPlayerLoop();

            for (int i = 0; i < playerLoop.subSystemList.Length; i++)
            {
                // 在 EarlyUpdate 阶段插入网络接收
                if (playerLoop.subSystemList[i].type == typeof(UnityEngine.PlayerLoop.EarlyUpdate))
                {
                    var subSystems = new List<PlayerLoopSystem>(playerLoop.subSystemList[i].subSystemList);
                    subSystems.Insert(0, new PlayerLoopSystem
                    {
                        type = typeof(ServerTickLoop),
                        updateDelegate = UpdateNetworkReceive
                    });
                    playerLoop.subSystemList[i].subSystemList = subSystems.ToArray();
                }

                // 在 PostLateUpdate 阶段插入 tick 执行 + 网络发送
                if (playerLoop.subSystemList[i].type == typeof(UnityEngine.PlayerLoop.PostLateUpdate))
                {
                    var subSystems = new List<PlayerLoopSystem>(playerLoop.subSystemList[i].subSystemList);
                    subSystems.Insert(0, new PlayerLoopSystem
                    {
                        type = typeof(ServerTickLoop),
                        updateDelegate = UpdateTickAndSend
                    });
                    playerLoop.subSystemList[i].subSystemList = subSystems.ToArray();
                }
            }

            PlayerLoop.SetPlayerLoop(playerLoop);
        }

        private void UpdateNetworkReceive()
        {
            // 由传输层回调填充，此处为占位
            // 实际网络接收在 UdpTransport 层面异步处理
        }

        private void UpdateTickAndSend()
        {
            Tick();
        }

        // ==================== 单例 ====================

        private static ServerTickLoop? _instance;
        public static ServerTickLoop? Instance => _instance;

        public static ServerTickLoop Create(int tickRate = 20, CollisionWorld? collisionWorld = null)
        {
            _instance = new ServerTickLoop(tickRate, collisionWorld);
            return _instance;
        }
    }
}
