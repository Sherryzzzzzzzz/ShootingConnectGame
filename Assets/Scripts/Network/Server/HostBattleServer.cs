using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ShootingGame.Shared.Ability;
using ShootingGame.Shared.ECS;
using ShootingGame.Shared.GameplayTags;
using ShootingGame.Shared.Hero;
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
        private const int SnapshotIntervalTicks = 2;
        private const int FullSnapshotIntervalTicks = 10;

        /// <summary>由 PlayerCombatBehaviour RPC 入队的待处理攻击</summary>
        public static readonly System.Collections.Generic.Queue<AttackEntry> PendingAttacks =
            new System.Collections.Generic.Queue<AttackEntry>();
        [Header("Tick 设置")]
        [SerializeField] private int _tickRate = 60;
        [SerializeField] private float _tickInterval = 1f / 60f;

        private readonly Dictionary<int, PlayerSlot> _players = new Dictionary<int, PlayerSlot>();
        private readonly Dictionary<int, BattlePlayerInfo> _playerSelections = new Dictionary<int, BattlePlayerInfo>();
        private readonly List<SpawnPointMsg> _spawnPoints = new List<SpawnPointMsg>();
        private readonly List<HitEventMsg> _pendingHitEvents = new List<HitEventMsg>(8);
        private readonly List<AbilityEventData> _pendingAbilityConfirmations = new List<AbilityEventData>(4);
        private readonly object _selectionLock = new object();
        private readonly Dictionary<int, int> _udpToPlayerId = new Dictionary<int, int>(); // UDP clientId → BattlePlayerId
        private CollisionWorld _collisionWorld;
        private HostServerEcsWorld _ecsWorld;
        private ServerTransport _transport;
        private int _currentTick;
        private float _accumulator;
        private int _battleId;
        private int _gameMode = 1;
        private int _livesPerPlayer = GameConstants.DeathmatchLives;
        private bool _matchResetRequested;
        private bool _sceneSpawnPointsAuthoritative;

        private class PlayerSlot
        {
            public int ClientId;
            public InputFrame? LatestInput;
            public PlayerOperation? LatestOp;
            public List<AttackOperation> PendingBroadcastAttacks;
            public bool IsReady;
            public int Kills;
            public int Deaths;
            public int RespawnTick;
            public int LastSpawnIndex = -1;
            public ShootingGame.Shared.Hero.GunConfigData Gun;  // 从 HeroRegistry 解析
            public ShootingGame.Shared.Hero.HeroConfig HeroCfg; // 从 HeroRegistry 解析
        }

        public bool IsRunning { get; private set; }

        private void Awake()
        {
            _tickInterval = 1f / _tickRate;
            GameplayTagConfig.Initialize();
            HeroRegistry.Initialize();
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

            _ecsWorld = new HostServerEcsWorld(_collisionWorld);
        }

        public void StartServer(ServerTransport transport)
        {
            _transport = transport;
            _transport.OnMessageReceived += OnBattleMessage;
            IsRunning = true;
            _currentTick = 1;
            Debug.Log($"[HostBattleServer] Started at {_tickRate}Hz");
        }

        public void SetPlayerSelections(
            BattleInfo battleInfo,
            bool sceneSpawnPointsAuthoritative = false)
        {
            lock (_selectionLock)
            {
                _playerSelections.Clear();
                _spawnPoints.Clear();
                _sceneSpawnPointsAuthoritative = sceneSpawnPointsAuthoritative;
                if (battleInfo == null) return;

                if (battleInfo.BattleId != _battleId)
                {
                    _battleId = battleInfo.BattleId;
                    _matchResetRequested = true;
                }

                _gameMode = battleInfo.GameMode;
                _livesPerPlayer = battleInfo.LivesPerPlayer > 0
                    ? battleInfo.LivesPerPlayer
                    : GameConstants.DeathmatchLives;

                if (battleInfo.SpawnPoints != null)
                {
                    foreach (var spawn in battleInfo.SpawnPoints)
                    {
                        _spawnPoints.Add(new SpawnPointMsg
                        {
                            Position = spawn.Position,
                            Yaw = spawn.Yaw,
                            TeamId = spawn.TeamId
                        });
                    }
                }

                if (battleInfo.BattlePlayers == null) return;
                foreach (var player in battleInfo.BattlePlayers)
                    _playerSelections[player.PlayerId] = player;
            }

            if (sceneSpawnPointsAuthoritative)
                ApplySceneSpawnCorrections();
        }

        private void ApplySceneSpawnCorrections()
        {
            foreach (var (playerId, slot) in _players)
            {
                if (!slot.IsReady) continue;

                BattlePlayerInfo selection;
                lock (_selectionLock)
                {
                    if (!_playerSelections.TryGetValue(playerId, out selection))
                        continue;
                }

                PlayerSnapshot snapshot = _ecsWorld.GetSnapshot(playerId, _currentTick);
                snapshot.Position = selection.SpawnPosition;
                snapshot.Velocity = new SharedVec3(0f, 0f, 0f);
                snapshot.VerticalVelocity = 0f;
                _ecsWorld.ApplySnapshot(playerId, snapshot);
                slot.LastSpawnIndex = FindSpawnIndex(selection.SpawnPosition);
            }
        }

        public void StopServer()
        {
            IsRunning = false;
            if (_transport != null)
                _transport.OnMessageReceived -= OnBattleMessage;
            _ecsWorld?.Clear();
        }

        private void OnDestroy()
        {
            StopServer();
        }

        private void Update()
        {
            if (!IsRunning) return;

            ApplyPendingMatchReset();

            _accumulator += Time.unscaledDeltaTime;
            while (_accumulator >= _tickInterval)
            {
                Tick();
                _accumulator -= _tickInterval;
            }
        }

        private void ApplyPendingMatchReset()
        {
            lock (_selectionLock)
            {
                if (!_matchResetRequested) return;
                _matchResetRequested = false;
            }

            _ecsWorld.Clear();
            _players.Clear();
            _udpToPlayerId.Clear();
            _pendingHitEvents.Clear();
            _pendingAbilityConfirmations.Clear();
            PendingAttacks.Clear();
            _gameOverSent = false;
            _battleStartTick = -1;
            _currentTick = 1;
            _accumulator = 0f;
            Debug.Log($"[HostBattleServer] Reset match state for BattleId={_battleId}");
        }

        // ==================== Tick ====================

        private void Tick()
        {
            ProcessRespawns();

            // 0. 刷新碰撞世界（Fight 场景加载后 CollisionWorldLoader 可能有新数据）
            var world = CollisionWorldLoader.Instance;
            if (world != null && world.Count > 0 && world != _collisionWorld)
            {
                _collisionWorld = world;
                _ecsWorld.SetCollisionWorld(world);
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
            foreach (var (clientId, slot) in _players)
            {
                if (!slot.IsReady) continue;

                // 所有玩家规则（移动、重力、碰撞、弹药和换弹）由 ECS World 执行。
                ProcessAbilityEvents(clientId, slot);

                if (slot.LatestInput.HasValue)
                    _ecsWorld.TickPlayer(clientId, slot.LatestInput.Value, _tickInterval);

                // 处理攻击（hitscan），然后移到广播列表
                if (slot.LatestOp?.AttackOperations != null && slot.LatestOp.AttackOperations.Count > 0)
                {
                    foreach (var atk in slot.LatestOp.AttackOperations)
                        ProcessAttack(clientId, atk, _pendingHitEvents);
                    if (slot.PendingBroadcastAttacks == null)
                        slot.PendingBroadcastAttacks = new List<AttackOperation>();
                    slot.PendingBroadcastAttacks.AddRange(slot.LatestOp.AttackOperations);
                    slot.LatestOp.AttackOperations.Clear();
                }
            }

            if (!ShouldBroadcastSnapshot(_currentTick))
            {
                _currentTick++;
                CheckGameOver();
                return;
            }

            // 2a. 构建 BattleFrame（现有协议） + DeltaState（I帧 或 P帧）
            bool isFull = _currentTick % FullSnapshotIntervalTicks == 0;
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
                var snap = _ecsWorld.GetSnapshot(clientId, _currentTick);
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
                    IsAiming = op?.Aim ?? false,
                    IsCrouching = op?.Crouch ?? false,
                    CurrentAmmo = snap.CurrentAmmo, IsReloading = snap.IsReloading,
                    TagBitmask = snap.TagBitmask, MaxHp = (slot.HeroCfg?.MaxHP ?? GameConstants.MaxHealth),
                    Kills = slot.Kills, Deaths = slot.Deaths
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
                    op.Fire = false;
                    op.Reload = false;
                    op.Jump = false;
                    slot.PendingBroadcastAttacks = null;
                }

                if (isFull)
                {
                    var entityDelta = new EntityDelta { NetId = (uint)clientId };
                    var compWriter = new PacketWriter();
                    byte maxHp = slot.HeroCfg?.MaxHP ?? GameConstants.MaxHealth;
                    var hp = new HealthComponent(snap.Health, maxHp);
                    hp.WriteFull(compWriter);
                    entityDelta.Components.Add(new ComponentDelta { ComponentTypeId = HealthComponent.ComponentTypeId, IsFull = true, Data = compWriter.ToArray() });
                    deltaState.Entities.Add(entityDelta);
                }
            }

            // 3a. 广播 BattleFrame（现有协议）
            var frame = new AllPlayerOperation
            {
                FrameId = _currentTick,
                Operations = allOps,
                PlayerStates = allStates,
                HitEvents = _pendingHitEvents,
                AbilityEvents = _pendingAbilityConfirmations
            };
            var response = new MainPack
            {
                RequestCode = RequestCode.Battle,
                ActionCode = ActionCode.BattleFrame,
                BattleInfo = new BattleInfo
                {
                    OperationId = _currentTick,
                    AllPlayerOperations = new List<AllPlayerOperation> { frame },
                    HitEvents = _pendingHitEvents
                }
            };
            byte[] frameBytes = ProtobufSerializer.SerializeMainPack(response);
            foreach (var (clientId, _) in _players)
                _transport.Send(clientId, frameBytes);
            _pendingHitEvents.Clear();
            _pendingAbilityConfirmations.Clear();

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
        }

        private static bool ShouldBroadcastSnapshot(int tick)
        {
            return tick % SnapshotIntervalTicks == 0;
        }

        private bool _gameOverSent;
        private int _battleStartTick = -1;
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

            if (_battleStartTick < 0)
                _battleStartTick = _currentTick;

            bool timedOut = (_currentTick - _battleStartTick) * _tickInterval >= GameConstants.MatchDurationSeconds;

            int survivorCount = 0;
            int lastSurvivorId = -1;
            foreach (var (pid, slot) in _players)
            {
                if (slot.IsReady && HasRemainingLives(slot.Deaths, _livesPerPlayer))
                {
                    survivorCount++;
                    lastSurvivorId = pid;
                }
            }

            if (!timedOut && survivorCount > 1)
                return;

            if (timedOut)
            {
                int bestKills = int.MinValue;
                lastSurvivorId = -1;
                foreach (var (pid, slot) in _players)
                {
                    if (!slot.IsReady) continue;
                    bool better = slot.Kills > bestKills
                        || (slot.Kills == bestKills && (lastSurvivorId < 0 || pid < lastSurvivorId));
                    if (better)
                    {
                        bestKills = slot.Kills;
                        lastSurvivorId = pid;
                    }
                }
            }

            if (timedOut || survivorCount <= 1)
            {
                _gameOverSent = true;
                Debug.Log($"[HostBattleServer] Game Over! reason={(timedOut ? "time-limit" : "last-survivor")}, winner: player {lastSurvivorId}");
                var gameOver = new MainPack
                {
                    RequestCode = RequestCode.Battle,
                    ActionCode = ActionCode.GameOver,
                    IntVal = 1,
                    Str = lastSurvivorId.ToString()
                };
                foreach (var (pid, slot) in _players)
                {
                    string playerName = null;
                    lock (_selectionLock)
                    {
                        if (_playerSelections.TryGetValue(pid, out var selection))
                            playerName = selection.PlayerName;
                    }
                    gameOver.ScoreEntries.Add(new ScoreEntryMsg
                    {
                        PlayerId = pid,
                        PlayerName = playerName,
                        Kills = slot.Kills,
                        Deaths = slot.Deaths
                    });
                }
                var bytes = ProtobufSerializer.SerializeMainPack(gameOver);
                foreach (var (cid, _) in _players) _transport.Send(cid, bytes);
            }
        }

        private static bool HasRemainingLives(int deaths, int livesPerPlayer)
        {
            return deaths < Mathf.Max(1, livesPerPlayer);
        }

        private void ProcessRespawns()
        {
            foreach (var (playerId, slot) in _players)
            {
                if (!slot.IsReady || slot.RespawnTick <= 0 || _currentTick < slot.RespawnTick)
                    continue;

                var spawnPos = GetSpawnPos(playerId);
                var snapshot = CreateSpawnSnapshot(slot, spawnPos);
                _ecsWorld.RegisterPlayer(playerId, snapshot);
                _ecsWorld.ConfigurePlayer(playerId, slot.HeroCfg, slot.Gun);
                slot.RespawnTick = 0;
                slot.LatestInput = null;
                if (slot.LatestOp != null)
                {
                    slot.LatestOp.Fire = false;
                    slot.LatestOp.Reload = false;
                    slot.LatestOp.Jump = false;
                    slot.LatestOp.AttackOperations?.Clear();
                }
                Debug.Log($"[HostBattleServer] Player {playerId} respawned with {_livesPerPlayer - slot.Deaths} lives remaining.");
            }
        }

        /// <summary>
        /// 处理单次攻击——Hitscan 判定。结果直接添加到 hitEvents 列表。
        /// </summary>
        private void ProcessAttack(int attackerId, AttackOperation atk, List<HitEventMsg> hitEvents)
        {
            if (!_players.TryGetValue(attackerId, out var attackerSlot)) return;

            var gun = attackerSlot.Gun;
            float range = gun?.Range ?? GameConstants.HitscanRange;
            byte baseDamage = gun?.Damage ?? GameConstants.HitscanDamage;
            float playerHeight = attackerSlot.HeroCfg?.PlayerHeight ?? GameConstants.PlayerHeight;

            var attackerSnapshot = _ecsWorld.GetSnapshot(attackerId, _currentTick);
            if (attackerSnapshot.Health <= 0) return;
            var origin = attackerSnapshot.Position +
                SharedVec3.Up * (playerHeight * 0.85f);

            float aimYaw = Mathf.Atan2(atk.TowardX, atk.TowardY) * Mathf.Rad2Deg;
            var dir = UnityEngine.Quaternion.Euler(atk.AimPitch, aimYaw, 0f) * Vector3.forward;
            var direction = new SharedVec3(dir.x, dir.y, dir.z);

            float closestDist = range;
            int victimId = -1;
            SharedVec3 hitPoint = SharedVec3.Zero;

            foreach (var (targetId, targetSlot) in _players)
            {
                if (targetId == attackerId) continue;
                var targetSnapshot = _ecsWorld.GetSnapshot(targetId, _currentTick);
                if (targetSnapshot.Health <= 0) continue;

                var targetPos = targetSnapshot.Position;
                var capsule = new Capsule(targetPos, GameConstants.PlayerHeight, GameConstants.HitCapsuleRadius);
                var aabb = capsule.BoundingBox();
                var ray = new ShootingGame.Shared.Physics.Ray(origin, direction);


                var hit = Intersection.RayAABB(ray, aabb, closestDist);
                if (hit.Hit && hit.Distance < closestDist)
                {
                    closestDist = hit.Distance;
                    victimId = targetId;
                    hitPoint = hit.Point;
                }
            }


            // 应用伤害（枪械驱动 + 距离衰减）
            if (victimId < 0 || !_players.ContainsKey(victimId))
                return;

            int dmg = gun != null ? (int)(gun.Damage * gun.GetFalloffMultiplier(closestDist) + 0.5f) : baseDamage;
            byte damage = (byte)Mathf.Clamp(dmg, 1, 255);
            var victimSnapshot = _ecsWorld.GetSnapshot(victimId, _currentTick);
            byte newHp = (byte)Mathf.Max(0, victimSnapshot.Health - damage);
            _ecsWorld.TrySetHealth(victimId, newHp);

            bool isKill = victimSnapshot.Health > 0 && newHp == 0;
            if (isKill)
            {
                var victimSlot = _players[victimId];
                victimSlot.Deaths++;
                attackerSlot.Kills++;
                victimSlot.RespawnTick = HasRemainingLives(victimSlot.Deaths, _livesPerPlayer)
                    ? _currentTick + Mathf.CeilToInt(GameConstants.RespawnDelay * _tickRate)
                    : 0;
            }


            hitEvents.Add(new HitEventMsg
            {
                AttackId = atk.AttackId,
                AttackerId = attackerId,
                VictimId = victimId,
                Damage = damage,
                IsKill = isKill,
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

            int selectedHeroId = ShootingGame.Shared.Hero.HeroRegistry.DefaultHeroId;
            BattlePlayerInfo selection = null;
            lock (_selectionLock)
            {
                if (_playerSelections.TryGetValue(battlePlayerId, out selection) && selection.HeroId > 0)
                    selectedHeroId = selection.HeroId;
            }
            var hero = ShootingGame.Shared.Hero.HeroRegistry.GetHero(selectedHeroId)
                ?? ShootingGame.Shared.Hero.HeroRegistry.GetHero(ShootingGame.Shared.Hero.HeroRegistry.DefaultHeroId);
            slot.HeroCfg = hero;
            slot.Gun = hero?.Gun ?? ShootingGame.Shared.Hero.GunRegistry.GetGun(null);

            var spawnPos = selection != null
                ? selection.SpawnPosition
                : GetSpawnPos(battlePlayerId);
            slot.LastSpawnIndex = FindSpawnIndex(spawnPos);
            var snapshot = CreateSpawnSnapshot(slot, spawnPos);
            slot.IsReady = true;
            _ecsWorld.RegisterPlayer(battlePlayerId, snapshot);
            _ecsWorld.ConfigurePlayer(battlePlayerId, hero, slot.Gun);

            // 回复 BattleStart
            var start = new MainPack
            {
                RequestCode = RequestCode.Battle, ActionCode = ActionCode.BattleStart,
                ReturnCode = ReturnCode.Success
            };
            _transport.Send(clientId, ProtobufSerializer.SerializeMainPack(start));
            Debug.Log($"[HostBattleServer] Player {battlePlayerId} (UDP:{clientId}) is ready, spawn=({spawnPos.x:F1},{spawnPos.z:F1})");
        }

        private static PlayerSnapshot CreateSpawnSnapshot(PlayerSlot slot, SharedVec3 spawnPos)
        {
            var snapshot = PlayerSnapshot.Default(spawnPos);
            if (slot.Gun != null)
            {
                snapshot.MaxAmmo = slot.Gun.ClipSize;
                snapshot.CurrentAmmo = slot.Gun.ClipSize;
                snapshot.ReloadDuration = slot.Gun.ReloadTime;
                snapshot.FireInterval = slot.Gun.FireRate;
            }
            snapshot.Health = slot.HeroCfg?.MaxHP ?? GameConstants.MaxHealth;
            return snapshot;
        }

        private void HandleBattleOperation(int clientId, MainPack pack)
        {
            var op = pack.BattleInfo?.SelfOperation;
            if (op == null) return;

            int playerId = _udpToPlayerId.TryGetValue(clientId, out var pid) ? pid : clientId;

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
                if (slot.LatestOp?.AttackOperations != null && slot.LatestOp.AttackOperations.Count > 0)
                    op.AttackOperations.InsertRange(0, slot.LatestOp.AttackOperations);
                slot.LatestOp = op;
            }
        }

        private void ProcessAbilityEvents(int playerId, PlayerSlot slot)
        {
            var events = slot.LatestOp?.AbilityEvents;
            if (events == null || events.Count == 0)
                return;

            foreach (var evt in events)
            {
                if (evt.EventType != AbilityEventType.RequestActivate)
                    continue;

                ushort serverInstanceId = _ecsWorld.TryActivateAbility(playerId, evt.AssetId);
                _pendingAbilityConfirmations.Add(new AbilityEventData
                {
                    PlayerId = (byte)playerId,
                    InstanceId = evt.InstanceId,
                    AssetId = evt.AssetId,
                    EventType = serverInstanceId > 0
                        ? AbilityEventType.ConfirmActivate
                        : AbilityEventType.RejectActivate
                });
            }

            events.Clear();
        }

        private SharedVec3 GetSpawnPos(int playerId)
        {
            var world = CollisionWorldLoader.Instance;
            int selectedIndex = -1;
            float bestSafety = float.NegativeInfinity;
            int previousIndex = _players.TryGetValue(playerId, out var playerSlot)
                ? playerSlot.LastSpawnIndex
                : -1;

            lock (_selectionLock)
            {
                for (int i = 0; i < _spawnPoints.Count; i++)
                {
                    if (_spawnPoints.Count > 1 && i == previousIndex)
                        continue;

                    var candidate = new Vector3(
                        _spawnPoints[i].Position.x,
                        _spawnPoints[i].Position.y,
                        _spawnPoints[i].Position.z);
                    if (!_sceneSpawnPointsAuthoritative
                        && !SpawnValidator.IsSpawnValid(candidate, world))
                        continue;

                    float minimumEnemyDistance = float.PositiveInfinity;
                    foreach (var (otherId, otherSlot) in _players)
                    {
                        if (otherId == playerId || !otherSlot.IsReady
                            || !HasRemainingLives(otherSlot.Deaths, _livesPerPlayer))
                            continue;
                        var other = _ecsWorld.GetSnapshot(otherId, _currentTick).Position;
                        float dx = candidate.x - other.x;
                        float dy = candidate.y - other.y;
                        float dz = candidate.z - other.z;
                        float distanceSquared = dx * dx + dy * dy + dz * dz;
                        if (distanceSquared < minimumEnemyDistance)
                            minimumEnemyDistance = distanceSquared;
                    }

                    if (minimumEnemyDistance > bestSafety)
                    {
                        bestSafety = minimumEnemyDistance;
                        selectedIndex = i;
                    }
                }

                if (selectedIndex >= 0)
                {
                    var selected = _spawnPoints[selectedIndex].Position;
                    if (playerSlot != null) playerSlot.LastSpawnIndex = selectedIndex;
                    Debug.Log($"[HostSpawn] Player {playerId} -> ({selected.x:F1},{selected.z:F1}) index={selectedIndex}");
                    return selected;
                }

                if (_spawnPoints.Count > 0)
                {
                    var first = _spawnPoints[0].Position;
                    if (_sceneSpawnPointsAuthoritative)
                    {
                        if (playerSlot != null) playerSlot.LastSpawnIndex = 0;
                        return first;
                    }
                    var fallback = new Vector3(first.x, first.y, first.z);
                    var found = SpawnValidator.FindNearestValidSpawn(fallback, world);
                    if (playerSlot != null) playerSlot.LastSpawnIndex = 0;
                    return new SharedVec3(found.x, found.y, found.z);
                }
            }

            Debug.LogWarning("[HostSpawn] No configured spawn point; using fallback position.");

            // Fallback：SpawnPoints.json 不存在时使用默认位置
            float fx = (playerId - 1) * 12f - 6f;
            float fz = (playerId % 2 == 0) ? 6f : -6f;
            return new SharedVec3(fx, 0.1f, fz);
        }

        private int FindSpawnIndex(SharedVec3 position)
        {
            lock (_selectionLock)
            {
                for (int i = 0; i < _spawnPoints.Count; i++)
                {
                    if (SharedVec3.SqrDistance(_spawnPoints[i].Position, position) < 0.01f)
                        return i;
                }
            }
            return -1;
        }
    }
}
