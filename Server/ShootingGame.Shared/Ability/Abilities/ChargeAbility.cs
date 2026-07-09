using ShootingGame.Shared.ECS;
using ShootingGame.Shared.Math;

namespace ShootingGame.Shared.Ability.Abilities
{
    public class ChargeAbility : IAbilityBehavior
    {
        private const float ChargeForce = 20f;

        public bool CanActivate(EntityManager em, Entity entity, AbilityConfig config)
        {
            if (!em.HasComponent<MovementComponent>(entity)) return false;
            if (!em.HasComponent<HealthComponent>(entity)) return false;
            var move = em.GetComponent<MovementComponent>(entity);
            var hp = em.GetComponent<HealthComponent>(entity);
            return move.IsGrounded && hp.IsAlive;
        }

        public void OnActivate(EntityManager em, Entity entity, AbilityConfig config)
        {
            if (!em.HasComponent<MovementComponent>(entity)) return;
            if (!em.HasComponent<TransformComponent>(entity)) return;

            var move = em.GetComponent<MovementComponent>(entity);
            var tx = em.GetComponent<TransformComponent>(entity);

            Vec3 forward = tx.Rotation * Vec3.Forward;
            forward = forward.Normalized;
            move.Velocity = forward * ChargeForce;
            em.SetComponent(entity, move);
        }

        public void OnUpdate(EntityManager em, Entity entity, AbilityConfig config, float dt) { }
        public void OnDeactivate(EntityManager em, Entity entity, AbilityConfig config) { }
        public void OnCancel(EntityManager em, Entity entity, AbilityConfig config) { }
    }
}
