using System.Collections.Generic;
using UnityEngine;
using ShootingGame.Shared.Ability;
using ShootingGame.Shared.ECS;
using ShootingGame.Shared.GameplayTags;
using ShootingGame.Shared.Hero;
using ShootingGame.Shared.Math;
using ShootingGame.Shared.Simulation;

/// <summary>
/// 客户端 ECS 世界：持有 EntityManager，管理所有玩家实体的生命周期。
/// 挂载在场景中的单例 GameObject 上。
/// </summary>
public class ClientECSWorld : MonoBehaviour
{
    public static ClientECSWorld Instance { get; private set; }

    private EntityManager _entityManager;
    private readonly Dictionary<int, Entity> _playerEntities = new Dictionary<int, Entity>();
    private readonly Dictionary<int, PlayerSnapshot> _serverSnapshots = new Dictionary<int, PlayerSnapshot>();
    private readonly Dictionary<int, HeroConfig> _playerHeroConfigs = new Dictionary<int, HeroConfig>();

    public EntityManager EntityManager => _entityManager;

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
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// 注册本地玩家并创建对应的 ECS 实体。
    /// </summary>
    public Entity RegisterLocalPlayer(int playerId, Vec3 spawnPosition, HeroConfig heroConfig = null)
    {
        if (_playerEntities.TryGetValue(playerId, out var existing))
        {
            if (_entityManager.IsValid(existing))
                return existing;
            _playerEntities.Remove(playerId);
        }

        var snap = PlayerSnapshot.Default(spawnPosition);
        var entity = ECSBridge.CreatePlayerEntity(_entityManager, snap);
        _playerEntities[playerId] = entity;

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
        }
        else
        {
            GrantDefaultAbilities(entity);
        }

        return entity;
    }

    /// <summary>
    /// 注册远程玩家并创建对应的 ECS 实体（仅包含视觉相关组件）。
    /// </summary>
    public Entity RegisterRemotePlayer(int playerId, Vec3 spawnPosition, HeroConfig heroConfig = null)
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
        return entity;
    }

    /// <summary>
    /// 注销玩家并销毁其 ECS 实体。
    /// </summary>
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

    /// <summary>
    /// 获取玩家的 ECS 实体。
    /// </summary>
    public Entity GetPlayerEntity(int playerId)
    {
        return _playerEntities.TryGetValue(playerId, out var entity) ? entity : Entity.Invalid;
    }

    /// <summary>
    /// 获取本地玩家实体（playerId=0 通常是本地玩家，但也可能是自定义 ID）。
    /// </summary>
    public Entity GetLocalPlayerEntity()
    {
        // 返回第一个注册的玩家实体作为本地玩家
        foreach (var kv in _playerEntities)
        {
            if (_entityManager.HasComponent<InputComponent>(kv.Value))
                return kv.Value;
        }
        return Entity.Invalid;
    }

    /// <summary>
    /// 更新远程玩家的服务端状态缓存（用于插值）。
    /// </summary>
    public void CacheServerSnapshot(int playerId, PlayerSnapshot snap)
    {
        _serverSnapshots[playerId] = snap;
    }

    /// <summary>
    /// 将服务端快照应用到远程玩家的 ECS 实体。
    /// </summary>
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

    /// <summary>
    /// 获取本地玩家的 PlayerSnapshot（从 ECS 实体构建）。
    /// </summary>
    public PlayerSnapshot GetLocalSnapshot(int tick)
    {
        var entity = GetLocalPlayerEntity();
        if (!_entityManager.IsValid(entity)) return default;
        return ECSBridge.BuildSnapshot(_entityManager, entity, tick);
    }

    /// <summary>
    /// 获取远程玩家的 TransformComponent。
    /// </summary>
    public bool TryGetRemoteTransform(int playerId, out TransformComponent tx)
    {
        tx = default;
        if (!_playerEntities.TryGetValue(playerId, out var entity)) return false;
        if (!_entityManager.IsValid(entity)) return false;
        return _entityManager.TryGetComponent(entity, out tx);
    }

    /// <summary>
    /// 给实体授予默认能力配置。
    /// </summary>
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

    /// <summary>
    /// 客户端预测激活能力。返回 instanceId（0 表示失败）。
    /// </summary>
    public ushort TryActivateAbility(int playerId, byte assetId)
    {
        var entity = GetPlayerEntity(playerId);
        if (!_entityManager.IsValid(entity)) return 0;
        return AbilityLifecycleSystem.RequestActivate(_entityManager, entity, assetId, isPredicting: true);
    }

    /// <summary>
    /// 服务端确认预测激活。
    /// </summary>
    public bool ConfirmActivate(int playerId, ushort instanceId)
    {
        var entity = GetPlayerEntity(playerId);
        if (!_entityManager.IsValid(entity)) return false;
        return AbilityLifecycleSystem.ConfirmActivate(_entityManager, entity, instanceId);
    }

    /// <summary>
    /// 服务端拒绝预测激活。
    /// </summary>
    public bool RejectActivate(int playerId, ushort instanceId)
    {
        var entity = GetPlayerEntity(playerId);
        if (!_entityManager.IsValid(entity)) return false;
        return AbilityLifecycleSystem.RejectActivate(_entityManager, entity, instanceId);
    }

    /// <summary>
    /// 清空所有玩家实体。
    /// </summary>
    public void ClearAll()
    {
        _entityManager.Clear();
        _playerEntities.Clear();
        _serverSnapshots.Clear();
        _playerHeroConfigs.Clear();
    }
}
