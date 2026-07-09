using System.Collections.Generic;
using ShootingGame.Shared.Ability;
using ShootingGame.Shared.ECS;
using ShootingGame.Shared.Hero;
using ShootingGame.Shared.Physics;
using ShootingGame.Shared.Simulation;

namespace ShootingGame.Server.ECS
{
    /// <summary>
    /// 服务端 ECS 世界：管理所有玩家实体，按正确顺序运行 ECS 系统。
    /// 替代直接调用 PlayerSimulation.Simulate()。
    /// </summary>
    public class ServerECSWorld
    {
        public EntityManager EntityManager { get; } = new EntityManager();

        private readonly Dictionary<int, Entity> _playerEntities = new Dictionary<int, Entity>();
        private readonly Dictionary<int, AbilityOwnerComponent> _abilityOwners = new Dictionary<int, AbilityOwnerComponent>();

        private CollisionWorld _collisionWorld;

        public int PlayerCount => _playerEntities.Count;

        /// <summary>
        /// 注册玩家实体。从 PlayerSnapshot 创建 ECS 实体并授予默认能力。
        /// </summary>
        public Entity RegisterPlayer(int bpId, PlayerSnapshot snap, HeroConfig heroConfig = null)
        {
            if (_playerEntities.TryGetValue(bpId, out var existing))
                UnregisterPlayer(bpId);

            var entity = ECSBridge.CreatePlayerEntity(EntityManager, snap);
            _playerEntities[bpId] = entity;

            if (heroConfig != null)
            {
                GrantHeroAbilities(bpId, entity, heroConfig);

                // Override MovementComponent with hero-specific physics
                if (EntityManager.TryGetComponent<MovementComponent>(entity, out var move))
                {
                    move.PlayerRadius = heroConfig.PlayerRadius;
                    move.PlayerHeight = heroConfig.PlayerHeight;
                    move.MaxMoveSpeed = heroConfig.MoveSpeed;
                    EntityManager.SetComponent(entity, move);
                }

                // Override HealthComponent with hero-specific max
                if (EntityManager.TryGetComponent<HealthComponent>(entity, out var hp))
                {
                    hp.Max = heroConfig.MaxHP;
                    hp.Current = heroConfig.MaxHP;
                    EntityManager.SetComponent(entity, hp);
                }
            }
            else
            {
                GrantDefaultAbilities(bpId, entity);
            }

            return entity;
        }

        /// <summary>
        /// 注销玩家实体。
        /// </summary>
        public void UnregisterPlayer(int bpId)
        {
            if (_playerEntities.TryGetValue(bpId, out var entity))
            {
                if (EntityManager.IsValid(entity))
                    EntityManager.DestroyEntity(entity);
                _playerEntities.Remove(bpId);
            }
            _abilityOwners.Remove(bpId);
        }

        /// <summary>
        /// 设置碰撞世界（用于未来 ECS 碰撞集成）。
        /// </summary>
        public void SetCollisionWorld(CollisionWorld world)
        {
            _collisionWorld = world;
        }

        /// <summary>
        /// 获取玩家实体。
        /// </summary>
        public Entity GetEntity(int bpId)
        {
            _playerEntities.TryGetValue(bpId, out var entity);
            return entity;
        }

        /// <summary>
        /// 授予默认能力配置。
        /// </summary>
        private void GrantDefaultAbilities(int bpId, Entity entity)
        {
            var configs = AbilityConfig.CreateDefaults();
            var owner = new AbilityOwnerComponent
            {
                GrantedAbilities = new List<AbilityConfig>(configs),
                GrantedMask = 0
            };

            foreach (var cfg in configs)
                owner.GrantedMask |= (1L << cfg.AssetId);

            if (EntityManager.HasComponent<AbilityOwnerComponent>(entity))
                EntityManager.SetComponent(entity, owner);
            else
                EntityManager.AddComponent(entity, owner);

            if (!EntityManager.HasComponent<AbilityInstanceComponent>(entity))
                EntityManager.AddComponent(entity, new AbilityInstanceComponent());

            _abilityOwners[bpId] = owner;
        }

        /// <summary>
        /// 授予英雄特定能力配置。
        /// </summary>
        private void GrantHeroAbilities(int bpId, Entity entity, HeroConfig heroConfig)
        {
            var configs = heroConfig.Abilities;
            var owner = new AbilityOwnerComponent
            {
                GrantedAbilities = new List<AbilityConfig>(configs),
                GrantedMask = 0
            };

            foreach (var cfg in configs)
                owner.GrantedMask |= (1L << cfg.AssetId);

            if (EntityManager.HasComponent<AbilityOwnerComponent>(entity))
                EntityManager.SetComponent(entity, owner);
            else
                EntityManager.AddComponent(entity, owner);

            if (!EntityManager.HasComponent<AbilityInstanceComponent>(entity))
                EntityManager.AddComponent(entity, new AbilityInstanceComponent());

            _abilityOwners[bpId] = owner;
        }

        /// <summary>
        /// 对单个玩家实体运行完整 tick。
        /// </summary>
        public void TickPlayer(int bpId, Entity entity, InputFrame input, float dt)
        {
            PlayerSystemGroup.TickPlayer(EntityManager, entity, input, dt, _collisionWorld);
        }

        /// <summary>
        /// 从 ECS 实体构建玩家快照。
        /// </summary>
        public PlayerSnapshot GetSnapshot(int bpId, int tick)
        {
            if (!_playerEntities.TryGetValue(bpId, out var entity))
                return default;

            return ECSBridge.BuildSnapshot(EntityManager, entity, tick);
        }

        /// <summary>
        /// 应用服务端权威状态到 ECS 实体。
        /// </summary>
        public void ApplyServerState(int bpId, PlayerSnapshot snap)
        {
            if (!_playerEntities.TryGetValue(bpId, out var entity))
                return;
            ECSBridge.ApplyServerCorrection(EntityManager, entity, snap);
        }

        /// <summary>
        /// 检查并请求激活能力（服务端权威）。返回 InstanceId（0=失败）。
        /// </summary>
        public ushort TryActivateAbility(int bpId, byte assetId)
        {
            if (!_playerEntities.TryGetValue(bpId, out var entity))
                return 0;
            return AbilityLifecycleSystem.RequestActivate(EntityManager, entity, assetId, isPredicting: false);
        }

        /// <summary>
        /// 清理所有实体。
        /// </summary>
        public void Clear()
        {
            foreach (var (_, entity) in _playerEntities)
            {
                if (EntityManager.IsValid(entity))
                    EntityManager.DestroyEntity(entity);
            }
            _playerEntities.Clear();
            _abilityOwners.Clear();
        }
    }
}
