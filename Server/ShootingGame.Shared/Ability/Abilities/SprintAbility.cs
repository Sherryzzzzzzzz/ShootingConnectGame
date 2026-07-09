using ShootingGame.Shared.ECS;

namespace ShootingGame.Shared.Ability.Abilities
{
    /// <summary>
    /// 冲刺能力：应用 Action.Running 标签，移动系统读取此标签来应用速度倍率。
    /// </summary>
    public class SprintAbility : IAbilityBehavior
    {
        private const float SprintSpeedMultiplier = 1.6f;

        public bool CanActivate(EntityManager em, Entity entity, AbilityConfig config)
        {
            if (!em.HasComponent<MovementComponent>(entity)) return false;
            if (!em.HasComponent<HealthComponent>(entity)) return false;

            var move = em.GetComponent<MovementComponent>(entity);
            var health = em.GetComponent<HealthComponent>(entity);
            return move.IsGrounded && health.IsAlive;
        }

        public void OnActivate(EntityManager em, Entity entity, AbilityConfig config)
        {
            if (!em.HasComponent<MovementComponent>(entity)) return;
            var move = em.GetComponent<MovementComponent>(entity);
            move.MaxMoveSpeed *= SprintSpeedMultiplier;
            em.SetComponent(entity, move);
        }

        public void OnUpdate(EntityManager em, Entity entity, AbilityConfig config, float dt) { }

        public void OnDeactivate(EntityManager em, Entity entity, AbilityConfig config)
        {
            if (!em.HasComponent<MovementComponent>(entity)) return;
            var move = em.GetComponent<MovementComponent>(entity);
            move.MaxMoveSpeed /= SprintSpeedMultiplier;
            em.SetComponent(entity, move);
        }

        public void OnCancel(EntityManager em, Entity entity, AbilityConfig config)
        {
            if (!em.HasComponent<MovementComponent>(entity)) return;
            var move = em.GetComponent<MovementComponent>(entity);
            move.MaxMoveSpeed /= SprintSpeedMultiplier;
            em.SetComponent(entity, move);
        }
    }
}
