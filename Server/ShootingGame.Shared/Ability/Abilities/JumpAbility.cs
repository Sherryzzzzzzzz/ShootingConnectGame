using ShootingGame.Shared.ECS;
using ShootingGame.Shared.Simulation;

namespace ShootingGame.Shared.Ability.Abilities
{
    /// <summary>
    /// 跳跃能力：设置垂直速度，脱离地面。
    /// </summary>
    public class JumpAbility : IAbilityBehavior
    {
        public bool CanActivate(EntityManager em, Entity entity, AbilityConfig config)
        {
            if (!em.HasComponent<MovementComponent>(entity)) return false;
            var move = em.GetComponent<MovementComponent>(entity);
            return move.IsGrounded;
        }

        public void OnActivate(EntityManager em, Entity entity, AbilityConfig config)
        {
            if (!em.HasComponent<MovementComponent>(entity)) return;

            var move = em.GetComponent<MovementComponent>(entity);
            move.VerticalVelocity = GameConstants.JumpForce;
            move.IsGrounded = false;
            em.SetComponent(entity, move);
        }

        public void OnUpdate(EntityManager em, Entity entity, AbilityConfig config, float dt) { }

        public void OnDeactivate(EntityManager em, Entity entity, AbilityConfig config) { }

        public void OnCancel(EntityManager em, Entity entity, AbilityConfig config) { }
    }
}
