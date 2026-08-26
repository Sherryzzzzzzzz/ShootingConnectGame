using ShootingGame.Shared.Math;
using ShootingGame.Shared.Physics;

namespace ShootingGame.Shared.Simulation
{
    public static class PlayerSimulation
    {
        public static PlayerSnapshot Simulate(PlayerSnapshot snap, InputFrame input, float dt, CollisionWorld collisionWorld = null)
        {
            snap.Tick = input.Tick;

            Vec3 moveDir = new Vec3(input.Movement.x, 0f, input.Movement.y);
            float moveMag = moveDir.Magnitude;
            if (moveMag > 1f) moveDir = moveDir / moveMag;
            if (moveMag < 0.001f) moveDir = Vec3.Zero;

            float targetSpeed = input.Run ? GameConstants.MoveSpeed * GameConstants.RunMultiplier : GameConstants.MoveSpeed;
            if (input.Aim) targetSpeed *= GameConstants.AimMoveMultiplier;
            Vec3 targetVelocity = moveDir * (moveMag * targetSpeed);
            float acceleration = moveMag > 0.001f
                ? GameConstants.MovementAcceleration
                : GameConstants.MovementStopAcceleration;
            snap.Velocity = snap.Velocity.SqrMagnitude < 0.0001f && moveMag > 0.001f
                ? targetVelocity
                : Vec3.Lerp(snap.Velocity, targetVelocity,
                    GameMath.Clamp01(acceleration * dt));

            // 跳跃输入（不在跳跃帧上立即应用重力，避免削弱初始速度）
            if (input.Jump && snap.IsGrounded)
            {
                snap.VerticalVelocity = GameConstants.JumpForce;
                snap.IsGrounded = false;
                snap.State = PlayerStateEnum.Sky;
            }

            if (collisionWorld != null)
            {
                float hx = snap.Velocity.x * dt;
                float hy = snap.VerticalVelocity * dt;
                float hz = snap.Velocity.z * dt;
                Vec3 displacement = new Vec3(hx, hy, hz);

                // 重力（在位移之后应用，为下一帧准备）
                if (!snap.IsGrounded)
                    snap.VerticalVelocity += GameConstants.Gravity * dt;

                var result = KinematicMover.Move(snap.Position, displacement, GameConstants.PlayerRadius, collisionWorld);

                snap.Position = result.Position;
                snap.IsGrounded = result.IsGrounded;
                if (snap.IsGrounded)
                    snap.VerticalVelocity = 0f;
                snap.State = snap.IsGrounded ? PlayerStateEnum.Ground : PlayerStateEnum.Sky;
            }
            else
            {
                float groundY = 0.01f;

                float newX = snap.Position.x + snap.Velocity.x * dt;
                float newY = snap.Position.y + snap.VerticalVelocity * dt;
                float newZ = snap.Position.z + snap.Velocity.z * dt;

                if (newY <= groundY)
                {
                    newY = groundY;
                    snap.VerticalVelocity = 0f;
                    snap.IsGrounded = true;
                    snap.State = PlayerStateEnum.Ground;
                }

                snap.Position = new Vec3(newX, newY, newZ);

                // 重力（在位移之后应用，为下一帧准备）
                if (!snap.IsGrounded)
                    snap.VerticalVelocity += GameConstants.Gravity * dt;
            }

            if (input.Tick > 0)
            {
                float targetYaw;
                bool hasTargetYaw;
                if (input.Aim)
                {
                    targetYaw = input.AimYaw;
                    hasTargetYaw = true;
                }
                else
                {
                    hasTargetYaw = moveDir.SqrMagnitude > 0.0001f;
                    targetYaw = hasTargetYaw
                        ? GameMath.Atan2(moveDir.x, moveDir.z) * GameMath.Rad2Deg
                        : 0f;
                }

                if (hasTargetYaw)
                {
                    Quat targetRot = Quat.Euler(0f, targetYaw, 0f);
                    snap.Rotation = Quat.RotateTowards(snap.Rotation, targetRot, GameConstants.RotationSpeed * dt);
                }
            }

            if (snap.FireCooldown > 0f)
                snap.FireCooldown -= dt;

            if (snap.IsReloading)
            {
                snap.ReloadTimer -= dt;
                if (snap.ReloadTimer <= 0f)
                {
                    snap.CurrentAmmo = snap.MaxAmmo;
                    snap.IsReloading = false;
                    snap.ReloadTimer = 0f;
                }
            }
            else if (input.Reload && snap.CurrentAmmo < snap.MaxAmmo)
            {
                snap.IsReloading = true;
                snap.ReloadTimer = snap.ReloadDuration;
            }

            if (input.Fire && snap.FireCooldown <= 0f && !snap.IsReloading && snap.CurrentAmmo > 0)
            {
                snap.FireCooldown = snap.FireInterval;
                snap.CurrentAmmo--;
            }

            return snap;
        }
    }
}
