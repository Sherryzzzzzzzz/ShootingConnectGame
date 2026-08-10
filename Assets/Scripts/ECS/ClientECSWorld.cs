using System.Collections.Generic;
using UnityEngine;
using ShootingGame.Network;
using ShootingGame.Shared.Ability;
using ShootingGame.Shared.ECS;
using ShootingGame.Shared.GameplayTags;
using ShootingGame.Shared.Hero;
using ShootingGame.Shared.Math;
using ShootingGame.Shared.Physics;
using ShootingGame.Shared.Protocol;
using ShootingGame.Shared.Simulation;

/// <summary>
/// 客户端 ECS 世界：系统宿主 + 实体管理 + tick 循环。
/// 挂载在场景单例 GameObject 上，每帧驱动所有客户端 ECS 系统。
/// 原 NetPlayerController 的 tick/输入/网络/视觉/动画逻辑全部收敛于此。
/// </summary>
public class ClientECSWorld : MonoBehaviour
{
    public static ClientECSWorld Instance { get; private set; }

    private EntityManager _entityManager;
    private readonly Dictionary<int, Entity> _playerEntities = new Dictionary<int, Entity>();
    private readonly Dictionary<int, PlayerSnapshot> _serverSnapshots = new Dictionary<int, PlayerSnapshot>();
    private readonly Dictionary<int, HeroConfig> _playerHeroConfigs = new Dictionary<int, HeroConfig>();

    // ==================== Tick 状态 ====================
    private int _currentTick;
    private float _accumulator;
    private float _tickInterval;
    private RingBuffer<InputFrame> _inputHistory;
    private RingBuffer<PlayerSnapshot> _snapshotHistory;
    private int _lastServerTick = -1;
    private CollisionWorld _collisionWorld;
    private BattleClient _battleClient;

    // ==================== 本地玩家状态 ====================
    private bool _isDead;
    private PlayerCombatBehaviour _combatBehaviour;
    private float _bloomHeat;
    public float CurrentSpreadDeg { get; private set; }

    /// <summary>是否已收到首个服务端帧（开火闸门）。</summary>
    public bool HasReceivedServerFrame { get; set; }

    /// <summary>本地玩家 ID。</summary>
    public int LocalPlayerId { get; private set; } = -1;

    /// <summary>本地玩家当前预测快照（UI/调试读取）。</summary>
    public PlayerSnapshot CurrentSnapshot;

    public EntityManager EntityManager => _entityManager;
    public int CurrentTick => _currentTick;
    public bool IsDead => _isDead;
    public PlayerCombatBehaviour LocalCombatBehaviour => _combatBehaviour;
    public RingBuffer<InputFrame> InputHistory => _inputHistory;
    public RingBuffer<PlayerSnapshot> SnapshotHistory => _snapshotHistory;
    public CollisionWorld CollisionWorld => _collisionWorld;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        GameplayTagConfig.Initialize();
        _entityManager = new EntityManager();
        _tickInterval = GameConstants.TickDelta;
        _inputHistory = new RingBuffer<InputFrame>(GameConstants.SnapshotHistorySize);
        _snapshotHistory = new RingBuffer<PlayerSnapshot>(GameConstants.SnapshotHistorySize);

        _collisionWorld = CollisionWorldLoader.Instance;
        if (_collisionWorld == null)
        {
            _collisionWorld = new CollisionWorld();
            _collisionWorld.AddBox(new AABB(new Vec3(-50, -1, -50), new Vec3(50, 0, 50)));
        }
    }

    private void Start()
    {
        _battleClient = BattleClient.Instance;
        if (_battleClient != null)
        {
            _battleClient.OnFrameReceived += OnFrameReceived;
            _battleClient.OnHitEvent += ClientHitEventSystem.ProcessHitEvent;
            _battleClient.OnBattleStart += OnBattleStart;
            if (_battleClient.IsInBattle) OnBattleStart();
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (_battleClient != null)
        {
            _battleClient.OnFrameReceived -= OnFrameReceived;
            _battleClient.OnHitEvent -= ClientHitEventSystem.ProcessHitEvent;
            _battleClient.OnBattleStart -= OnBattleStart;
        }
        ClientInputSystem.Shutdown();
    }

    private void OnBattleStart()
    {
        _currentTick = 1;
        _lastServerTick = -1;
        _accumulator = 0f;
        _bloomHeat = 0f;
        CurrentSpreadDeg = 0f;
        _isDead = false;

        var em = _entityManager;
        var entity = GetLocalPlayerEntity();
        if (!em.IsValid(entity)) return;

        // 初始快照
        CurrentSnapshot = ECSBridge.BuildSnapshot(em, entity, 0);
        DynamicTickSystem.Instance?.Reset(1);
    }

    // ==================== Tick 循环 ====================

    private void Update()
    {
        if (_battleClient == null || !_battleClient.IsInBattle)
        {
            _battleClient = BattleClient.Instance;
            return;
        }

        _accumulator += Time.unscaledDeltaTime;
        while (_accumulator >= _tickInterval)
        {
            ClientTick();
            _accumulator -= _tickInterval;
        }

        UpdatePresentation();
    }

    /// <summary>单个模拟 tick：输入 → 网络 → 预测 → 攻击重传 → 动画决策。</summary>
    private void ClientTick()
    {
        _currentTick++;

        var em = _entityManager;
        var entity = GetLocalPlayerEntity();
        if (!em.IsValid(entity)) return;

        if (_isDead)
        {
            var deadInput = ClientInputSystem.Tick(em, entity, _currentTick, 0f);
            ClientNetworkSyncSystem.SendLocalOperation(_currentTick, deadInput, CurrentSnapshot);
            return;
        }

        // 1. 输入采集
        float moveYaw = CurrentSnapshot.Rotation.EulerAngles.y;
        InputFrame inputFrame = ClientInputSystem.Tick(em, entity, _currentTick, moveYaw);

        // 2. 网络发送（含开火/技能）
        ClientNetworkSyncSystem.SendLocalOperation(_currentTick, inputFrame, CurrentSnapshot);

        // 3. ECS 预测 tick（移动/重力/换弹/冷却/技能）
        CurrentSnapshot = ClientPredictionSystem.PredictTick(
            em, entity, inputFrame, _tickInterval, _collisionWorld,
            _inputHistory, _snapshotHistory, _currentTick);

        // 4. 攻击重传
        ClientAttackSystem.TickResend(em, entity);

        // 5. 扩散热度恢复
        UpdateBloomRecovery();

        // 6. 本地动画决策（tick 驱动，60Hz，与原 PistolGirlStateMachine 一致）
        ClientAnimationSystem.UpdatePlayer(em, entity);

        DynamicTickSystem.Instance?.SetClientFrame(_currentTick);
    }

    /// <summary>每帧表现层：视觉平滑 + 动画 + 远程插值（表现系统，不触碰模拟数据）。</summary>
    private void UpdatePresentation()
    {
        var em = _entityManager;
        var entities = new List<Entity>();
        em.GetEntitiesWith<PlayerViewComponent>(entities);

        foreach (var entity in entities)
        {
            if (!em.HasComponent<PlayerViewComponent>(entity)) continue;
            var view = em.GetComponent<PlayerViewComponent>(entity);
            if (view.View == null) continue;

            if (view.IsLocal)
            {
                // 本地：ECS Transform → GameObject 平滑（唯一写入者）
                // 本地动画已在 ClientTick（60Hz）中由 ClientAnimationSystem 驱动
                ClientVisualSyncSystem.SyncLocalView(em, entity, view.View.transform);
            }
            else
            {
                // 远程：插值采样 → 写 view + ECS Transform（内部自洽）
                ClientRemoteInterpolationSystem.UpdateRemoteTransform(em, entity);
                // 远程动画：Update 驱动（每帧，与原 PistolAnimationDriver 一致）
                ClientAnimationSystem.UpdatePlayer(em, entity);
            }
        }
    }

    /// <summary>扩散热度衰减（非开火帧持续衰减）。</summary>
    private void UpdateBloomRecovery()
    {
        var heroConfig = GetHeroConfig(LocalPlayerId);
        var gun = heroConfig?.Gun;
        if (gun == null || _bloomHeat <= 0f || gun.BloomRecover <= 0f) return;

        _bloomHeat = Mathf.Max(0f, _bloomHeat - gun.BloomRecover * Time.deltaTime);
        bool isMoving = (CurrentSnapshot.Velocity.x * CurrentSnapshot.Velocity.x
                      + CurrentSnapshot.Velocity.z * CurrentSnapshot.Velocity.z) > 1f;
        CurrentSpreadDeg = SpreadUtility.ComputeTotalSpread(gun, isMoving, _bloomHeat);
    }

    // ==================== 玩家注册 ====================

    /// <summary>注册本地玩家并创建对应 ECS 实体（含客户端表现组件）。</summary>
    public Entity RegisterLocalPlayer(int playerId, Vec3 spawnPosition, HeroConfig heroConfig = null, GameObject viewObject = null)
    {
        LocalPlayerId = playerId;

        if (_playerEntities.TryGetValue(playerId, out var existing))
        {
            if (_entityManager.IsValid(existing))
                return existing;
            _playerEntities.Remove(playerId);
        }

        var snap = PlayerSnapshot.Default(spawnPosition);
        var entity = ECSBridge.CreatePlayerEntity(_entityManager, snap);
        _playerEntities[playerId] = entity;

        // 客户端组件
        AttachClientComponents(entity, playerId, true, viewObject, heroConfig);

        if (heroConfig != null)
        {
            _playerHeroConfigs[playerId] = heroConfig;
            GrantHeroAbilities(entity, heroConfig);

            if (_entityManager.TryGetComponent<MovementComponent>(entity, out var move))
            {
                move.PlayerRadius = heroConfig.PlayerRadius;
                move.PlayerHeight = heroConfig.PlayerHeight;
                move.MaxMoveSpeed = heroConfig.MoveSpeed;
                _entityManager.SetComponent(entity, move);
            }

            if (_entityManager.TryGetComponent<HealthComponent>(entity, out var hp))
            {
                hp.Max = heroConfig.MaxHP;
                hp.Current = heroConfig.MaxHP;
                _entityManager.SetComponent(entity, hp);
            }

            var gun = heroConfig.Gun ?? ShootingGame.Shared.Hero.GunRegistry.GetGun(heroConfig.StartingGunId);
            if (gun != null && _entityManager.TryGetComponent<AmmoComponent>(entity, out var ammo))
            {
                ammo.Max = gun.ClipSize;
                ammo.Current = gun.ClipSize;
                _entityManager.SetComponent(entity, ammo);
            }
            if (gun != null && _entityManager.TryGetComponent<FireCooldownComponent>(entity, out var fc))
            {
                fc.Rate = gun.FireRate;
                _entityManager.SetComponent(entity, fc);
            }
            if (gun != null && _entityManager.TryGetComponent<ReloadComponent>(entity, out var rc))
            {
                rc.Duration = gun.ReloadTime;
                _entityManager.SetComponent(entity, rc);
            }
        }
        else
        {
            GrantDefaultAbilities(entity);
        }

        // 绑定 NetworkBehaviour（战斗重置后 Unbind 过，需重建）
        if (_combatBehaviour == null || !_combatBehaviour.IsBound)
        {
            _combatBehaviour = new PlayerCombatBehaviour();
            _combatBehaviour.Bind(entity, _entityManager, NetObjectType.Player);
        }

        CurrentSnapshot = ECSBridge.BuildSnapshot(_entityManager, entity, 0);
        return entity;
    }

    /// <summary>注册远程玩家并创建对应 ECS 实体（视觉组件）。</summary>
    public Entity RegisterRemotePlayer(int playerId, Vec3 spawnPosition, HeroConfig heroConfig = null, GameObject viewObject = null)
    {
        if (_playerEntities.TryGetValue(playerId, out var existing))
        {
            if (_entityManager.IsValid(existing))
                return existing;
            _playerEntities.Remove(playerId);
        }

        byte maxHp = heroConfig?.MaxHP ?? GameConstants.MaxHealth;
        float radius = heroConfig?.PlayerRadius ?? GameConstants.PlayerRadius;
        float height = heroConfig?.PlayerHeight ?? GameConstants.PlayerHeight;
        float moveSpeed = heroConfig?.MoveSpeed ?? GameConstants.MoveSpeed;

        if (heroConfig != null)
            _playerHeroConfigs[playerId] = heroConfig;

        var entity = _entityManager.CreateEntity();
        _entityManager.AddComponent(entity, new TransformComponent(spawnPosition, Quat.Identity));
        _entityManager.AddComponent(entity, new MovementComponent(Vec3.Zero, 0f, true)
        {
            PlayerRadius = radius,
            PlayerHeight = height,
            MaxMoveSpeed = moveSpeed
        });
        _entityManager.AddComponent(entity, new PlayerStateComponent(PlayerStateEnum.Ground));
        _entityManager.AddComponent(entity, new HealthComponent(maxHp, maxHp));
        _playerEntities[playerId] = entity;

        AttachClientComponents(entity, playerId, false, viewObject, heroConfig);
        return entity;
    }

    /// <summary>挂载客户端表现组件（视图/动画/输入边缘/攻击队列）。</summary>
    private void AttachClientComponents(Entity entity, int playerId, bool isLocal, GameObject viewObject, HeroConfig heroConfig)
    {
        var em = _entityManager;

        if (!em.HasComponent<InputEdgeComponent>(entity))
            em.AddComponent(entity, new InputEdgeComponent());
        if (!em.HasComponent<AnimationStateComponent>(entity))
            em.AddComponent(entity, new AnimationStateComponent
            {
                LastHp = heroConfig?.MaxHP ?? GameConstants.MaxHealth
            });
        if (!em.HasComponent<PendingAttackComponent>(entity))
            ClientAttackSystem.Ensure(em, entity);

        if (!em.HasComponent<PlayerViewComponent>(entity))
        {
            var view = new PlayerViewComponent
            {
                IsLocal = isLocal,
                PlayerId = playerId,
                View = viewObject
            };
            if (viewObject != null)
            {
                view.AnimationView = viewObject.GetComponent<PlayerAnimationView>();
                view.FirePoint = FindFirePoint(viewObject);
                if (!isLocal)
                {
                    view.InterpBuffer = new InterpolationBuffer();
                    view.LastKnownHp = heroConfig?.MaxHP ?? GameConstants.MaxHealth;
                    view.LastKnownAlive = true;
                    view.LastKnownGrounded = true;
                }
            }
            em.AddComponent(entity, view);
        }
    }

    private static Transform FindFirePoint(GameObject view)
    {
        if (view == null) return null;
        var animView = view.GetComponent<PlayerAnimationView>();
        if (animView != null && animView.firePoint != null) return animView.firePoint;
        return view.transform;
    }

    /// <summary>注销玩家并销毁其 ECS 实体。</summary>
    public void UnregisterPlayer(int playerId)
    {
        if (_playerEntities.TryGetValue(playerId, out var entity))
        {
            _entityManager.DestroyEntity(entity);
            _playerEntities.Remove(playerId);
        }
        _serverSnapshots.Remove(playerId);
        _playerHeroConfigs.Remove(playerId);
    }

    /// <summary>获取玩家的 ECS 实体。</summary>
    public Entity GetPlayerEntity(int playerId)
    {
        return _playerEntities.TryGetValue(playerId, out var entity) ? entity : Entity.Invalid;
    }

    /// <summary>获取本地玩家实体。</summary>
    public Entity GetLocalPlayerEntity()
    {
        foreach (var kv in _playerEntities)
        {
            if (_entityManager.HasComponent<InputComponent>(kv.Value))
                return kv.Value;
        }
        return Entity.Invalid;
    }

    public PlayerAnimationView GetLocalPlayerView()
    {
        var entity = GetLocalPlayerEntity();
        if (!_entityManager.HasComponent<PlayerViewComponent>(entity)) return null;
        var pv = _entityManager.GetComponent<PlayerViewComponent>(entity);
        return pv.AnimationView ?? (pv.View != null ? pv.View.GetComponent<PlayerAnimationView>() : null);
    }

    public HeroConfig GetHeroConfig(int playerId)
    {
        return _playerHeroConfigs.TryGetValue(playerId, out var cfg) ? cfg : null;
    }

    /// <summary>缓存远程玩家服务端状态。</summary>
    public void CacheServerSnapshot(int playerId, PlayerSnapshot snap)
    {
        _serverSnapshots[playerId] = snap;
    }

    /// <summary>将服务端快照应用到远程玩家 ECS 实体。</summary>
    public void ApplyServerStateToRemote(int playerId, PlayerSnapshot snap)
    {
        if (!_playerEntities.TryGetValue(playerId, out var entity)) return;
        if (!_entityManager.IsValid(entity)) return;

        if (_entityManager.TryGetComponent<TransformComponent>(entity, out var tx))
        {
            tx.Position = snap.Position;
            tx.Rotation = snap.Rotation;
            _entityManager.SetComponent(entity, tx);
        }
        if (_entityManager.TryGetComponent<MovementComponent>(entity, out var mv))
        {
            mv.Velocity = snap.Velocity;
            mv.VerticalVelocity = snap.VerticalVelocity;
            mv.IsGrounded = snap.IsGrounded;
            _entityManager.SetComponent(entity, mv);
        }
        if (_entityManager.TryGetComponent<PlayerStateComponent>(entity, out _))
            _entityManager.SetComponent(entity, new PlayerStateComponent(snap.State));
        if (_entityManager.TryGetComponent<HealthComponent>(entity, out _))
        {
            byte maxHp = _playerHeroConfigs.TryGetValue(playerId, out var hc) ? hc.MaxHP : GameConstants.MaxHealth;
            _entityManager.SetComponent(entity, new HealthComponent(snap.Health, maxHp));
        }
    }

    /// <summary>获取本地玩家 PlayerSnapshot。</summary>
    public PlayerSnapshot GetLocalSnapshot(int tick)
    {
        var entity = GetLocalPlayerEntity();
        if (!_entityManager.IsValid(entity)) return default;
        return ECSBridge.BuildSnapshot(_entityManager, entity, tick);
    }

    public bool TryGetRemoteTransform(int playerId, out TransformComponent tx)
    {
        tx = default;
        if (!_playerEntities.TryGetValue(playerId, out var entity)) return false;
        if (!_entityManager.IsValid(entity)) return false;
        return _entityManager.TryGetComponent(entity, out tx);
    }

    // ==================== 能力 ====================

    public ushort TryActivateAbility(int playerId, byte assetId)
    {
        var entity = GetPlayerEntity(playerId);
        if (!_entityManager.IsValid(entity)) return 0;
        return AbilityLifecycleSystem.RequestActivate(_entityManager, entity, assetId, isPredicting: true);
    }

    public bool ConfirmActivate(int playerId, ushort instanceId)
    {
        var entity = GetPlayerEntity(playerId);
        if (!_entityManager.IsValid(entity)) return false;
        return AbilityLifecycleSystem.ConfirmActivate(_entityManager, entity, instanceId);
    }

    public bool RejectActivate(int playerId, ushort instanceId)
    {
        var entity = GetPlayerEntity(playerId);
        if (!_entityManager.IsValid(entity)) return false;
        return AbilityLifecycleSystem.RejectActivate(_entityManager, entity, instanceId);
    }

    private void GrantDefaultAbilities(Entity entity)
    {
        var configs = AbilityConfig.CreateDefaults();
        var owner = new AbilityOwnerComponent
        {
            GrantedAbilities = new List<AbilityConfig>(configs),
            GrantedMask = 0
        };
        foreach (var cfg in configs)
            owner.GrantedMask |= (1L << cfg.AssetId);

        if (_entityManager.HasComponent<AbilityOwnerComponent>(entity))
            _entityManager.SetComponent(entity, owner);
        else
            _entityManager.AddComponent(entity, owner);

        if (!_entityManager.HasComponent<AbilityInstanceComponent>(entity))
            _entityManager.AddComponent(entity, new AbilityInstanceComponent());
    }

    private void GrantHeroAbilities(Entity entity, HeroConfig heroConfig)
    {
        var configs = heroConfig.Abilities;
        var owner = new AbilityOwnerComponent
        {
            GrantedAbilities = new List<AbilityConfig>(configs),
            GrantedMask = 0
        };
        foreach (var cfg in configs)
            owner.GrantedMask |= (1L << cfg.AssetId);

        if (_entityManager.HasComponent<AbilityOwnerComponent>(entity))
            _entityManager.SetComponent(entity, owner);
        else
            _entityManager.AddComponent(entity, owner);

        if (!_entityManager.HasComponent<AbilityInstanceComponent>(entity))
            _entityManager.AddComponent(entity, new AbilityInstanceComponent());
    }

    // ==================== 服务端帧处理 ====================

    private void OnFrameReceived(AllPlayerOperation frame)
    {
        // 网络逻辑：本地和解/HP/死亡/技能确认
        ClientNetworkSyncSystem.OnFrameReceived(frame);
    }

    /// <summary>本地玩家和解（ClientNetworkSyncSystem 调用）。</summary>
    public void ReconcileWithServer(PlayerStateMsg serverState, int serverTick)
    {
        if (serverTick <= _lastServerTick) return;

        var em = _entityManager;
        var entity = GetLocalPlayerEntity();
        if (!em.IsValid(entity)) return;

        ClientReconciliationSystem.Reconcile(
            em, entity, serverState, serverTick,
            ref CurrentSnapshot,
            _inputHistory, _snapshotHistory,
            _currentTick, _tickInterval, _collisionWorld,
            ref _lastServerTick);
    }

    // ==================== 死亡 / 复活 ====================

    public void SetDead()
    {
        if (_isDead) return;
        _isDead = true;
        var view = GetLocalPlayerView();
        if (view != null) view.PlayDeath();
    }

    public void Revive(Vector3 spawnPosition)
    {
        _isDead = false;
        CurrentSnapshot = PlayerSnapshot.Default(spawnPosition.ToShared());

        var entity = GetLocalPlayerEntity();
        if (_entityManager.IsValid(entity))
        {
            if (_entityManager.TryGetComponent<HealthComponent>(entity, out var hp))
            {
                hp.Current = hp.Max;
                _entityManager.SetComponent(entity, hp);
            }
            if (_entityManager.TryGetComponent<TransformComponent>(entity, out var tx))
            {
                tx.Position = spawnPosition.ToShared();
                _entityManager.SetComponent(entity, tx);
            }
            var view = GetLocalPlayerView();
            if (view != null)
            {
                view.transform.position = spawnPosition;
                var animator = view.GetComponent<Animator>();
                if (animator != null) animator.SetBool("Dead", false);
            }
        }
        _lastServerTick = -1;
    }

    // ==================== 清空 ====================

    public void ClearAll()
    {
        _entityManager.Clear();
        _playerEntities.Clear();
        _serverSnapshots.Clear();
        _playerHeroConfigs.Clear();
        _inputHistory = new RingBuffer<InputFrame>(GameConstants.SnapshotHistorySize);
        _snapshotHistory = new RingBuffer<PlayerSnapshot>(GameConstants.SnapshotHistorySize);
        _currentTick = 0;
        _lastServerTick = -1;
        _isDead = false;
        HasReceivedServerFrame = false;

        // 解绑 NetworkBehaviour（实体已销毁）
        _combatBehaviour?.Unbind();
        _combatBehaviour = null;
    }
}
