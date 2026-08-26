using System;
using System.Collections.Generic;
using ShootingGame.Shared.Ability;
using ShootingGame.Shared.ECS;
using ShootingGame.Shared.Hero;
using ShootingGame.Shared.Physics;
using ShootingGame.Shared.Simulation;

namespace ShootingGame.Network.Server
{
    /// <summary>
    /// Unity Host 可引用的服务端 ECS composition root。
    ///
    /// HostBattleServer 只负责传输和房间元数据；玩家状态、输入应用以及
    /// 固定 tick 模拟均由这个纯 C# World 持有。
    /// </summary>
    public sealed class HostServerEcsWorld : IDisposable
    {
        private readonly EntityManager _entities = new EntityManager();
        private readonly Dictionary<int, Entity> _players = new Dictionary<int, Entity>();
        private readonly PlayerSimulationPipeline _pipeline;
        private CollisionWorld _collisionWorld;
        private bool _disposed;

        public EntityManager Entities => _entities;

        public HostServerEcsWorld(CollisionWorld collisionWorld = null)
        {
            _collisionWorld = collisionWorld ?? new CollisionWorld();
            _pipeline = new PlayerSimulationPipeline(_collisionWorld);
        }

        public void SetCollisionWorld(CollisionWorld collisionWorld)
        {
            if (collisionWorld == null || ReferenceEquals(collisionWorld, _collisionWorld))
                return;

            _collisionWorld = collisionWorld;
            _pipeline.SetCollisionWorld(collisionWorld);
        }

        public bool ContainsPlayer(int playerId)
        {
            return _players.TryGetValue(playerId, out var entity) && _entities.IsValid(entity);
        }

        public bool TryGetEntity(int playerId, out Entity entity)
        {
            if (_players.TryGetValue(playerId, out entity) && _entities.IsValid(entity))
                return true;

            entity = Entity.Invalid;
            return false;
        }

        public Entity RegisterPlayer(int playerId, PlayerSnapshot snapshot)
        {
            ThrowIfDisposed();

            if (_players.TryGetValue(playerId, out var existing) && _entities.IsValid(existing))
            {
                ECSBridge.ApplyServerCorrection(_entities, existing, snapshot);
                return existing;
            }

            var entity = ECSBridge.CreatePlayerEntity(_entities, snapshot);
            _players[playerId] = entity;
            return entity;
        }

        public void RemovePlayer(int playerId)
        {
            if (!_players.TryGetValue(playerId, out var entity))
                return;

            if (_entities.IsValid(entity))
                _entities.DestroyEntity(entity);
            _players.Remove(playerId);
        }

        public void SubmitInput(int playerId, InputFrame input)
        {
            if (_players.TryGetValue(playerId, out var entity) && _entities.IsValid(entity))
                ECSBridge.WriteInput(_entities, entity, input);
        }

        public void TickPlayer(int playerId, InputFrame input, float deltaTime)
        {
            if (!_players.TryGetValue(playerId, out var entity) || !_entities.IsValid(entity))
                return;

            ECSBridge.WriteInput(_entities, entity, input);
            _pipeline.TickPlayer(_entities, entity, input, deltaTime);
        }

        public ushort TryActivateAbility(int playerId, byte assetId)
        {
            if (!_players.TryGetValue(playerId, out var entity) || !_entities.IsValid(entity))
                return 0;

            return AbilityLifecycleSystem.RequestActivate(_entities, entity, assetId, isPredicting: false);
        }

        public bool TryGetSnapshot(int playerId, int tick, out PlayerSnapshot snapshot)
        {
            if (_players.TryGetValue(playerId, out var entity) && _entities.IsValid(entity))
            {
                snapshot = ECSBridge.BuildSnapshot(_entities, entity, tick);
                return true;
            }

            snapshot = default;
            return false;
        }

        public PlayerSnapshot GetSnapshot(int playerId, int tick)
        {
            return TryGetSnapshot(playerId, tick, out var snapshot) ? snapshot : default;
        }

        public void ApplySnapshot(int playerId, PlayerSnapshot snapshot)
        {
            if (_players.TryGetValue(playerId, out var entity) && _entities.IsValid(entity))
                ECSBridge.ApplyServerCorrection(_entities, entity, snapshot);
        }

        public void ConfigurePlayer(int playerId, HeroConfig heroConfig, GunConfigData gun)
        {
            if (!_players.TryGetValue(playerId, out var entity) || !_entities.IsValid(entity))
                return;

            if (heroConfig != null && _entities.TryGetComponent<MovementComponent>(entity, out var movement))
            {
                movement.PlayerRadius = heroConfig.PlayerRadius;
                movement.PlayerHeight = heroConfig.PlayerHeight;
                movement.MaxMoveSpeed = heroConfig.MoveSpeed;
                _entities.SetComponent(entity, movement);
            }

            if (heroConfig != null && _entities.TryGetComponent<HealthComponent>(entity, out var health))
            {
                health.Max = heroConfig.MaxHP;
                health.Current = (byte)Math.Min(health.Current, health.Max);
                _entities.SetComponent(entity, health);
            }

            if (gun != null)
            {
                if (_entities.TryGetComponent<AmmoComponent>(entity, out var ammo))
                {
                    ammo.Max = gun.ClipSize;
                    ammo.Current = Math.Min(ammo.Current, ammo.Max);
                    _entities.SetComponent(entity, ammo);
                }

                if (_entities.TryGetComponent<FireCooldownComponent>(entity, out var cooldown))
                {
                    cooldown.Rate = gun.FireRate;
                    _entities.SetComponent(entity, cooldown);
                }

                if (_entities.TryGetComponent<ReloadComponent>(entity, out var reload))
                {
                    reload.Duration = gun.ReloadTime;
                    _entities.SetComponent(entity, reload);
                }
            }
        }

        public bool TrySetHealth(int playerId, byte health)
        {
            if (!_players.TryGetValue(playerId, out var entity)
                || !_entities.IsValid(entity)
                || !_entities.TryGetComponent<HealthComponent>(entity, out var component))
                return false;

            component.Current = health;
            _entities.SetComponent(entity, component);
            return true;
        }

        public void Clear()
        {
            foreach (var entity in _players.Values)
            {
                if (_entities.IsValid(entity))
                    _entities.DestroyEntity(entity);
            }

            _players.Clear();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            Clear();
            _entities.Clear();
            _disposed = true;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(HostServerEcsWorld));
        }
    }
}
