using ShootingGame.Shared.Math;
using ShootingGame.Shared.Physics;
using ShootingGame.Shared.Simulation;

namespace ShootingGame.Shared.ECS
{
    public static class MovementSystem
    {
        public static void Execute(EntityManager em, Entity entity, float dt, CollisionWorld world = null)
        {
            if (!em.HasComponent<InputComponent>(entity)) return;
            if (!em.HasComponent<TransformComponent>(entity)) return;
            if (!em.HasComponent<MovementComponent>(entity)) return;

            var input = em.GetComponent<InputComponent>(entity);
            var transform = em.GetComponent<TransformComponent>(entity);
            var movement = em.GetComponent<MovementComponent>(entity);

            Vec3 moveDir = new Vec3(input.Movement.x, 0f, input.Movement.y);
            float moveMag = moveDir.Magnitude;
            if (moveMag > 1f) moveDir = moveDir / moveMag;
            if (moveMag < 0.001f) moveDir = Vec3.Zero;

            float maxSpeed = movement.MaxMoveSpeed;
            float targetSpeed = input.Run
                ? maxSpeed * GameConstants.RunMultiplier
                : maxSpeed;

            movement.Velocity = moveDir * targetSpeed;
            movement.Dirty.MarkDirty(0); // Velocity

            if (world != null)
            {
                // 跳跃输入（不在跳跃帧上立即应用重力，避免削弱初始速度）
                if (input.Jump && movement.IsGrounded)
                {
                    movement.VerticalVelocity = GameConstants.JumpForce;
                    movement.IsGrounded = false;
                    movement.Dirty.MarkDirty(1); // VerticalVelocity
                    movement.Dirty.MarkDirty(2); // IsGrounded
                }

                // 构建总位移向量（在重力之前，使跳跃帧使用完整 JumpForce）
                float hx = moveDir.x * targetSpeed * moveMag * dt;
                float hy = movement.VerticalVelocity * dt;
                float hz = moveDir.z * targetSpeed * moveMag * dt;
                Vec3 displacement = new Vec3(hx, hy, hz);

                // 重力（在位移之后应用，为下一帧准备）
                if (!movement.IsGrounded)
                {
                    movement.VerticalVelocity += GameConstants.Gravity * dt;
                    movement.Dirty.MarkDirty(1); // VerticalVelocity
                }

                var result = KinematicMover.Move(transform.Position, displacement, movement.PlayerRadius, world);

                transform.Position = result.Position;
                movement.IsGrounded = result.IsGrounded;

                if (movement.IsGrounded)
                    movement.VerticalVelocity = 0f;

                // 更新 PlayerState
                if (em.HasComponent<PlayerStateComponent>(entity))
                {
                    var state = em.GetComponent<PlayerStateComponent>(entity);
                    state.State = movement.IsGrounded ? PlayerStateEnum.Ground : PlayerStateEnum.Sky;
                    em.SetComponent(entity, state);
                }
            }
            else
            {
                float newX = transform.Position.x + moveDir.x * targetSpeed * moveMag * dt;
                float newZ = transform.Position.z + moveDir.z * targetSpeed * moveMag * dt;
                transform.Position = new Vec3(newX, transform.Position.y, newZ);
            }

            em.SetComponent(entity, transform);
            em.SetComponent(entity, movement);
        }
    }
}
