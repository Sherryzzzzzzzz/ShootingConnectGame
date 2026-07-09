using ShootingGame.Shared.Physics;
using ShootingGame.Shared.Simulation;

namespace ShootingGame.Shared.ECS
{
    /// <summary>
    /// 重力系统：处理跳跃和重力。
    /// 当 CollisionWorld 可用时，MovementSystem 已处理跳跃/重力/地面检测，本系统空操作。
    /// </summary>
    public static class GravitySystem
    {
        public static void Execute(EntityManager em, Entity entity, float dt, CollisionWorld world = null)
        {
            // MovementSystem 已处理跳跃+重力+碰撞
            if (world != null) return;

            if (!em.HasComponent<TransformComponent>(entity)) return;
            if (!em.HasComponent<MovementComponent>(entity)) return;
            if (!em.HasComponent<InputComponent>(entity)) return;

            var input = em.GetComponent<InputComponent>(entity);
            var transform = em.GetComponent<TransformComponent>(entity);
            var movement = em.GetComponent<MovementComponent>(entity);

            if (input.Jump && movement.IsGrounded)
            {
                movement.VerticalVelocity = GameConstants.JumpForce;
                movement.IsGrounded = false;
            }

            if (!movement.IsGrounded)
                movement.VerticalVelocity += GameConstants.Gravity * dt;

            float newY = transform.Position.y + movement.VerticalVelocity * dt;
            transform.Position = new Math.Vec3(transform.Position.x, newY, transform.Position.z);

            em.SetComponent(entity, transform);
            em.SetComponent(entity, movement);
        }
    }
}
