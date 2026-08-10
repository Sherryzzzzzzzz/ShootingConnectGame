using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using ShootingGame.Network;
using ShootingGame.Shared.Math;
using ShootingGame.Shared.Protocol;
using ShootingGame.Shared.Simulation;
using ShootingGame.Shared.Physics;
using ShootingGame.Shared.ECS;
using ShootingGame.Shared.Ability;
using ShootingGame.Shared.Hero;
using ShootingGame.Shared.GameplayTags;
using ShootingGame.Server.ECS;

namespace ShootingGame.Server
{
    /// <summary>
    /// BattleRoom manages a single battle instance.
    /// Handles BattleReady -> BattleStart handshake, frame loop, and battle lifecycle.
    /// </summary>
    public class BattleRoom
    {
        public BattleContext Context { get; }
        private readonly MatchMaker _matchMaker;
        private readonly object _lock = new object();

        // Battle state
        private int _frameId;
        private volatile bool _isRunning;
        private volatile bool _hasStarted;
        private volatile bool _hasEnded;

        // Player readiness
        private readonly Dictionary<int, bool> _playerReady = new Dictionary<int, bool>();
        private readonly Dictionary<int, string> _playerEndpoints = new Dictionary<int, string>();
        private readonly HashSet<int> _disconnectedPlayers = new HashSet<int>();

        // Player state
        private readonly Dictionary<int, PlayerSnapshot> _playerSnapshots = new Dictionary<int, PlayerSnapshot>();
        private readonly Dictionary<int, int> _playerTeams = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _playerHp = new Dictionary<int, int>();
        private readonly Dictionary<int, bool> _playerIsDead = new Dictionary<int, bool>();
        private readonly Dictionary<int, HeroConfig> _playerHeroConfigs = new Dictionary<int, HeroConfig>();
        private readonly Dictionary<int, GunConfigData> _playerGuns = new Dictionary<int, GunConfigData>();
        private readonly Dictionary<int, int> _lastFireFrame = new Dictionary<int, int>();
        private readonly Dictionary<int, float> _bloomHeat = new Dictionary<int, float>(); // 连发扩散热度(度)
        private readonly Dictionary<int, bool> _playerIsAiming = new Dictionary<int, bool>();
        private readonly Dictionary<int, bool> _playerIsCrouching = new Dictionary<int, bool>();

        // Input buffers (sliding window)
        private readonly Dictionary<int, InputBuffer> _inputBuffers = new Dictionary<int, InputBuffer>();
        private readonly Dictionary<int, int> _lastConsumedFrame = new Dictionary<int, int>();
        private readonly Dictionary<int, bool> _prevJumpHeld = new Dictionary<int, bool>();
        // 跳跃边沿事件队列（HandlePlayerOperation 检测，ProcessFrame 应用，避免输入消费时序丢失）
        private readonly HashSet<int> _pendingJumps = new HashSet<int>();

        // Attack retransmission
        private readonly Dictionary<int, int> _lastProcessedAttackId = new Dictionary<int, int>();
        private readonly Dictionary<int, List<AttackOperation>> _pendingAttacks = new Dictionary<int, List<AttackOperation>>();

        // Bullet system
        private readonly List<ServerBullet> _activeBullets = new List<ServerBullet>();
        private readonly List<HitEventMsg> _pendingHitEvents = new List<HitEventMsg>();

        // Position history for lag compensation
        private readonly Dictionary<int, Dictionary<int, Vec3>> _positionHistory = new Dictionary<int, Dictionary<int, Vec3>>();
        private readonly List<int> _positionHistoryFrames = new List<int>();
        private const int PositionHistorySize = 30;

        // Frame history for retransmission
        private readonly Dictionary<int, AllPlayerOperation> _frameHistory = new Dictionary<int, AllPlayerOperation>();
        private readonly Dictionary<int, int> _playerAckedFrame = new Dictionary<int, int>();
        private const int MaxFrameHistory = 64;

        // Game state
        private int _killerTeamId;
        private int _killerPlayerId;

        // 死斗(FFA)模式状态
        private bool IsDeathmatch => Context.Mode == 1;
        private readonly Dictionary<int, int> _kills = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _deaths = new Dictionary<int, int>();
        private float _matchElapsed;
        private readonly Random _spawnRng = new Random();

        // Respawn system
        private readonly Dictionary<int, float> _respawnTimers = new Dictionary<int, float>();
        private readonly Dictionary<int, Vec3> _spawnPositions = new Dictionary<int, Vec3>();  // 记录出生位置，用于卡点检测
        private List<SpawnPoint> _team1SpawnPoints;
        private List<SpawnPoint> _team2SpawnPoints;
        private List<SpawnPoint> _anySpawnPoints;

        // GameOver reliable retransmission
        private int _gameOverRetransmitRemaining;
        private const int GameOverRetransmitTicks = 60; // ~1 second at 60fps

        // RTT provider from UDP server
        private Func<int, float> _getPlayerRtt; // returns RTT in seconds

        // Pending ability confirmations (generated by server, sent back to client)
        private readonly List<AbilityEventData> _pendingAbilityConfirmations = new List<AbilityEventData>();

        // Input receive rate logging

        // Collision world
        private CollisionWorld _collisionWorld;

        // ECS world
        private ServerECSWorld _ecsWorld;

        // Frame timing
        private readonly float _frameInterval = GameConstants.TickDelta;
        private const int MaxCatchupFrames = 5;

        // Default fallback spawn positions (when no spawn points are configured)
        private static readonly Vec3[] DefaultSpawnPositions = new Vec3[]
        {
            new Vec3(0, 0, 0),
            new Vec3(5, 0, 5),
            new Vec3(-5, 0, 5),
            new Vec3(0, 0, -5),
            new Vec3(5, 0, -5),
            new Vec3(-5, 0, -5),
            new Vec3(10, 0, 0),
            new Vec3(-10, 0, 0),
            new Vec3(0, 0, 10),
            new Vec3(0, 0, -10)
        };

        public int BattleId => Context.BattleId;
        public bool IsStarted => _hasStarted;

        /// <summary>强制开始（跳过 BattleReady 握手，用于英雄确认后直接开始）</summary>
        public void ForceStart()
        {
            if (!_hasStarted)
            {
                _hasStarted = true;
                // 用 bpId（与 HandleBattleReady 一致），避免 _playerReady key 混乱
                foreach (var player in Context.Players)
                {
                    int bpId = Context.GetBattlePlayerId(player.UserId);
                    _playerReady[bpId] = true;
                }
                // 完整启动：广播 BattleStart + 启动帧循环
                BroadcastBattleStart();
                StartFrameLoop();
                Console.WriteLine($"[BattleRoom] ForceStart: battle {BattleId} started, frame loop launched");
            }
        }

        /// <summary>
        /// Set the RTT provider callback so the room can use per-player RTT.
        /// </summary>
        public void SetRttProvider(Func<int, float> getRtt)
        {
            _getPlayerRtt = getRtt;
        }

        public BattleRoom(BattleContext context, MatchMaker matchMaker)
        {
            Context = context;
            _matchMaker = matchMaker;

            RegisterAbilityRpcHandlers();
            InitializeBattle();
        }

        /// <summary>
        /// 技能预测确认链（RPC 版）：客户端预测施法 → [ServerRpc] RequestActivateAbility
        /// → 服务器验证 + 激活 AbilityLifecycleSystem → 回程 Confirm/Reject（[ClientRpc] 语义）。
        /// 特效由客户端程序化生成，Confirm 保留 / Reject 回滚。
        /// </summary>
        private void RegisterAbilityRpcHandlers()
        {
            RegisterRpcHandler("global::PlayerCombatBehaviour", "RequestActivateAbility",
                new[] { "System.Int32", "System.Int32" }, (bpId, r) =>
            {
                int assetId = r.ReadInt32();
                int predictedId = r.ReadInt32();
                if (!_hasStarted || _hasEnded)
                    return;
                if (_disconnectedPlayers.Contains(bpId))
                    return;

                // 服务器权威验证 + 激活（复用现有 AbilityLifecycleSystem）
                ushort instanceId = _ecsWorld.TryActivateAbility(bpId, (byte)assetId);
                if (instanceId > 0)
                {
                    // ConfirmAbility(predictedId, instanceId)：客户端用 predictedId 匹配预测特效
                    SendClientRpc(bpId,
                        RpcMethodHash.Compute("global::PlayerCombatBehaviour.ConfirmAbility(System.Int32,System.Int32)"),
                        predictedId, instanceId);
                    Console.WriteLine($"[RPC] Skill: bp{bpId} assetId={assetId} pred={predictedId} -> Confirm instance={instanceId}");
                }
                else
                {
                    // RejectAbility(predictedId)：客户端回滚预测特效
                    SendClientRpc(bpId,
                        RpcMethodHash.Compute("global::PlayerCombatBehaviour.RejectAbility(System.Int32)"),
                        predictedId);
                    Console.WriteLine($"[RPC] Skill: bp{bpId} assetId={assetId} pred={predictedId} -> Reject");
                }
            });
        }

        /// <summary>服务器 → 客户端 RPC 回程（payload = NetId + MethodHash + reqId + 参数，与生成代理格式一致）</summary>
        private void SendClientRpc(int bpId, long methodHash, params int[] args)
        {
            var w = new PacketWriter();
            w.WriteUInt32((uint)bpId);   // NetId 低 16 位 = bpId
            w.WriteInt64(methodHash);
            w.WriteUInt32(0);            // reqId = 0（Fire-and-Forget）
            foreach (var a in args) w.WriteInt32(a);
            OnSendClientRpc?.Invoke(bpId, w.ToArray());
        }

        private void InitializeBattle()
        {
            lock (_lock)
            {
                // Load collision world
                _collisionWorld = new CollisionWorld();
                if (!string.IsNullOrEmpty(Context.CollisionDataPath) && System.IO.File.Exists(Context.CollisionDataPath))
                {
                    _collisionWorld = CollisionWorld.Load(Context.CollisionDataPath);
                    Console.WriteLine($"[BattleRoom] Loaded collision from {Context.CollisionDataPath}: {_collisionWorld.Count} boxes");
                }
                else
                {
                    _collisionWorld.AddBox(new AABB(new Vec3(-50, -1, -50), new Vec3(50, 0, 50)));
                    Console.WriteLine("[BattleRoom] No collision file. Using default floor.");
                }
                // Build and cache spawn point lookup
                _team1SpawnPoints = new List<SpawnPoint>();
                _team2SpawnPoints = new List<SpawnPoint>();
                _anySpawnPoints = new List<SpawnPoint>();
                if (Context.SpawnPoints != null && Context.SpawnPoints.Count > 0)
                {
                    foreach (var sp in Context.SpawnPoints)
                    {
                        if (sp.TeamId == 1)
                            _team1SpawnPoints.Add(sp);
                        else if (sp.TeamId == 2)
                            _team2SpawnPoints.Add(sp);
                        else
                            _anySpawnPoints.Add(sp);
                    }
                }

                // Initialize player state
                foreach (var player in Context.Players)
                {
                    int bpId = Context.GetBattlePlayerId(player.UserId);

                    // Look up hero config
                    var heroConfig = HeroRegistry.GetHero(player.HeroId);
                    if (heroConfig == null) heroConfig = HeroRegistry.GetHero(HeroRegistry.DefaultHeroId);
                    _playerHeroConfigs[bpId] = heroConfig;
                    _playerGuns[bpId] = heroConfig.Gun ?? GunRegistry.GetGun(heroConfig.StartingGunId);
                    _kills[bpId] = 0;
                    _deaths[bpId] = 0;
                    _bloomHeat[bpId] = 0f;

                    _playerReady[bpId] = false;
                    _playerTeams[bpId] = player.TeamId;
                    _playerHp[bpId] = heroConfig.MaxHP;
                    _playerIsDead[bpId] = false;

                    // 出生点随机选择（每次出生不同位置）
                    Vec3 spawnPos;
                    if (!IsDeathmatch && player.TeamId == 1 && _team1SpawnPoints.Count > 0)
                    {
                        int idx = _spawnRng.Next(_team1SpawnPoints.Count);
                        spawnPos = _team1SpawnPoints[idx].Position;
                    }
                    else if (!IsDeathmatch && player.TeamId == 2 && _team2SpawnPoints.Count > 0)
                    {
                        int idx = _spawnRng.Next(_team2SpawnPoints.Count);
                        spawnPos = _team2SpawnPoints[idx].Position;
                    }
                    else if (_anySpawnPoints.Count > 0)
                    {
                        int idx = _spawnRng.Next(_anySpawnPoints.Count);
                        spawnPos = _anySpawnPoints[idx].Position;
                    }
                    else
                    {
                        int idx = _spawnRng.Next(DefaultSpawnPositions.Length);
                        spawnPos = DefaultSpawnPositions[idx];
                    }

                    _playerSnapshots[bpId] = PlayerSnapshot.Default(spawnPos);
                    var snap = _playerSnapshots[bpId];
                    snap.Health = heroConfig.MaxHP;
                    ApplyGunToSnapshot(ref snap, _playerGuns.GetValueOrDefault(bpId));
                    _playerSnapshots[bpId] = snap;
                    _spawnPositions[bpId] = spawnPos;

                    // Initialize input buffer
                    _inputBuffers[bpId] = new InputBuffer();
                    _lastConsumedFrame[bpId] = 0;
                    _playerAckedFrame[bpId] = 0;

                    // Initialize attack tracking
                    _lastProcessedAttackId[bpId] = 0;
                    _pendingAttacks[bpId] = new List<AttackOperation>();

                    // Initialize position history
                    _positionHistory[bpId] = new Dictionary<int, Vec3>();

                }

                // Initialize ECS world
                _ecsWorld = new ServerECSWorld();
                _ecsWorld.SetCollisionWorld(_collisionWorld);
                foreach (var kvp in _playerSnapshots)
                {
                    var heroCfg = _playerHeroConfigs.TryGetValue(kvp.Key, out var hc) ? hc : null;
                    _ecsWorld.RegisterPlayer(kvp.Key, kvp.Value, heroCfg);
                }

                Log($"Battle {BattleId} initialized with {Context.Players.Count} players");
            }
        }

        /// <summary>
        /// Handle BattleReady from a player.
        /// </summary>
        public void HandleBattleReady(int battlePlayerId, string endpoint)
        {
            lock (_lock)
            {
                if (_disconnectedPlayers.Contains(battlePlayerId))
                    return;

                _playerReady[battlePlayerId] = true;
                _playerEndpoints[battlePlayerId] = endpoint;

                Log($"Player {battlePlayerId} ready in battle {BattleId}");

                // 先给发 BattleReady 的客户端回 BattleStart（不等全员）
                SendBattleStart(battlePlayerId, endpoint);

                // 全员就绪后启动帧循环
                if (!_hasStarted && _playerReady.Values.Count >= Context.Players.Count)
                {
                    bool allReady = true;
                    foreach (var ready in _playerReady.Values)
                        allReady = allReady && ready;

                    if (allReady)
                    {
                        _hasStarted = true;
                        Log($"[BattleRoom] All {Context.Players.Count} players ready, starting frame loop");
                        BroadcastBattleStart();
                        StartFrameLoop();
                    }
                }
            }
        }

        /// <summary>
        /// Handle player input operation.
        /// </summary>
        /// <summary>
        /// RPC 入口（路径 X）：客户端 RpcCall 包经 BattleUdpServer 路由到此。
        /// 解析 NetId + MethodHash → 按 methodHash 分发到服务器侧处理器。
        /// </summary>
        public event Action<int, long> OnRpcCallReceived; // (battlePlayerId, methodHash) 供测试/日志观测
        public event Action<int, byte[]> OnSendClientRpc; // (battlePlayerId, payload) 服务器→客户端 RPC 回程
        private readonly Dictionary<long, Action<int, PacketReader>> _rpcHandlers = new Dictionary<long, Action<int, PacketReader>>();

        /// <summary>注册服务器侧 RPC 处理器（签名与客户端 [ServerRpc] 方法一致）</summary>
        public void RegisterRpcHandler(string fullTypeName, string methodName, string[] paramTypes, Action<int, PacketReader> handler)
        {
            string sig = ShootingGame.Network.RpcMethodHash.BuildSignature(fullTypeName, methodName, paramTypes);
            long hash = ShootingGame.Network.RpcMethodHash.Compute(sig);
            _rpcHandlers[hash] = handler;
            Console.WriteLine($"[RPC] Registered handler 0x{hash:X16} : {sig}");
        }

        public void HandleRpcCall(int battlePlayerId, byte[] payload)
        {
            lock (_lock)
            {
                if (!_hasStarted || _hasEnded)
                    return;
                if (_disconnectedPlayers.Contains(battlePlayerId))
                    return;

                if (payload.Length < 12) return; // NetId(4) + MethodHash(8) 最小头部
                try
                {
                    var reader = new PacketReader(payload);
                    uint netId = reader.ReadUInt32();
                    long methodHash = reader.ReadInt64();
                    reader.ReadUInt32(); // reqId（生成代理格式：NetId + MethodHash + reqId + args）
                    Console.WriteLine($"[RPC] bp{battlePlayerId} -> netId={netId} methodHash=0x{methodHash:X8} payload={payload.Length}B");
                    OnRpcCallReceived?.Invoke(battlePlayerId, methodHash);

                    if (_rpcHandlers.TryGetValue(methodHash, out var handler))
                    {
                        handler(battlePlayerId, reader);
                    }
                    else
                    {
                        Console.WriteLine($"[RPC] No handler for methodHash=0x{methodHash:X8} from bp{battlePlayerId}");
                    }                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[RPC] Parse error from bp{battlePlayerId}: {ex.Message}");
                }
            }
        }

        public void HandlePlayerOperation(int battlePlayerId, PlayerOperation operation, int clientFrameId, int clientAckedFrame)
        {
            lock (_lock)
            {
                if (!_hasStarted || _hasEnded)
                    return;

                if (_disconnectedPlayers.Contains(battlePlayerId))
                    return;

                if (_playerIsDead.GetValueOrDefault(battlePlayerId, false))
                    return;

                // Update acked frame
                if (_playerAckedFrame[battlePlayerId] < clientAckedFrame)
                {
                    _playerAckedFrame[battlePlayerId] = clientAckedFrame;
                }

                // Store movement input (will be consumed in frame loop)
                if (_inputBuffers.TryGetValue(battlePlayerId, out var inputBuffer))
                {
                    // 跳跃按键边缘检测：防止长按跳跃键在落地后立刻重新起跳
                    bool jumpEdge = operation.Jump;
                    if (_prevJumpHeld.TryGetValue(battlePlayerId, out bool wasHeld) && wasHeld && operation.Jump)
                    {
                        jumpEdge = false; // 持续按住，不触发新跳跃
                    }
                    _prevJumpHeld[battlePlayerId] = operation.Jump;

                    // 跳跃边沿 → 事件队列（ProcessFrame 应用，确保不丢）
                    if (jumpEdge)
                        _pendingJumps.Add(battlePlayerId);

                    var input = new InputFrame
                    {
                        Tick = clientFrameId,
                        Movement = new Vec2(operation.MoveX, operation.MoveY),
                        AimYaw = operation.AimYaw,
                        AimPitch = operation.AimPitch,
                        Fire = operation.Fire,
                        Jump = jumpEdge,
                        Run = operation.Run,
                        Aim = operation.Aim,
                        Reload = operation.Reload,
                        Crouch = operation.Crouch
                    };
                    inputBuffer.Store(input);

                }



                // Process attacks (deduplication)
                if (operation.AttackOperations != null && operation.AttackOperations.Count > 0)
                {
                    foreach (var atk in operation.AttackOperations)
                    {
                        // 去重：只接受比上次更新的 AttackId
                        if (atk.AttackId > _lastProcessedAttackId[battlePlayerId])
                        {
                            _pendingAttacks[battlePlayerId].Add(atk);
                            _lastProcessedAttackId[battlePlayerId] = atk.AttackId;
                        }
                        else
                        {
                        }
                    }
                }

                // Process ability events (server-authoritative)
                if (operation.AbilityEvents != null && operation.AbilityEvents.Count > 0)
                {
                    foreach (var evt in operation.AbilityEvents)
                    {
                        switch (evt.EventType)
                        {
                            case AbilityEventType.RequestActivate:
                                ushort instanceId = _ecsWorld.TryActivateAbility(battlePlayerId, evt.AssetId);
                                _pendingAbilityConfirmations.Add(new AbilityEventData
                                {
                                    PlayerId = (byte)battlePlayerId,
                                    InstanceId = instanceId,
                                    AssetId = evt.AssetId,
                                    EventType = instanceId > 0 ? AbilityEventType.ConfirmActivate : AbilityEventType.RejectActivate
                                });
                                break;
                            case AbilityEventType.Deactivate:
                                var entity = _ecsWorld.GetEntity(battlePlayerId);
                                if (entity.IsValid)
                                    AbilityLifecycleSystem.Deactivate(_ecsWorld.EntityManager, entity, evt.InstanceId);
                                break;
                            case AbilityEventType.Cancel:
                                entity = _ecsWorld.GetEntity(battlePlayerId);
                                if (entity.IsValid)
                                    AbilityLifecycleSystem.Cancel(_ecsWorld.EntityManager, entity, evt.InstanceId);
                                break;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Handle player disconnect.
        /// </summary>
        public void HandlePlayerDisconnect(int userId)
        {
            int bpId = Context.GetBattlePlayerId(userId);
            if (bpId < 0) return;

            lock (_lock)
            {
                // 玩家还没通过 UDP 连上战斗（BattleReady 未到）→ 不判负，等重连或清理
                if (!_playerEndpoints.ContainsKey(bpId))
                {
                    Log($"Player {bpId} (userId={userId}) disconnected BEFORE UDP battle join. Marking rejoin-pending.");
                    _playerReady.Remove(bpId);
                    return;
                }

                _disconnectedPlayers.Add(bpId);
                _playerReady.Remove(bpId);
                _playerIsDead[bpId] = true;

                Log($"Player {bpId} (userId={userId}) disconnected");

                // 检查是否所有玩家都已断开或死亡（只统计已通过 UDP 加入的玩家）
                if (!_hasEnded)
                {
                    bool allDown = true;
                    bool anyJoined = false;
                    foreach (var kvp in _playerSnapshots)
                    {
                        if (!_playerEndpoints.ContainsKey(kvp.Key))
                            continue; // 还没连上 UDP，不算
                        anyJoined = true;
                        if (!_disconnectedPlayers.Contains(kvp.Key) && !_playerIsDead.GetValueOrDefault(kvp.Key, false))
                        {
                            allDown = false;
                            break;
                        }
                    }
                    if (allDown && anyJoined)
                    {
                        Log($"All players down, ending battle {BattleId}");
                        EndBattle();
                    }
                }
            }
        }

        private void BroadcastBattleStart()
        {
            foreach (var kvp in _playerEndpoints)
            {
                SendBattleStart(kvp.Key, kvp.Value);
            }
        }

        private void SendBattleStart(int battlePlayerId, string endpoint)
        {
            // This will be sent via UDP by BattleUdpServer
            // For now, we'll use a callback mechanism
            OnSendBattleStart?.Invoke(battlePlayerId, endpoint);
        }

        public event Action<int, string> OnSendBattleStart;
        public event Action<string, MainPack> OnSendPacket;

        private void StartFrameLoop()
        {
            _frameId = 1;
            _isRunning = true;

            var thread = new Thread(FrameLoop)
            {
                IsBackground = true,
                Name = $"Battle_{BattleId}_FrameLoop"
            };
            thread.Start();

            Log($"Battle {BattleId} frame loop started");
        }

        private void FrameLoop()
        {
            var sw = Stopwatch.StartNew();
            long lastTick = sw.ElapsedMilliseconds;
            double accumulator = 0;

            while (_isRunning)
            {
                long now = sw.ElapsedMilliseconds;
                long dt = now - lastTick;
                lastTick = now;
                accumulator += dt / 1000.0;

                int stepCount = 0;
                while (accumulator >= _frameInterval && stepCount < MaxCatchupFrames)
                {
                    if (_gameOverRetransmitRemaining > 0)
                    {
                        // GameOver retransmission phase: re-send GameOver to all players
                        lock (_lock)
                        {
                            foreach (var kvp in _playerEndpoints)
                            {
                                SendGameOver(kvp.Value);
                            }
                            _gameOverRetransmitRemaining--;

                            if (_gameOverRetransmitRemaining <= 0)
                            {
                                _isRunning = false;
                                _frameHistory.Clear();
                            }
                        }
                    }
                    else
                    {
                        bool shouldEnd = false;
                        lock (_lock)
                        {
                            if (IsDeathmatch ? CheckDeathmatchEnd() : IsTeamEliminated())
                            {
                                shouldEnd = true;
                            }
                            else
                            {
                                try
                                {
                                    ProcessFrame();
                                    _frameId++;
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"[BattleRoom] ProcessFrame ERROR: {ex.Message}\n{ex.StackTrace}");
                                }
                            }
                        }

                        if (shouldEnd)
                        {
                            EndBattle();
                        }
                    }

                    accumulator -= _frameInterval;
                    stepCount++;
                }

                Thread.Sleep(1);
            }

            sw.Stop();
        }

        private void ProcessFrame()
        {
            var frameOp = new AllPlayerOperation
            {
                FrameId = _frameId
            };

            // 0. Process respawns
            ProcessRespawns();

            // 0.5. 连发扩散热度衰减
            DecayBloomHeat();

            // 1. Process inputs and update positions
            foreach (var kvp in _playerSnapshots)
            {
                int bpId = kvp.Key;
                if (_disconnectedPlayers.Contains(bpId) || _playerIsDead[bpId])
                    continue;

                var snapshot = kvp.Value;
                var inputBuffer = _inputBuffers[bpId];
                var input = inputBuffer.ConsumeNext();

                // 应用跳跃边沿事件（事件驱动，不依赖输入消费时序）
                if (_pendingJumps.Contains(bpId))
                {
                    input.Jump = true;
                    _pendingJumps.Remove(bpId);
                }

                _playerIsAiming[bpId] = input.Aim;
                _playerIsCrouching[bpId] = input.Crouch;

                // Update from input
                var op = new PlayerOperation
                {
                    PlayerId = bpId,
                    MoveX = input.Movement.x,
                    MoveY = input.Movement.y,
                    AimYaw = input.AimYaw,
                    AimPitch = input.AimPitch,
                    Fire = input.Fire,
                    Jump = input.Jump,
                    Run = input.Run,
                    Aim = input.Aim,
                    Reload = input.Reload
                };

                // === 服务器权威模拟（用共享 PlayerSystemGroup，双端一致）===
                var entity = _ecsWorld.GetEntity(bpId);
                if (entity.IsValid && _ecsWorld.EntityManager.IsValid(entity))
                {
                    _ecsWorld.TickPlayer(bpId, entity, input, _frameInterval);
                    snapshot = _ecsWorld.GetSnapshot(bpId, _frameId);
                }
                else
                {
                    snapshot = PlayerSimulation.Simulate(snapshot, input, _frameInterval, _collisionWorld);
                }
                _playerSnapshots[bpId] = snapshot;

                RecordPosition(bpId, snapshot.Position);

                // Merge pending attacks
                if (_pendingAttacks.TryGetValue(bpId, out var attacks) && attacks.Count > 0)
                {
                    op.AttackOperations.AddRange(attacks);
                }

                frameOp.Operations.Add(op);
            }

            // 2. Clear pending attacks (will be processed)
            foreach (var attacks in _pendingAttacks.Values)
            {
                attacks.Clear();
            }

            // 3. Spawn bullets from attacks
            SpawnBullets(frameOp);

            // 4. Tick bullets and collect hits
            TickBullets();

            // 5. Pack player states
            PackPlayerStates(frameOp);

            // 6. Add hit events
            frameOp.HitEvents.AddRange(_pendingHitEvents);
            _pendingHitEvents.Clear();

            // 6b. Add ability confirmations (server→client)
            if (_pendingAbilityConfirmations.Count > 0)
            {
                frameOp.AbilityEvents.AddRange(_pendingAbilityConfirmations);
                _pendingAbilityConfirmations.Clear();
            }

            // 7. Store frame history
            _frameHistory[_frameId] = frameOp;

            // Trim history
            while (_frameHistory.Count > MaxFrameHistory)
            {
                int oldest = _frameId - MaxFrameHistory;
                _frameHistory.Remove(oldest);
            }

            // 8. Broadcast frame
            BroadcastFrame(frameOp);
        }

        /// <summary>
        /// 伤害后同步到快照 + ECS HealthComponent，防止 GetSnapshot 每帧从 ECS 读回初始 HP。
        /// </summary>
        private void ApplyDamageToSnapshotAndECS(int targetId, int hp)
        {
            if (_playerSnapshots.TryGetValue(targetId, out var targetSnap))
            {
                targetSnap.Health = (byte)hp;
                _playerSnapshots[targetId] = targetSnap;
            }
            var e = _ecsWorld.GetEntity(targetId);
            if (e.IsValid && _ecsWorld.EntityManager.IsValid(e) &&
                _ecsWorld.EntityManager.TryGetComponent<HealthComponent>(e, out var hc))
            {
                hc.Current = (byte)hp;
                hc.Max = (byte)Math.Max(hc.Max, hp);
                _ecsWorld.EntityManager.SetComponent(e, hc);
            }
        }

        private void RecordPosition(int bpId, Vec3 pos)
        {
            if (!_positionHistory.ContainsKey(bpId))
                _positionHistory[bpId] = new Dictionary<int, Vec3>();

            _positionHistory[bpId][_frameId] = pos;

            // Clean old history
            while (_positionHistoryFrames.Count > PositionHistorySize)
            {
                int oldest = _positionHistoryFrames[0];
                _positionHistoryFrames.RemoveAt(0);
                foreach (var history in _positionHistory.Values)
                {
                    history.Remove(oldest);
                }
            }

            if (!_positionHistoryFrames.Contains(_frameId))
                _positionHistoryFrames.Add(_frameId);
        }

        private Vec3 GetHistoricalPosition(int bpId, int frameId)
        {
            if (_positionHistory.TryGetValue(bpId, out var history) &&
                history.TryGetValue(frameId, out var pos))
            {
                return pos;
            }
            return _playerSnapshots.TryGetValue(bpId, out var snap) ? snap.Position : Vec3.Zero;
        }

        /// <summary>把枪械参数注入玩家快照（弹药/换弹/射速）。</summary>
        private static void ApplyGunToSnapshot(ref PlayerSnapshot snap, GunConfigData gun)
        {
            if (gun == null) return;
            snap.MaxAmmo = gun.ClipSize;
            snap.CurrentAmmo = gun.ClipSize;
            snap.ReloadDuration = gun.ReloadTime;
            snap.FireInterval = gun.FireRate;
        }

        private GunConfigData GetPlayerGun(int bpId)
        {
            return _playerGuns.TryGetValue(bpId, out var gun) ? gun : GunRegistry.GetGun(null);
        }

        /// <summary>连发扩散热度随时间恢复</summary>
        private void DecayBloomHeat()
        {
            foreach (var kvp in _playerGuns)
            {
                var gun = kvp.Value;
                if (gun == null || gun.BloomRecover <= 0f) continue;
                float heat = _bloomHeat.GetValueOrDefault(kvp.Key);
                if (heat > 0f)
                    _bloomHeat[kvp.Key] = Math.Max(0f, heat - gun.BloomRecover * _frameInterval);
            }
        }

        private void SpawnBullets(AllPlayerOperation frameOp)
        {
            foreach (var op in frameOp.Operations)
            {
                if (op.AttackOperations == null || op.AttackOperations.Count == 0)
                    continue;

                int bpId = op.PlayerId;
                if (!_playerSnapshots.TryGetValue(bpId, out var snapshot))
                    continue;

                // 服务端权威弹药检查：换弹中或弹药不足时忽略攻击
                if (snapshot.IsReloading || snapshot.CurrentAmmo <= 0)
                    continue;

                int teamId = _playerTeams.GetValueOrDefault(bpId, 0);
                var gun = GetPlayerGun(bpId);
                float bulletSpeed = gun != null ? gun.BulletSpeed : 100f;
                float maxRange = gun != null ? gun.Range : 200f;

                foreach (var atk in op.AttackOperations)
                {
                    int originalClientFrame = atk.ClientFrameId; // 保留原始客户端帧号用于射速检查

                    int clientFrame = originalClientFrame;
                    if (clientFrame > _frameId)
                        clientFrame = _frameId;

                    // 服务端射速校验（用原始客户端帧号，不受 _frameId 截断影响；
                    // 否则服务器 tick 慢于客户端时两次攻击被截成同一帧，差=0→永远拒绝）
                    if (gun != null && _lastFireFrame.TryGetValue(bpId, out int lastFire))
                    {
                        float interval = (originalClientFrame - lastFire) * _frameInterval;
                        if (interval < gun.FireRate * 0.5f)
                        {
                            Log($"[AntiCheat] bp{bpId} 射速过快: {interval:F3}s < {gun.FireRate * 0.5f:F3}s, 丢弃攻击");
                            continue;
                        }
                    }
                    _lastFireFrame[bpId] = originalClientFrame;

                    // 优先使用客户端发送的枪口位置（非零表示客户端已设置）
                    Vec3 spawnPos;
                    if (atk.SpawnPos.x != 0f || atk.SpawnPos.y != 0f || atk.SpawnPos.z != 0f)
                    {
                        spawnPos = atk.SpawnPos;
                    }
                    else
                    {
                        // 回退：从历史位置计算
                        spawnPos = GetHistoricalPosition(bpId, clientFrame);
                        spawnPos = spawnPos + new Vec3(0, GameConstants.PlayerHeight * 0.85f, 0);
                    }

                    // Write back spawn position for client visual bullets
                    atk.SpawnPos = spawnPos;

                    // Direction from AttackOperation's stored values (correct for the frame it was fired)
                    float aimYaw = MathF.Atan2(atk.TowardX, atk.TowardY) * (180f / MathF.PI);
                    var aimRot = Quat.Euler(atk.AimPitch, aimYaw, 0);
                    Vec3 dir = aimRot * Vec3.Forward;
                    dir = dir.Normalized;

                    // 扩散：基础散射 + 移动惩罚 + 连发 bloom（双端同种子，客户端视觉弹道一致）
                    if (gun != null)
                    {
                        bool isMoving = new Vec3(snapshot.Velocity.x, 0f, snapshot.Velocity.z).SqrMagnitude > 1f;
                        float heat = _bloomHeat.GetValueOrDefault(bpId);
                        float spreadDeg = SpreadUtility.ComputeTotalSpread(gun, isMoving, heat);
                        dir = SpreadUtility.ApplyConeSpread(dir, spreadDeg, SpreadUtility.MakeSeed(atk.AttackId, bpId));
                        _bloomHeat[bpId] = Math.Min(heat + gun.BloomPerShot, gun.BloomMax > 0f ? gun.BloomMax : heat + gun.BloomPerShot);
                    }

                    // Lag compensation: frame-by-frame catchup with collision detection
                    int catchupFrames = _frameId - clientFrame;
                    if (_getPlayerRtt != null && catchupFrames > 0)
                    {
                        float rtt = _getPlayerRtt(bpId);
                        int maxCatchupFromRtt = (int)(rtt / _frameInterval) + 2;
                        catchupFrames = Math.Min(catchupFrames, maxCatchupFromRtt);
                    }

                    Vec3 currentPos = spawnPos;
                    float traveledDistance = 0f;
                    bool hitDuringCatchup = false;

                    // Frame-by-frame simulation with swept collision to prevent tunneling
                    int framesSimulated = 0;
                    while (framesSimulated <= catchupFrames)
                    {
                        Vec3 prevPos = currentPos;
                        float stepDist = bulletSpeed * _frameInterval;
                        currentPos = currentPos + dir * stepDist;
                        traveledDistance += stepDist;
                        framesSimulated++;

                        // Check max distance
                        if (traveledDistance >= maxRange)
                            break;

                        // Check world collision during catchup (bullet vs obstacles)
                        var worldHit = _collisionWorld.SweepSphere(prevPos, GameConstants.BulletRadius, dir, stepDist);
                        if (worldHit.Hit)
                            break;

                        // Check swept collision during catchup
                        foreach (var targetKvp in _playerSnapshots)
                        {
                            int targetId = targetKvp.Key;
                            if (targetId == bpId) continue;
                            if (!IsDeathmatch && _playerTeams.TryGetValue(targetId, out int targetTeam) && targetTeam == teamId) continue;
                            if (_playerIsDead.GetValueOrDefault(targetId, false)) continue;
                            if (_disconnectedPlayers.Contains(targetId)) continue;

                            // Use the target's position at the corresponding catchup frame
                            Vec3 targetPos;
                            if (framesSimulated < catchupFrames)
                            {
                                int historyFrame = clientFrame + framesSimulated;
                                targetPos = GetHistoricalPosition(targetId, historyFrame);
                            }
                            else
                            {
                                targetPos = targetKvp.Value.Position;
                            }

                            // 商用级扫描碰撞：子弹路径(prevPos→currentPos) vs 命中胶囊体
                            var hitCapsule = new Capsule(targetPos - new Vec3(0, GameConstants.FootCapsuleOffset, 0), GameConstants.PlayerHeight + GameConstants.FootCapsuleOffset, GameConstants.HitCapsuleRadius);
                            if (Intersection.SweepBulletHitCapsule(prevPos, currentPos, hitCapsule, GameConstants.BulletRadius))
                            {
                                // Hit during catchup! Calculate body-part damage (枪械伤害 × 距离衰减 × 部位倍率)
                                int baseDamage = gun != null
                                    ? (int)(gun.Damage * gun.GetFalloffMultiplier(traveledDistance) + 0.5f)
                                    : GameConstants.HitscanDamage;
                                int damage = CalculateBodyPartDamage(baseDamage, currentPos, targetPos, out int bodyPart);
                                damage = ApplyTagDamageModifiers(damage, bpId, targetId);
                                int hp = _playerHp[targetId] - damage;
                                hp = Math.Max(0, hp);
                                _playerHp[targetId] = hp;
                                // 同步到快照 + ECS（否则 GetSnapshot 每帧从 ECS 读回初始值，HP 反复回满）
                                ApplyDamageToSnapshotAndECS(targetId, hp);

                                bool isKill = hp <= 0;
                                if (isKill)
                                {
                                    _playerIsDead[targetId] = true;
                                    _respawnTimers[targetId] = GameConstants.RespawnDelay;
                                    _killerPlayerId = bpId;
                                    _killerTeamId = teamId;
                                    _kills[bpId] = _kills.GetValueOrDefault(bpId) + 1;
                                    _deaths[targetId] = _deaths.GetValueOrDefault(targetId) + 1;
                                    Log($"Player {targetId} killed by {bpId} (K:{_kills[bpId]}). Respawning in {GameConstants.RespawnDelay}s");
                                }

                                _pendingHitEvents.Add(new HitEventMsg
                                {
                                    AttackId = atk.AttackId,
                                    AttackerId = bpId,
                                    VictimId = targetId,
                                    Damage = damage,
                                    IsKill = isKill,
                                    HitPoint = currentPos,
                                    HitFrameId = _frameId,
                                    BodyPart = bodyPart
                                });

                                Log($"[Hit-Catchup] bp{bpId} hit bp{targetId} for {damage} damage (kill={isKill}) frameDiff={catchupFrames}");
                                hitDuringCatchup = true;
                                break;
                            }
                        }

                        if (hitDuringCatchup)
                            break;
                    }

                    if (!hitDuringCatchup && traveledDistance < maxRange)
                    {
                        var bullet = new ServerBullet
                        {
                            AttackId = atk.AttackId,
                            OwnerId = bpId,
                            OwnerTeamId = teamId,
                            Position = currentPos,
                            Direction = dir,
                            Speed = bulletSpeed,
                            MaxDistance = maxRange,
                            Damage = gun != null ? gun.Damage : GameConstants.HitscanDamage,
                            Gun = gun,
                            SpawnFrameId = clientFrame,
                            TraveledDistance = traveledDistance
                        };

                        _activeBullets.Add(bullet);
                    }
                }
            }
        }

        private void TickBullets()
        {
            var toRemove = new List<ServerBullet>();

            foreach (var bullet in _activeBullets)
            {
                // 保存移动前位置，用于扫描碰撞检测
                Vec3 prevPos = bullet.Position;

                // Move
                bullet.Position = bullet.Position + bullet.Direction * (bullet.Speed * _frameInterval);
                bullet.TraveledDistance += bullet.Speed * _frameInterval;

                // Check max distance
                if (bullet.TraveledDistance >= bullet.MaxDistance)
                {
                    toRemove.Add(bullet);
                    continue;
                }

                // Check world collision (bullets cannot pass through obstacles)
                float moveDist = bullet.Speed * _frameInterval;
                var worldHit = _collisionWorld.SweepSphere(prevPos, GameConstants.BulletRadius, bullet.Direction, moveDist);
                if (worldHit.Hit)
                {
                    toRemove.Add(bullet);
                    continue;
                }

                // Check swept collision with players
                foreach (var kvp in _playerSnapshots)
                {
                    int targetId = kvp.Key;
                    var targetPos = kvp.Value.Position;

                    // Skip self and teammates (FFA 无队友，只跳过自己)
                    if (targetId == bullet.OwnerId)
                        continue;
                    if (!IsDeathmatch && _playerTeams.TryGetValue(targetId, out int targetTeam) &&
                        targetTeam == bullet.OwnerTeamId)
                        continue;

                    // Skip dead/disconnected
                    if (_playerIsDead.GetValueOrDefault(targetId, false))
                        continue;
                    if (_disconnectedPlayers.Contains(targetId))
                        continue;

                    // 商用级扫描碰撞：子弹路径(prevPos→newPos) vs 命中胶囊体
                    // 使用比物理碰撞更宽松的 HitCapsuleRadius，且扫描整段路径防止穿透
                    var hitCapsule = new Capsule(targetPos - new Vec3(0, GameConstants.FootCapsuleOffset, 0), GameConstants.PlayerHeight + GameConstants.FootCapsuleOffset, GameConstants.HitCapsuleRadius);
                    if (Intersection.SweepBulletHitCapsule(prevPos, bullet.Position, hitCapsule, GameConstants.BulletRadius))
                    {
                        // Hit! Calculate body-part damage (含距离衰减)
                        int bulletBaseDamage = bullet.Gun != null
                            ? (int)(bullet.Gun.Damage * bullet.Gun.GetFalloffMultiplier(bullet.TraveledDistance) + 0.5f)
                            : bullet.Damage;
                        int damage = CalculateBodyPartDamage(bulletBaseDamage, bullet.Position, targetPos, out int bodyPart);
                        damage = ApplyTagDamageModifiers(damage, bullet.OwnerId, targetId);
                        int hp = _playerHp[targetId] - damage;
                        hp = Math.Max(0, hp);
                        _playerHp[targetId] = hp;
                        // 同步到快照 + ECS
                        ApplyDamageToSnapshotAndECS(targetId, hp);

                        bool isKill = hp <= 0;
                        if (isKill)
                        {
                            _playerIsDead[targetId] = true;
                            _respawnTimers[targetId] = GameConstants.RespawnDelay;
                            _killerPlayerId = bullet.OwnerId;
                            _killerTeamId = bullet.OwnerTeamId;
                            _kills[bullet.OwnerId] = _kills.GetValueOrDefault(bullet.OwnerId) + 1;
                            _deaths[targetId] = _deaths.GetValueOrDefault(targetId) + 1;
                            Log($"Player {targetId} killed by {bullet.OwnerId} (K:{_kills[bullet.OwnerId]}). Respawning in {GameConstants.RespawnDelay}s");
                        }

                        var hitEvent = new HitEventMsg
                        {
                            AttackId = bullet.AttackId,
                            AttackerId = bullet.OwnerId,
                            VictimId = targetId,
                            Damage = damage,
                            IsKill = isKill,
                            HitPoint = bullet.Position,
                            HitFrameId = _frameId,
                            BodyPart = bodyPart
                        };

                        _pendingHitEvents.Add(hitEvent);
                        toRemove.Add(bullet);
                        Log($"[Hit] bp{bullet.OwnerId} hit bp{targetId} for {damage} damage (kill={isKill})");
                        break;
                    }
                }
            }

            foreach (var b in toRemove)
                _activeBullets.Remove(b);
        }

        private void ProcessRespawns()
        {
            var respawnedPlayers = new List<int>();
            foreach (var kvp in _respawnTimers)
            {
                int bpId = kvp.Key;
                if (!_playerIsDead.GetValueOrDefault(bpId, false))
                    continue;

                float remaining = kvp.Value - _frameInterval;
                if (remaining <= 0)
                {
                    RespawnPlayer(bpId);
                    respawnedPlayers.Add(bpId);
                }
                else
                {
                    _respawnTimers[bpId] = remaining;
                }
            }

            foreach (var bpId in respawnedPlayers)
                _respawnTimers.Remove(bpId);
        }

        private void RespawnPlayer(int bpId)
        {
            int teamId = _playerTeams.GetValueOrDefault(bpId, 0);
            Vec3 spawnPos = GetRespawnPosition(bpId, teamId);
            int maxHp = _playerHeroConfigs.TryGetValue(bpId, out var hc) ? hc.MaxHP : GameConstants.MaxHealth;

            _playerIsDead[bpId] = false;
            _playerHp[bpId] = maxHp;
            _playerSnapshots[bpId] = PlayerSnapshot.Default(spawnPos);

            // Re-register ECS entity
            var snap = _playerSnapshots[bpId];
            ApplyGunToSnapshot(ref snap, _playerGuns.GetValueOrDefault(bpId));
            _playerSnapshots[bpId] = snap;
            var heroConfig = _playerHeroConfigs.GetValueOrDefault(bpId);
            _ecsWorld.RegisterPlayer(bpId, snap, heroConfig);

            _spawnPositions[bpId] = spawnPos;
            Log($"Player {bpId} respawned at ({spawnPos.x:F1}, {spawnPos.y:F1}, {spawnPos.z:F1})");
        }

        private Vec3 GetRespawnPosition(int bpId, int teamId)
        {
            // FFA：从 any 池随机选点，避免固定重生点被蹲
            if (IsDeathmatch && _anySpawnPoints.Count > 0)
                return _anySpawnPoints[_spawnRng.Next(_anySpawnPoints.Count)].Position;

            var pool = teamId == 1 ? _team1SpawnPoints :
                       teamId == 2 ? _team2SpawnPoints :
                       _anySpawnPoints;

            if (pool == null || pool.Count == 0)
                pool = _anySpawnPoints;

            if (pool != null && pool.Count > 0)
            {
                var sp = pool[_spawnRng.Next(pool.Count)];
                return sp.Position;
            }

            return bpId < DefaultSpawnPositions.Length
                ? DefaultSpawnPositions[bpId]
                : new Vec3(bpId * 2, 0, 0);
        }

        /// <summary>
        /// 死斗结束判定：任一玩家达到击杀目标，或对局时间耗尽。
        /// 时间耗尽时击杀最多者胜（平手比死亡少、再比 bpId 小）。
        /// </summary>
        private bool CheckDeathmatchEnd()
        {
            _matchElapsed += _frameInterval;

            foreach (var kvp in _kills)
            {
                if (kvp.Value >= Context.KillTarget)
                {
                    _killerPlayerId = kvp.Key;
                    return true;
                }
            }

            if (_matchElapsed >= Context.TimeLimit)
            {
                int bestBp = -1, bestKills = -1, bestDeaths = int.MaxValue;
                foreach (var kvp in _kills)
                {
                    int d = _deaths.GetValueOrDefault(kvp.Key);
                    if (kvp.Value > bestKills ||
                        (kvp.Value == bestKills && d < bestDeaths) ||
                        (kvp.Value == bestKills && d == bestDeaths && kvp.Key < bestBp))
                    {
                        bestBp = kvp.Key;
                        bestKills = kvp.Value;
                        bestDeaths = d;
                    }
                }
                _killerPlayerId = bestBp;
                return true;
            }

            return false;
        }

        private bool IsTeamEliminated()
        {
            // 还有玩家未通过 UDP 加入（BattleReady 未到）且未断开 → 不判定胜负（可能在加载场景）
            foreach (var bpId in _playerTeams.Keys)
            {
                if (!_playerEndpoints.ContainsKey(bpId) && !_disconnectedPlayers.Contains(bpId))
                    return false;
            }

            bool team1Alive = false, team2Alive = false;
            foreach (var kvp in _playerTeams)
            {
                int bpId = kvp.Key;
                if (_disconnectedPlayers.Contains(bpId))
                    continue;
                if (_playerIsDead.GetValueOrDefault(bpId, false))
                    continue;

                if (kvp.Value == 1) team1Alive = true;
                else if (kvp.Value == 2) team2Alive = true;
            }

            if (!team1Alive && !team2Alive) return false; // no players at all
            if (!team1Alive) { _killerTeamId = 2; return true; }
            if (!team2Alive) { _killerTeamId = 1; return true; }
            return false;
        }

        private void PackPlayerStates(AllPlayerOperation frameOp)
        {
            foreach (var kvp in _playerSnapshots)
            {
                int bpId = kvp.Key;
                var snap = kvp.Value;

                float rotationY = snap.Rotation.EulerAngles.y;
                float speed = snap.Velocity.Magnitude;
                bool isRunning = speed > GameConstants.MoveSpeed + 0.1f;


                // 收集能力实例数据
                List<AbilityInstanceData> activeAbilities = null;
                if (snap.ActiveAbilities != null && snap.ActiveAbilityCount > 0)
                {
                    activeAbilities = new List<AbilityInstanceData>();
                    for (int i = 0; i < snap.ActiveAbilityCount; i++)
                    {
                        if (snap.ActiveAbilities[i].IsActive)
                            activeAbilities.Add(snap.ActiveAbilities[i]);
                    }
                }

                frameOp.PlayerStates.Add(new PlayerStateMsg
                {
                    PlayerId = bpId,
                    Position = snap.Position,
                    Hp = snap.Health,
                    IsDead = snap.Health <= 0,
                    Velocity = snap.Velocity,
                    VerticalVelocity = snap.VerticalVelocity,
                    IsGrounded = snap.IsGrounded,
                    StateEnum = (int)snap.State,
                    FireCooldown = snap.FireCooldown,
                    RotationY = rotationY,
                    IsRunning = isRunning,
                    IsAiming = _playerIsAiming.GetValueOrDefault(bpId),
                    IsCrouching = _playerIsCrouching.GetValueOrDefault(bpId),
                    CurrentAmmo = snap.CurrentAmmo,
                    IsReloading = snap.IsReloading,
                    TagBitmask = snap.TagBitmask,
                    ActiveAbilities = activeAbilities,
                    MaxHp = _playerHeroConfigs.TryGetValue(bpId, out var hc) ? hc.MaxHP : GameConstants.MaxHealth,
                    Kills = _kills.GetValueOrDefault(bpId),
                    Deaths = _deaths.GetValueOrDefault(bpId)
                });
            }
        }

        private void BroadcastFrame(AllPlayerOperation frameOp)
        {
            foreach (var kvp in _playerEndpoints)
            {
                int bpId = kvp.Key;
                string endpoint = kvp.Value;

                // 构建包
                var pack = new MainPack
                {
                    RequestCode = RequestCode.Battle,
                    ActionCode = ActionCode.BattleFrame,
                    BattleInfo = new BattleInfo
                    {
                        OperationId = _frameId,
                        BattleId = BattleId
                    }
                };

                // 只广播最新 1 帧（不做 ack 补发——客户端插值缓冲自行平滑丢帧，
                // 避免 ack 落后时一次广播几十帧淹没客户端主线程）
                if (_frameHistory.TryGetValue(_frameId, out var historyFrame))
                {
                    pack.BattleInfo.AllPlayerOperations.Add(historyFrame);
                }

                // Add hit events
                pack.BattleInfo.HitEvents.AddRange(frameOp.HitEvents);

                OnSendPacket?.Invoke(endpoint, pack);
            }
        }

        private void EndBattle()
        {
            if (_hasEnded) return;

            _hasEnded = true;
            _gameOverRetransmitRemaining = GameOverRetransmitTicks;

            Log($"Battle {BattleId} ended. Winner team: {_killerTeamId}");

            // Clean up gameplay state (no more simulation)
            _activeBullets.Clear();
            _positionHistory.Clear();

            // Notify match maker
            _matchMaker?.EndBattle(BattleId);
        }

        /// <summary>
        /// 发送 GameOver 包给指定玩家（可重复调用以确保可靠送达）。
        /// </summary>
        private void SendGameOver(string endpoint)
        {
            var pack = new MainPack
            {
                RequestCode = RequestCode.Battle,
                ActionCode = ActionCode.GameOver,
                // IntVal: 0=团队模式(Str=胜利队伍ID) 1=死斗(Str=胜者 bpId)
                IntVal = Context.Mode,
                Str = IsDeathmatch ? _killerPlayerId.ToString() : _killerTeamId.ToString()
            };

            // 记分板（按击杀降序，平手比死亡少）
            var entries = new List<ScoreEntryMsg>();
            foreach (var kvp in _kills)
            {
                var player = Context.Players.Find(pl => Context.GetBattlePlayerId(pl.UserId) == kvp.Key);
                entries.Add(new ScoreEntryMsg
                {
                    PlayerId = kvp.Key,
                    PlayerName = player != null ? player.Username : $"Player_{kvp.Key}",
                    Kills = kvp.Value,
                    Deaths = _deaths.GetValueOrDefault(kvp.Key)
                });
            }
            entries.Sort((a, b) => b.Kills != a.Kills ? b.Kills - a.Kills : a.Deaths - b.Deaths);
            pack.ScoreEntries.AddRange(entries);

            OnSendPacket?.Invoke(endpoint, pack);
        }

        public void Stop()
        {
            _isRunning = false;
        }

        /// <summary>
        /// Apply tag-based damage modifiers from victim and attacker buff tags.
        /// </summary>
        private int ApplyTagDamageModifiers(int damage, int attackerId, int victimId)
        {
            // Shield: victim damage resistance (-50%)
            var victimEntity = _ecsWorld.GetEntity(victimId);
            if (victimEntity.IsValid && _ecsWorld.EntityManager.HasComponent<TagComponent>(victimEntity))
            {
                var vTags = _ecsWorld.EntityManager.GetComponent<TagComponent>(victimEntity);
                if (GameplayTagConfig.Tag_Buff_DamageResist.Matches(vTags.TagBitMask))
                    damage /= 2;
            }

            // MarkShot: attacker damage boost (+100%, consumed on first hit)
            if (damage > 0)
            {
                var attackerEntity = _ecsWorld.GetEntity(attackerId);
                if (attackerEntity.IsValid && _ecsWorld.EntityManager.HasComponent<TagComponent>(attackerEntity))
                {
                    var aTags = _ecsWorld.EntityManager.GetComponent<TagComponent>(attackerEntity);
                    if (GameplayTagConfig.Tag_Buff_DamageBoost.Matches(aTags.TagBitMask))
                    {
                        damage *= 2;
                        // Consume the Buff.DamageBoost tag
                        aTags.TagBitMask &= ~GameplayTagConfig.Tag_Buff_DamageBoost.SelfMask;
                        _ecsWorld.EntityManager.SetComponent(attackerEntity, aTags);
                    }
                }
            }

            return Math.Max(1, damage);
        }

        private int CalculateBodyPartDamage(int baseDamage, Vec3 hitPoint, Vec3 playerBasePos)
        {
            return CalculateBodyPartDamage(baseDamage, hitPoint, playerBasePos, out _);
        }

        /// <summary>部位: 0=胸 1=头 2=腹 3=四肢</summary>
        private int CalculateBodyPartDamage(int baseDamage, Vec3 hitPoint, Vec3 playerBasePos, out int bodyPart)
        {
            float relativeY = hitPoint.y - playerBasePos.y;
            float ratio = relativeY / GameConstants.PlayerHeight;

            float multiplier;
            if (ratio >= GameConstants.HeadHeightRatio)
            {
                multiplier = GameConstants.HeadDamageMultiplier;
                bodyPart = 1;
            }
            else if (ratio >= GameConstants.ChestHeightRatio)
            {
                multiplier = GameConstants.ChestDamageMultiplier;
                bodyPart = 0;
            }
            else if (ratio >= GameConstants.AbdomenHeightRatio)
            {
                multiplier = GameConstants.AbdomenDamageMultiplier;
                bodyPart = 2;
            }
            else
            {
                multiplier = GameConstants.LimbDamageMultiplier;
                bodyPart = 3;
            }

            return Math.Max(1, (int)(baseDamage * multiplier));
        }

        private void Log(string message)
        {
            Console.WriteLine($"[BattleRoom {BattleId}] {DateTime.Now:HH:mm:ss.fff} {message}");
        }
    }

    /// <summary>
    /// Server-side bullet entity.
    /// </summary>
    public class ServerBullet
    {
        public int AttackId;
        public int OwnerId;
        public int OwnerTeamId;
        public ShootingGame.Shared.Hero.GunConfigData Gun;
        public Vec3 Position;
        public Vec3 Direction;
        public float Speed;
        public float MaxDistance;
        public float TraveledDistance;
        public int Damage;
        public int SpawnFrameId;
    }
}