using ShootingGame.Shared.Ability;
using ShootingGame.Shared.Simulation;
using ShootingGame.Shared.GameplayTags;

namespace ShootingGame.Shared.ECS
{
    /// <summary>
    /// ECS ↔ PlayerSnapshot/InputFrame 桥接层。
    /// 负责在传统快照格式和 ECS 组件之间双向转换。
    /// </summary>
    public static class ECSBridge
    {
        /// <summary>
        /// 从 PlayerSnapshot 创建 ECS 玩家实体并初始化所有组件。
        /// </summary>
        public static Entity CreatePlayerEntity(EntityManager em, PlayerSnapshot snap)
        {
            var entity = em.CreateEntity();

            em.AddComponent(entity, new TransformComponent(snap.Position, snap.Rotation));
            em.AddComponent(entity, new MovementComponent(snap.Velocity, snap.VerticalVelocity, snap.IsGrounded));
            em.AddComponent(entity, new PlayerStateComponent(snap.State));
            em.AddComponent(entity, new FireCooldownComponent(snap.FireCooldown));
            em.AddComponent(entity, new HealthComponent(snap.Health, GameConstants.MaxHealth));
            em.AddComponent(entity, new AmmoComponent(snap.CurrentAmmo, GameConstants.MaxAmmoPerClip));
            em.AddComponent(entity, new ReloadComponent(snap.IsReloading, snap.ReloadTimer));
            em.AddComponent(entity, new TagComponent(new TagContainer { EffectiveMask = snap.TagBitmask }));
            em.AddComponent(entity, new InputComponent { Tick = snap.Tick });

            if (snap.ActiveAbilityCount > 0 && snap.ActiveAbilities != null)
            {
                var abilityComp = new AbilityInstanceComponent();
                for (int i = 0; i < snap.ActiveAbilityCount && i < 4; i++)
                {
                    abilityComp.SetSlot(i, snap.ActiveAbilities[i]);
                    abilityComp.ActiveCount++;
                }
                em.AddComponent(entity, abilityComp);
            }

            return entity;
        }

        /// <summary>
        /// 从 ECS 实体构建 PlayerSnapshot（用于网络发送）。
        /// </summary>
        public static PlayerSnapshot BuildSnapshot(EntityManager em, Entity entity, int tick)
        {
            var snap = new PlayerSnapshot { Tick = tick };

            if (em.TryGetComponent<TransformComponent>(entity, out var tx))
            {
                snap.Position = tx.Position;
                snap.Rotation = tx.Rotation;
            }

            if (em.TryGetComponent<MovementComponent>(entity, out var mv))
            {
                snap.Velocity = mv.Velocity;
                snap.VerticalVelocity = mv.VerticalVelocity;
                snap.IsGrounded = mv.IsGrounded;
            }

            if (em.TryGetComponent<PlayerStateComponent>(entity, out var ps))
                snap.State = ps.State;

            if (em.TryGetComponent<FireCooldownComponent>(entity, out var fc))
                snap.FireCooldown = fc.Cooldown;

            if (em.TryGetComponent<HealthComponent>(entity, out var hp))
                snap.Health = hp.Current;

            if (em.TryGetComponent<AmmoComponent>(entity, out var ammo))
                snap.CurrentAmmo = ammo.Current;

            if (em.TryGetComponent<ReloadComponent>(entity, out var rel))
            {
                snap.IsReloading = rel.IsReloading;
                snap.ReloadTimer = rel.Timer;
            }

            if (em.TryGetComponent<TagComponent>(entity, out var tc))
                snap.TagBitmask = tc.TagBitMask;

            if (em.TryGetComponent<AbilityInstanceComponent>(entity, out var ac))
            {
                snap.ActiveAbilityCount = ac.ActiveCount;
                snap.ActiveAbilities = new AbilityInstanceData[4];
                for (int i = 0; i < 4; i++)
                {
                    var slot = ac.GetSlot(i);
                    if (slot.IsActive)
                        snap.ActiveAbilities[i] = slot;
                }
            }

            return snap;
        }

        /// <summary>
        /// 用服务端权威快照覆盖 ECS 组件（用于客户端调谐）。
        /// </summary>
        public static void ApplyServerCorrection(EntityManager em, Entity entity, PlayerSnapshot snap)
        {
            if (em.TryGetComponent<TransformComponent>(entity, out var tx))
            {
                tx.Position = snap.Position;
                tx.Rotation = snap.Rotation;
                em.SetComponent(entity, tx);
            }

            if (em.TryGetComponent<MovementComponent>(entity, out var mv))
            {
                mv.Velocity = snap.Velocity;
                mv.VerticalVelocity = snap.VerticalVelocity;
                mv.IsGrounded = snap.IsGrounded;
                em.SetComponent(entity, mv);
            }

            if (em.TryGetComponent<PlayerStateComponent>(entity, out _))
                em.SetComponent(entity, new PlayerStateComponent(snap.State));

            if (em.TryGetComponent<FireCooldownComponent>(entity, out _))
                em.SetComponent(entity, new FireCooldownComponent(snap.FireCooldown));

            if (em.TryGetComponent<HealthComponent>(entity, out _))
                em.SetComponent(entity, new HealthComponent(snap.Health, GameConstants.MaxHealth));

            if (em.TryGetComponent<AmmoComponent>(entity, out _))
                em.SetComponent(entity, new AmmoComponent(snap.CurrentAmmo, GameConstants.MaxAmmoPerClip));

            if (em.TryGetComponent<ReloadComponent>(entity, out _))
                em.SetComponent(entity, new ReloadComponent(snap.IsReloading, snap.ReloadTimer));

            if (em.TryGetComponent<TagComponent>(entity, out _))
                em.SetComponent(entity, new TagComponent(new TagContainer { EffectiveMask = snap.TagBitmask }));

            if (snap.ActiveAbilityCount > 0 && snap.ActiveAbilities != null)
            {
                var abilityComp = em.HasComponent<AbilityInstanceComponent>(entity)
                    ? em.GetComponent<AbilityInstanceComponent>(entity)
                    : new AbilityInstanceComponent();

                for (int i = 0; i < snap.ActiveAbilityCount && i < 4; i++)
                {
                    abilityComp.SetSlot(i, snap.ActiveAbilities[i]);
                }
                abilityComp.ActiveCount = snap.ActiveAbilityCount;
                em.SetComponent(entity, abilityComp);
            }
        }

        /// <summary>
        /// 将 InputFrame 写入实体的 InputComponent。
        /// </summary>
        public static void WriteInput(EntityManager em, Entity entity, InputFrame input)
        {
            em.SetComponent(entity, new InputComponent
            {
                Tick = input.Tick,
                Movement = input.Movement,
                Jump = input.Jump,
                Run = input.Run,
                Aim = input.Aim,
                Fire = input.Fire,
                Reload = input.Reload,
                Ability1 = input.Ability1,
                Ability2 = input.Ability2,
                Ability3 = input.Ability3,
                Ability4 = input.Ability4,
                AimYaw = input.AimYaw,
                AimPitch = input.AimPitch
            });
        }
    }
}
