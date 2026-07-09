using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ShootingGame.Shared.ECS;
using ShootingGame.Shared.Protocol;
using ShootingGame.Shared.Simulation;
using ShootingGame.Shared.Physics;
using SharedVec2 = ShootingGame.Shared.Math.Vec2;
using SharedVec3 = ShootingGame.Shared.Math.Vec3;

/// <summary>
/// RPC 攻击条目——PlayerCombatBehaviour 入队，HostBattleServer 消费。
/// </summary>
public struct AttackEntry
{
    public ShootingGame.Shared.ECS.Entity Entity;
    public int ClientId;
    public float TowardX;
    public float TowardY;
    public float AimPitch;
    public int AttackId;
}

namespace ShootingGame.Network.Server
{
    /// <summary>
    /// Host 模式战斗服务。用 MonoBehaviour Update 驱动，不注入 PlayerLoop。
    /// 简单模拟所有玩家，广播 BattleFrame 给所有客户端。
    /// </summary>
    public class HostBattleServer : MonoBehaviour
    {
        /// <summary>由 PlayerCombatBehaviour RPC 入队的待处理攻击</summary>
        public static readonly System.Collections.Generic.Queue<AttackEntry> PendingAttacks =
            new System.Collections.Generic.Queue<AttackEntry>();
        [Header("Tick 设置")]
        [SerializeField] private int _tickRate = 60;
        [SerializeField] private float _tickInterval = 1f / 60f;

        private readonly Dictionary<int, PlayerSlot> _players = new Dictionary<int, PlayerSlot>();
        private readonly Dictionary<int, int> _udpToPlayerId = new Dictionary<int, int>(); // UDP clientId → BattlePlayerId
        private CollisionWorld _collisionWorld;
        private ServerTransport _transport;
        private int _currentTick;
        private float _accumulator;

        private class PlayerSlot
        {
            public int ClientId;
            public PlayerSnapshot Snapshot;
            public InputFrame? LatestInput;
            public PlayerOperation? LatestOp;
            public List<AttackOperation> PendingBroadcastAttacks;
            public bool IsReady;
            public float ReloadTimer;
        }

        public bool IsRunning { get; private set; }

        private void Awake()
        {
            _tickInterval = 1f / _tickRate;
            // 尝试加载和客户端一致的碰撞数据
            _collisionWorld = CollisionWorldLoader.Instance;
            if (_collisionWorld == null || _collisionWorld.Count == 0)
            {
                _collisionWorld = new CollisionWorld();
                _collisionWorld.AddBox(new AABB(new SharedVec3(-50, -1, -50), new SharedVec3(50, 0, 50)));
                Debug.Log("[HostBattleServer] 使用默认地面碰撞 (50×1×50)");
            }
            else
            {
                Debug.Log($"[HostBattleServer] 加载碰撞数据: {_collisionWorld.Count} boxes");
            }
        }

        public void StartServer(ServerTransport transport)
        {
            _transport = transport;
            _transport.OnMessageReceived += OnBattleMessage;
            IsRunning = true;
            _currentTick = 1;
            Debug.Log($"[HostBattleServer] Started at {_tickRate}Hz");
        }

        public void StopServer()
        {
            IsRunning = false;
            if (_transport != null)
                _transport.OnMessageReceived -= OnBattleMessage;
        }

        private void OnDestroy()
        {
            StopServer();
        }

        private void Update()
        {
            if (!IsRunning) return;

            _accumulator += Time.unscaledDeltaTime;
            while (_accumulator >= _tickInterval)
            {
                Tick();
                _accumulator -= _tickInterval;
            }
        }

        // ==================== Tick ====================

        private void Tick()
        {
            // 0. 刷新碰撞世界（Fight 场景加载后 CollisionWorldLoader 可能有新数据）
            var world = CollisionWorldLoader.Instance;
            if (world != null && world.Count > 0 && world != _collisionWorld)
            {
                _collisionWorld = world;
                Debug.Log($"[HostBattleServer] CollisionWorld refreshed: {_collisionWorld.Count} boxes");
            }

            // 1a. 消费 RPC 入队的攻击（新框架）
            while (PendingAttacks.Count > 0)
            {
                var entry = PendingAttacks.Dequeue();
                var atk = new ShootingGame.Shared.Protocol.AttackOperation
                {
                    AttackId = entry.AttackId,
                    TowardX = entry.TowardX, TowardY = entry.TowardY,
                    AimPitch = entry.AimPitch, ClientFrameId = entry.AttackId
                };
                // 把攻击存到对应 PlayerSlot
                if (_players.TryGetValue(entry.ClientId, out var rpcSlot))
                {
                    if (rpcSlot.LatestOp == null)
                        rpcSlot.LatestOp = new PlayerOperation { PlayerId = entry.ClientId };
                    if (rpcSlot.LatestOp.AttackOperations == null)
                        rpcSlot.LatestOp.AttackOperations = new List<AttackOperation>();
                    rpcSlot.LatestOp.AttackOperations.Add(atk);
                }
            }

            // 1b. 模拟所有就绪玩家 + 处理攻击
            var hitEvents = new List<HitEventMsg>();

            foreach (var (clientId, slot) in _players)
            {
                if (!slot.IsReady) continue;

                // 服务端弹药消耗
                if (slot.LatestInput?.Fire == true && slot.Snapshot.CurrentAmmo > 0 && !slot.Snapshot.IsReloading)
                    slot.Snapshot.CurrentAmmo--;

                // 服务端换弹
                if (slot.Snapshot.IsReloading)
                {
                    slot.ReloadTimer -= _tickInterval;
                    if (slot.ReloadTimer <= 0f)
                    {
                        slot.Snapshot.CurrentAmmo = GameConstants.MaxAmmoPerClip;
                        slot.Snapshot.IsReloading = false;
                        slot.ReloadTimer = 0f;
                    }
                }
                else if (slot.LatestInput?.Reload == true && slot.Snapshot.CurrentAmmo < GameConstants.MaxAmmoPerClip)
                {
                    slot.Snapshot.IsReloading = true;
                    slot.ReloadTimer = GameConstants.ReloadTime;
                }
                slot.Snapshot.Tick = _currentTick;

                // 处理攻击（hitscan），然后移到广播列表
                if (slot.LatestOp?.AttackOperations != null && slot.LatestOp.AttackOperations.Count > 0)
                {
                    Debug.Log($"[HostBattleServer] TICK_ATK: player={clientId} count={slot.LatestOp.AttackOperations.Count}");
                    foreach (var atk in slot.LatestOp.AttackOperations)
                        ProcessAttack(clientId, atk, hitEvents);
                    slot.PendingBroadcastAttacks = slot.LatestOp.AttackOperations;
                    slot.LatestOp.AttackOperations = new List<AttackOperation>();
                }
            }

            // 2a. 构建 BattleFrame（现有协议） + DeltaState（I帧 或 P帧）
            bool isFull = _currentTick % 10 == 0;
            var allOps = new List<PlayerOperation>();
            var allStates = new List<PlayerStateMsg>();
            var deltaState = new DeltaStateMsg
            {
                ServerTick = _currentTick,
                IsFull = isFull,
                BaseFrameId = _currentTick
            };

            foreach (var (clientId, slot) in _players)
            {
                if (!slot.IsReady) continue;
                var snap = slot.Snapshot;
                var op = slot.LatestOp;

                // BattleFrame（现有）
                allStates.Add(new PlayerStateMsg
                {
                    PlayerId = clientId,
                    Position = snap.Position, Hp = snap.Health, IsDead = snap.Health <= 0,
                    Velocity = snap.Velocity, VerticalVelocity = snap.VerticalVelocity,
                    IsGrounded = snap.IsGrounded, StateEnum = (int)snap.State,
                    FireCooldown = snap.FireCooldown,
                    RotationY = op?.AimYaw ?? 0f, IsRunning = op?.Run ?? false,
                    CurrentAmmo = snap.CurrentAmmo, IsReloading = snap.IsReloading,
                    TagBitmask = snap.TagBitmask, MaxHp = GameConstants.MaxHealth
                });

                if (op != null)
                {
                    allOps.Add(new PlayerOperation
                    {
                        PlayerId = clientId,
                        MoveX = op.MoveX, MoveY = op.MoveY, AimYaw = op.AimYaw, AimPitch = op.AimPitch,
                        Fire = op.Fire, Jump = op.Jump, Run = op.Run, Aim = op.Aim, Reload = op.Reload,
                        ClientFrameId = op.ClientFrameId,
                        AttackOperations = slot.PendingBroadcastAttacks ?? new List<AttackOperation>()
                    });
                    // 边缘触发只广播一次后清除，防止下一 tick 继续触发特效
                    op.Fire = false;
                    op.Reload = false;
                    op.Jump = false;
                    slot.PendingBroadcastAttacks = null;
                }

                // DeltaState: I帧用 WriteFull，P帧用 WriteDelta
                // 注意：Transform/Movement 位置由 BattleFrame 同步，DeltaState 只做 Health 验证
                var entityDelta = new EntityDelta { NetId = (uint)clientId };
                var compWriter = new PacketWriter();

                // Health（DeltaState 验证——新框架唯一同步的非位置数据）
                var hp = new HealthComponent(snap.Health, GameConstants.MaxHealth);
                if (isFull || hp.HasAnyDelta)
                {
                    compWriter.Reset();
                    if (isFull) hp.WriteFull(compWriter); else hp.WriteDelta(compWriter);
                    entityDelta.Components.Add(new ComponentDelta { ComponentTypeId = HealthComponent.ComponentTypeId, IsFull = isFull, Data = compWriter.ToArray() });
                }

                if (entityDelta.Components.Count > 0)
                    deltaState.Entities.Add(entityDelta);

                // 重置脏标记
                hp.MarkClean();
            }

            // 3a. 广播 BattleFrame（现有协议）
            foreach (var (clientId, _) in _players)
            {
                var frame = new AllPlayerOperation
                {
                    FrameId = _currentTick,
                    Operations = allOps,
                    PlayerStates = allStates,
                    HitEvents = hitEvents
                };
                var response = new MainPack
                {
                    RequestCode = RequestCode.Battle,
                    ActionCode = ActionCode.BattleFrame,
                    BattleInfo = new BattleInfo
                    {
                        OperationId = _currentTick,
                        AllPlayerOperations = new List<AllPlayerOperation> { frame },
                        HitEvents = hitEvents
                    }
                };
                _transport.Send(clientId, ProtobufSerializer.SerializeMainPack(response));
            }

            // 3b. 广播 DeltaState（I帧每 10 tick，P帧每 tick 有变更时）
            if (deltaState.Entities.Count > 0)
            {
                var dsWriter = new PacketWriter();
                NetworkFrameSerializer.WriteDeltaState(dsWriter, deltaState);
                var dsPack = new MainPack
                {
                    RequestCode = RequestCode.Battle,
                    ActionCode = ActionCode.DeltaState,
                    RpcPayload = dsWriter.ToArray()
                };
                var dsBytes = ProtobufSerializer.SerializeMainPack(dsPack);
                foreach (var (clientId, _) in _players)
                    _transport.Send(clientId, dsBytes);
            }

            _currentTick++;

            // 检查游戏结束
            CheckGameOver();

            // 每秒诊断一次
            if (_currentTick % 60 == 0)
            {
                foreach (var (pid, s) in _players)
                    if (s.IsReady)
                        Debug.Log($"[HostBattleServer] tick={_currentTick} player={pid} pos=({s.Snapshot.Position.x:F2},{s.Snapshot.Position.y:F2},{s.Snapshot.Position.z:F2}) ground={s.Snapshot.IsGrounded}");
            }
        }

        private bool _gameOverSent;
        private int _expectedPlayerCount = 0; // 匹配完成后由 LocalServerStarter 设置

        public void SetExpectedPlayerCount(int count) { _expectedPlayerCount = count; }

        /// <summary>
        /// 检查游戏是否结束。等全员 BattleReady 后才开始判定。
        /// </summary>
        private void CheckGameOver()
        {
            if (_gameOverSent) return;

            // 等全员就绪
            int readyCount = 0;
            foreach (var (_, s) in _players) { if (s.IsReady) readyCount++; }
            if (readyCount < _expectedPlayerCount || readyCount < 2) return;

            int aliveCount = 0;
            int lastAliveId = -1;
            foreach (var (pid, slot) in _players)
            {
                if (slot.IsReady && slot.Snapshot.Health > 0)
                {
                    aliveCount++;
                    lastAliveId = pid;
                }
            }

            if (aliveCount <= 1)
            {
                _gameOverSent = true;
                // 存活者队伍获胜（Player 1→Team1, Player 2→Team2）
                int winnerTeam = lastAliveId % 2 == 1 ? 1 : 2;
                Debug.Log($"[HostBattleServer] Game Over! Winner: player {lastAliveId} team={winnerTeam}");
                var gameOver = new MainPack
                {
                    RequestCode = RequestCode.Battle,
                    ActionCode = ActionCode.GameOver,
                    Str = winnerTeam.ToString()
                };
                var bytes = ProtobufSerializer.SerializeMainPack(gameOver);
                foreach (var (cid, _) in _players) _transport.Send(cid, bytes);
            }
        }

        /// <summary>
        /// 处理单次攻击——Hitscan 判定。结果直接添加到 hitEvents 列表。
        /// </summary>
        private void ProcessAttack(int attackerId, AttackOperation atk, List<HitEventMsg> hitEvents)
        {
            if (!_players.TryGetValue(attackerId, out var attackerSlot)) return;

            var origin = attackerSlot.Snapshot.Position +
                SharedVec3.Up * (GameConstants.PlayerHeight * 0.85f);

            float aimYaw = Mathf.Atan2(atk.TowardX, atk.TowardY) * Mathf.Rad2Deg;
            var dir = UnityEngine.Quaternion.Euler(atk.AimPitch, aimYaw, 0f) * Vector3.forward;
            var direction = new SharedVec3(dir.x, dir.y, dir.z);

            float closestDist = GameConstants.HitscanRange;
            int victimId = -1;
            SharedVec3 hitPoint = SharedVec3.Zero;

            foreach (var (targetId, targetSlot) in _players)
            {
                if (targetId == attackerId) continue;
                if (targetSlot.Snapshot.Health <= 0) continue;

                var targetPos = targetSlot.Snapshot.Position;
                var capsule = new Capsule(targetPos, GameConstants.PlayerHeight, GameConstants.HitCapsuleRadius);
                var aabb = capsule.BoundingBox();
                var ray = new ShootingGame.Shared.Physics.Ray(origin, direction);

                // 诊断第一个攻击
                Debug.Log($"[HostBattleServer] Hitscan: attacker={attackerId} origin=({origin.x:F2},{origin.y:F2},{origin.z:F2}) dir=({direction.x:F2},{direction.y:F2},{direction.z:F2}) target={targetId} pos=({targetPos.x:F2},{targetPos.y:F2},{targetPos.z:F2}) aabb=({aabb.Min.x:F2},{aabb.Max.x:F2})");

                var hit = Intersection.RayAABB(ray, aabb, closestDist);
                if (hit.Hit && hit.Distance < closestDist)
                {
                    closestDist = hit.Distance;
                    victimId = targetId;
                    hitPoint = hit.Point;
                    Debug.Log($"[HostBattleServer] Hitscan HIT: target={targetId} dist={closestDist:F2}");
                }
            }

            if (victimId < 0) { Debug.Log($"[HostBattleServer] Hitscan MISS: attacker={attackerId}"); return; }

            // 应用伤害
            var victimSlot = _players[victimId];
            byte damage = GameConstants.HitscanDamage;
            byte newHp = (byte)Mathf.Max(0, victimSlot.Snapshot.Health - damage);
            victimSlot.Snapshot.Health = newHp;

            Debug.Log($"[HostBattleServer] HIT: attacker={attackerId} victim={victimId} dmg={damage} newHp={newHp}");

            hitEvents.Add(new HitEventMsg
            {
                AttackId = atk.AttackId,
                AttackerId = attackerId,
                VictimId = victimId,
                Damage = damage,
                IsKill = newHp == 0,
                HitPoint = hitPoint,
                HitFrameId = _currentTick
            });
        }

        // ==================== 消息处理 ====================

        private void OnBattleMessage(byte[] data, int clientId)
        {
            try
            {
                MainPack pack;
                try { pack = ProtobufSerializer.DeserializeMainPack(data); }
                catch { return; } // 不是 MainPack（可能是 GameMessage）

                switch (pack.ActionCode)
                {
                    case ActionCode.BattleReady:
                        HandleBattleReady(clientId, pack);
                        break;
                    case ActionCode.BattleOperation:
                        HandleBattleOperation(clientId, pack);
                        break;
                    case ActionCode.Ping:
                        var pong = new MainPack
                        {
                            RequestCode = RequestCode.Battle, ActionCode = ActionCode.Pong,
                            Timestamp = pack.Timestamp
                        };
                        _transport.Send(clientId, ProtobufSerializer.SerializeMainPack(pong));
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[HostBattleServer] msg error: {ex.Message}");
            }
        }

        private void HandleBattleReady(int clientId, MainPack pack)
        {
            // 用 BattlePlayerId（MatchFound 分配）做 key，保证和客户端一致
            int battlePlayerId = pack.BattleInfo?.OperationId ?? clientId;
            _udpToPlayerId[clientId] = battlePlayerId;

            if (!_players.TryGetValue(battlePlayerId, out var slot))
            {
                slot = new PlayerSlot { ClientId = battlePlayerId };
                _players[battlePlayerId] = slot;
            }

            var spawnPos = GetSpawnPos(battlePlayerId);
            slot.Snapshot = PlayerSnapshot.Default(spawnPos);
            slot.IsReady = true;

            // 回复 BattleStart
            var start = new MainPack
            {
                RequestCode = RequestCode.Battle, ActionCode = ActionCode.BattleStart,
                ReturnCode = ReturnCode.Success
            };
            _transport.Send(clientId, ProtobufSerializer.SerializeMainPack(start));
            Debug.Log($"[HostBattleServer] Player {battlePlayerId} (UDP:{clientId}) is ready, spawn=({spawnPos.x:F1},{spawnPos.z:F1})");
        }

        private void HandleBattleOperation(int clientId, MainPack pack)
        {
            var op = pack.BattleInfo?.SelfOperation;
            if (op == null) return;

            int playerId = _udpToPlayerId.TryGetValue(clientId, out var pid) ? pid : clientId;

            // 诊断攻击操作
            if (op.AttackOperations != null && op.AttackOperations.Count > 0)
                Debug.Log($"[HostBattleServer] RECV ATTACK: player={playerId} UDP={clientId} atkCount={op.AttackOperations.Count} atkId={op.AttackOperations[0].AttackId}");

            var input = new InputFrame
            {
                Tick = pack.BattleInfo.OperationId,
                Movement = new SharedVec2(op.MoveX, op.MoveY),
                Jump = op.Jump, Run = op.Run, Aim = op.Aim,
                Fire = op.Fire, Reload = op.Reload,
                AimYaw = op.AimYaw, AimPitch = op.AimPitch
            };

            if (_players.TryGetValue(playerId, out var slot))
            {
                slot.LatestInput = input;
                // 直接用客户端预测位置——不服务端模拟
                slot.Snapshot.Position = new SharedVec3(op.PosX, op.PosY, op.PosZ);
                slot.Snapshot.Velocity = new SharedVec3(op.VelX, 0, op.VelZ);
                slot.Snapshot.IsGrounded = op.IsGrounded;
                if (slot.LatestOp?.AttackOperations != null && slot.LatestOp.AttackOperations.Count > 0)
                    op.AttackOperations.InsertRange(0, slot.LatestOp.AttackOperations);
                slot.LatestOp = op;
            }
        }

        private static SharedVec3 GetSpawnPos(int playerId)
        {
            // 玩家分散生成：间隔 8m
            float x = (playerId - 1) * 8f - 4f;
            float z = (playerId % 2 == 0) ? 4f : -4f;
            return new SharedVec3(x, 0.1f, z);
        }
    }
}
