using System.Collections.Generic;
using ShootingGame.Shared.ECS;
using ShootingGame.Shared.ECS.Components;
using ShootingGame.Shared.Math;
using ShootingGame.Shared.Physics;
using ShootingGame.Shared.Simulation;

namespace ShootingGame.Server
{
    /// <summary>
    /// Builds full world state snapshots for reconnecting clients.
    /// A WorldSnapshot contains all entities, their components, and the
    /// current game state — enough to fully reconstruct the client's view.
    ///
    /// Reference: SpaceBuilder's NetObjectRouter.CollectFullState() pattern.
    /// </summary>
    public static class WorldSnapshotBuilder
    {
        /// <summary>
        /// A serializable snapshot of the entire game world.
        /// Sent to clients on initial connect or reconnection.
        /// </summary>
        public struct Snapshot
        {
            public int ServerTick;
            public uint StateRevision;
            public List<EntitySnapshot> Entities;
            public CollisionWorld CollisionWorld;
        }

        public struct EntitySnapshot
        {
            public uint NetId;
            public byte PlayerId;
            public bool IsLocalPlayer;
            public Vec3 Position;
            public Quat Rotation;
            public Vec3 Velocity;
            public float VerticalVelocity;
            public bool IsGrounded;
            public byte Health;
            public byte MaxHealth;
            public byte Ammo;
            public byte PlayerState;
            public ulong TagBitmask;
            public byte ActiveAbilities0;
            public byte ActiveAbilities1;
            public byte ActiveAbilities2;
            public byte ActiveAbilities3;
        }

        /// <summary>
        /// Build a full world snapshot from the server ECS world.
        /// </summary>
        public static Snapshot Build(EntityManager em, CollisionWorld collisionWorld,
            int serverTick, uint stateRevision)
        {
            var entities = new List<EntitySnapshot>();
            var playerEntities = new List<Entity>();
            em.GetEntitiesWith(ComponentType.Mask(ComponentTypeId.PlayerState), playerEntities);

            foreach (var entity in playerEntities)
            {
                var transform = em.GetComponent<TransformComponent>(entity);
                var movement = em.GetComponent<MovementComponent>(entity);
                var health = em.GetComponent<HealthComponent>(entity);
                var state = em.GetComponent<PlayerStateComponent>(entity);
                var tag = em.TryGetComponent<TagComponent>(entity, out var t) ? t : default;
                var ammo = em.TryGetComponent<AmmoComponent>(entity, out var a) ? a : default;
                var ability = em.TryGetComponent<AbilityInstanceComponent>(entity, out var ab) ? ab : default;

                entities.Add(new EntitySnapshot
                {
                    NetId = (uint)entity.Id,
                    PlayerId = (byte)entity.Id,
                    Position = transform.Position,
                    Rotation = transform.Rotation,
                    Velocity = movement.Velocity,
                    VerticalVelocity = movement.VerticalVelocity,
                    IsGrounded = movement.IsGrounded,
                    Health = health.Current,
                    MaxHealth = health.Max,
                    Ammo = ammo.Current,
                    PlayerState = (byte)(state.State),
                    TagBitmask = tag.TagBitMask,
                    ActiveAbilities0 = ability.Slot0,
                    ActiveAbilities1 = ability.Slot1,
                    ActiveAbilities2 = ability.Slot2,
                    ActiveAbilities3 = ability.Slot3,
                });
            }

            return new Snapshot
            {
                ServerTick = serverTick,
                StateRevision = stateRevision,
                Entities = entities,
                CollisionWorld = collisionWorld,
            };
        }
    }
}
