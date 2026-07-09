using ShootingGame.Shared.Math;
using ShootingGame.Shared.Physics;

namespace ShootingGame.Shared.ECS
{
    /// <summary>
    /// 地面检测系统。
    /// 当 CollisionWorld 可用时，MovementSystem 已处理地面检测，本系统空操作。
    /// </summary>
    public static class GroundDetectionSystem
    {
        private const float GroundY = 0.01f;

        public static void Execute(EntityManager em, Entity entity, CollisionWorld world = null)
        {
            // MovementSystem (via KinematicMover) 已处理地面检测
            if (world != null) return;

            if (!em.HasComponent<TransformComponent>(entity)) return;
            if (!em.HasComponent<MovementComponent>(entity)) return;

            var transform = em.GetComponent<TransformComponent>(entity);
            var movement = em.GetComponent<MovementComponent>(entity);

            if (transform.Position.y <= GroundY)
            {
                transform.Position = new Vec3(transform.Position.x, GroundY, transform.Position.z);
                movement.VerticalVelocity = 0f;
                movement.IsGrounded = true;

                em.SetComponent(entity, transform);
                em.SetComponent(entity, movement);

                if (em.HasComponent<PlayerStateComponent>(entity))
                {
                    var state = em.GetComponent<PlayerStateComponent>(entity);
                    if (state.State == Simulation.PlayerStateEnum.Sky)
                    {
                        state.State = Simulation.PlayerStateEnum.Ground;
                        em.SetComponent(entity, state);
                    }
                }
            }
        }
    }
}
