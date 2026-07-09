using ShootingGame.Shared.Math;
using ShootingGame.Shared.Simulation;

namespace ShootingGame.Shared.Physics
{
    public struct MoveResult
    {
        public Vec3 Position;
        public bool IsGrounded;
        public Vec3 GroundNormal;
    }

    /// <summary>
    /// Kinematic character mover. Moves a sphere (bottom of capsule) through the collision world
    /// with sliding collision response and ground detection.
    /// </summary>
    public static class KinematicMover
    {
        private const int MaxSlideIterations = 3;
        private const float GroundCheckDistance = 0.08f; // 必须 < JumpForce*dt (≈0.134)，否则跳跃会被拉回地面
        private const float SkinWidth = 0.01f;
        private const float MinMoveDistance = 0.001f;
        private const float GroundOffset = 0.02f; // small offset to keep sphere above ground

        /// <summary>
        /// Move the player through the collision world.
        /// Separates horizontal and vertical movement to avoid floor clipping during lateral moves.
        /// </summary>
        public static MoveResult Move(Vec3 position, Vec3 movement, float radius, CollisionWorld world)
        {
            // 添加 SkinWidth 防止球心恰好落在 expanded AABB 边界上，
            // 导致 SweepSphere 返回 tmin=0 且 normal=Zero，使 SlideMove 死锁
            Vec3 sphereOrigin = position + Vec3.Up * (radius + GroundOffset + SkinWidth);

            // Pre-detect ground normal for slope-aware movement
            Vec3 groundNormal = Vec3.Up;
            var preGround = world.SweepSphere(sphereOrigin, radius, Vec3.Down, GroundCheckDistance + GroundOffset);
            if (preGround.Hit)
            {
                float slopeAngle = Vec3.Angle(preGround.Normal, Vec3.Up);
                if (slopeAngle <= GameConstants.SlopeLimit)
                {
                    groundNormal = preGround.Normal;
                }
            }

            // 1. Horizontal movement (project onto slope + slide along walls) + step-up detection
            Vec3 horizontal = new Vec3(movement.x, 0f, movement.z);
            if (horizontal.SqrMagnitude > MinMoveDistance * MinMoveDistance)
            {
                // Project horizontal movement onto ground surface plane for slope walking
                Vec3 slopeMove = horizontal - groundNormal * Vec3.Dot(horizontal, groundNormal);

                Vec3 beforeSlide = sphereOrigin;
                sphereOrigin = SlideMove(sphereOrigin, slopeMove, radius, world);

                // Check if blocked by a short obstacle — try step-up
                Vec3 actualMove = sphereOrigin - beforeSlide;
                actualMove.y = 0f;
                float expectedDist = horizontal.Magnitude;
                float actualDist = actualMove.Magnitude;

                if (actualDist < expectedDist - MinMoveDistance)
                {
                    if (TryStepUp(beforeSlide, slopeMove, radius, world, out Vec3 stepResult))
                    {
                        sphereOrigin = stepResult;
                    }
                }
            }

            // 2. Vertical movement (gravity/jump)
            float verticalMove = movement.y;
            if (GameMath.Abs(verticalMove) > MinMoveDistance)
            {
                Vec3 vertDir = verticalMove > 0 ? Vec3.Up : Vec3.Down;
                float vertDist = GameMath.Abs(verticalMove);

                var hit = world.SweepSphere(sphereOrigin, radius, vertDir, vertDist + SkinWidth);
                if (hit.Hit)
                {
                    float safeDist = GameMath.Max(0f, hit.Distance - SkinWidth);
                    sphereOrigin = sphereOrigin + vertDir * safeDist;
                }
                else
                {
                    sphereOrigin = sphereOrigin + vertDir * vertDist;
                }
            }

            // Convert sphere center back to feet position
            Vec3 feetPos = sphereOrigin - Vec3.Up * (radius + GroundOffset);

            // 3. Ground check
            var groundResult = GroundCheck(sphereOrigin, radius, world);

            // Snap to ground if grounded (remove the offset gap)
            if (groundResult.Hit && groundResult.Distance > 0f)
            {
                feetPos = feetPos - Vec3.Up * groundResult.Distance;
                sphereOrigin = sphereOrigin - Vec3.Up * groundResult.Distance;
            }

            return new MoveResult
            {
                Position = feetPos,
                IsGrounded = groundResult.Hit,
                GroundNormal = groundResult.Hit ? groundResult.Normal : Vec3.Up
            };
        }

        private static Vec3 SlideMove(Vec3 origin, Vec3 movement, float radius, CollisionWorld world)
        {
            Vec3 remaining = movement;

            for (int i = 0; i < MaxSlideIterations; i++)
            {
                float dist = remaining.Magnitude;
                if (dist < MinMoveDistance)
                    break;

                Vec3 dir = remaining / dist;

                var hit = world.SweepSphere(origin, radius, dir, dist + SkinWidth);

                if (!hit.Hit)
                {
                    origin = origin + remaining;
                    break;
                }

                // Move to just before the contact point
                float safeDistance = GameMath.Max(0f, hit.Distance - SkinWidth);
                origin = origin + dir * safeDistance;

                // Calculate remaining movement and project onto the surface
                float remainingDist = dist - safeDistance;
                Vec3 remainingMovement = dir * remainingDist;

                // Slide: remove the component along the hit normal
                remaining = remainingMovement - hit.Normal * Vec3.Dot(remainingMovement, hit.Normal);
            }

            return origin;
        }

        /// <summary>
        /// Attempt to step over a low obstacle by lifting, moving horizontally, and dropping down.
        /// </summary>
        private static bool TryStepUp(Vec3 origin, Vec3 horizontalMovement, float radius,
            CollisionWorld world, out Vec3 result)
        {
            result = origin;

            float stepHeight = GameConstants.MaxStepHeight;

            // 1. Sweep upward to check headroom
            var upHit = world.SweepSphere(origin, radius, Vec3.Up, stepHeight + SkinWidth);
            float upDist = upHit.Hit ? GameMath.Max(0f, upHit.Distance - SkinWidth) : stepHeight;

            if (upDist < MinMoveDistance)
                return false;

            // 2. Horizontal move from elevated position
            Vec3 elevated = origin + Vec3.Up * upDist;
            Vec3 afterSlide = SlideMove(elevated, horizontalMovement, radius, world);

            // 3. Sweep downward to find landing surface
            var downHit = world.SweepSphere(afterSlide, radius, Vec3.Down, stepHeight + SkinWidth);

            if (!downHit.Hit)
                return false;

            float slopeAngle = Vec3.Angle(downHit.Normal, Vec3.Up);
            if (slopeAngle > GameConstants.SlopeLimit)
                return false;

            float downDist = GameMath.Max(0f, downHit.Distance - SkinWidth);
            result = afterSlide - Vec3.Up * downDist;

            // Verify net horizontal progress
            Vec3 netMovement = result - origin;
            netMovement.y = 0f;
            if (netMovement.SqrMagnitude < MinMoveDistance * MinMoveDistance)
                return false;

            return true;
        }

        private static HitResult GroundCheck(Vec3 sphereOrigin, float radius, CollisionWorld world)
        {
            // Cast sphere downward from current position
            var hit = world.SweepSphere(sphereOrigin, radius, Vec3.Down, GroundCheckDistance + GroundOffset);

            if (hit.Hit)
            {
                // Check slope angle
                float slopeAngle = Vec3.Angle(hit.Normal, Vec3.Up);
                if (slopeAngle > GameConstants.SlopeLimit)
                {
                    return HitResult.None;
                }
            }

            return hit;
        }
    }
}
