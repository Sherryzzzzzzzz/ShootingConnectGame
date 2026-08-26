using ShootingGame.Shared.Math;
using ShootingGame.Shared.Physics;
using ShootingGame.Shared.Simulation;
using ShootingGame.Shared.GameplayTags;

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

            bool dashing = false;
            bool charging = false;
            bool shielded = false;
            if (em.TryGetComponent<TagComponent>(entity, out var tags))
            {
                dashing = GameplayTagConfig.Tag_Action_Dashing.Matches(tags.TagBitMask);
                charging = GameplayTagConfig.Tag_Action_Charging.Matches(tags.TagBitMask);
                shielded = GameplayTagConfig.Tag_Buff_DamageResist.Matches(tags.TagBitMask);
            }

            if (dashing || charging)
            {
                var abilityMotion = em.TryGetComponent<AbilityMotionComponent>(entity, out var currentMotion)
                    ? currentMotion
                    : default;
                if (dashing)
                {
                    if (abilityMotion.DashDirection.SqrMagnitude < 0.001f)
                    {
                        abilityMotion.DashDirection = moveDir.SqrMagnitude > 0.001f
                            ? moveDir.Normalized
                            : (transform.Rotation * Vec3.Forward).Normalized;
                    }
                    moveDir = abilityMotion.DashDirection;
                }
                else
                {
                    if (abilityMotion.ChargeDirection.SqrMagnitude < 0.001f)
                        abilityMotion.ChargeDirection = (transform.Rotation * Vec3.Forward).Normalized;
                    moveDir = abilityMotion.ChargeDirection;
                }
                moveMag = 1f;
                if (em.HasComponent<AbilityMotionComponent>(entity)) em.SetComponent(entity, abilityMotion);
                else em.AddComponent(entity, abilityMotion);
            }

            float maxSpeed = movement.MaxMoveSpeed;
            float targetSpeed = input.Run
                ? maxSpeed * GameConstants.RunMultiplier
                : maxSpeed;
            if (input.Aim) targetSpeed *= GameConstants.AimMoveMultiplier;
            if (dashing) targetSpeed = 22f;
            else if (charging) targetSpeed = 14f;
            else if (shielded) targetSpeed *= 0.65f;

            Vec3 targetVelocity = moveDir * (targetSpeed * moveMag);
            if (dashing || charging)
                movement.Velocity = targetVelocity;
            else if (movement.Velocity.SqrMagnitude < 0.0001f && moveMag > 0.001f)
                movement.Velocity = targetVelocity;
            else
            {
                float acceleration = moveMag > 0.001f
                    ? GameConstants.MovementAcceleration
                    : GameConstants.MovementStopAcceleration;
                movement.Velocity = Vec3.Lerp(
                    movement.Velocity, targetVelocity,
                    GameMath.Clamp01(acceleration * dt));
            }
            movement.Dirty.MarkDirty(0); // Velocity

            if (world != null)
            {
                // 跳跃输入（不在跳跃帧上立即应用重力，避免削弱初始速度）
                if (input.Jump && movement.IsGrounded && !charging)
                {
                    movement.VerticalVelocity = GameConstants.JumpForce;
                    movement.IsGrounded = false;
                    movement.Dirty.MarkDirty(1); // VerticalVelocity
                    movement.Dirty.MarkDirty(2); // IsGrounded
                }

                // 构建总位移向量（在重力之前，使跳跃帧使用完整 JumpForce）
                float hx = movement.Velocity.x * dt;
                float hy = movement.VerticalVelocity * dt;
                float hz = movement.Velocity.z * dt;
                Vec3 displacement = new Vec3(hx, hy, hz);

                // 重力（在位移之后应用，为下一帧准备）
                if (!movement.IsGrounded)
                {
                    movement.VerticalVelocity += GameConstants.Gravity * dt;
                    movement.Dirty.MarkDirty(1); // VerticalVelocity
                }

                Vec3 beforeMove = transform.Position;
                var result = KinematicMover.Move(transform.Position, displacement, movement.PlayerRadius, world);

                transform.Position = result.Position;
                movement.IsGrounded = result.IsGrounded;

                if (movement.IsGrounded)
                    movement.VerticalVelocity = 0f;

                if (charging)
                {
                    Vec3 actual = transform.Position - beforeMove;
                    actual.y = 0f;
                    float expected = targetSpeed * dt;
                    if (actual.Magnitude < expected * 0.25f && em.TryGetComponent<TagComponent>(entity, out tags))
                    {
                        tags.TagBitMask &= ~GameplayTagConfig.Tag_Action_Charging.SelfMask;
                        tags.TagBitMask &= ~GameplayTagConfig.Tag_Buff_Unstoppable.SelfMask;
                        em.SetComponent(entity, tags);
                    }
                }

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
                float newX = transform.Position.x + movement.Velocity.x * dt;
                float newZ = transform.Position.z + movement.Velocity.z * dt;
                transform.Position = new Vec3(newX, transform.Position.y, newZ);
            }

            em.SetComponent(entity, transform);
            em.SetComponent(entity, movement);
        }
    }
}
