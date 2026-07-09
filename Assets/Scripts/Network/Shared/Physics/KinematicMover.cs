// 运动学移动器
using ShootingGame.Shared.Math;

namespace ShootingGame.Shared.Physics
{
    public struct MoveResult
    {
        public Vec3 Position;
        public bool IsGrounded;
        public Vec3 GroundNormal;
    }

    /// <summary>
    /// 运动学角色移动器，带有滑动碰撞响应和地面检测
    /// </summary>
    public static class KinematicMover
    {
        private const int MaxSlideIterations = 3;
        private const float GroundCheckDistance = 0.08f; // 必须 < JumpForce*dt (≈0.134)，否则跳跃会被拉回地面
        private const float SkinWidth = 0.01f;
        private const float MinMoveDistance = 0.001f;
        private const float GroundOffset = 0.02f;

        /// <summary>
        /// 在碰撞世界中移动玩家
        /// </summary>
        public static MoveResult Move(Vec3 position, Vec3 movement, float radius, CollisionWorld world)
        {
            // 添加 SkinWidth 防止球心恰好落在 expanded AABB 边界上，
            // 导致 SweepSphere 返回 tmin=0 且 normal=Zero，使 SlideMove 死锁
            Vec3 sphereOrigin = position + Vec3.Up * (radius + GroundOffset + SkinWidth);

            // 预检测地面法线，用于斜面行走
            Vec3 groundNormal = Vec3.Up;
            var preGround = world.SweepSphere(sphereOrigin, radius, Vec3.Down, GroundCheckDistance + GroundOffset);
            if (preGround.Hit)
            {
                float slopeAngle = Vec3.Angle(preGround.Normal, Vec3.Up);
                if (slopeAngle <= PhysicsConstants.SlopeLimit)
                {
                    groundNormal = preGround.Normal;
                }
            }

            // 1. 水平移动（沿坡面投影 + 沿墙滑动）+ 跨步检测
            Vec3 horizontal = new Vec3(movement.x, 0f, movement.z);
            if (horizontal.SqrMagnitude > MinMoveDistance * MinMoveDistance)
            {
                // 将水平移动投影到地面平面上，使角色沿斜面行走
                Vec3 slopeMove = horizontal - groundNormal * Vec3.Dot(horizontal, groundNormal);

                Vec3 beforeSlide = sphereOrigin;
                sphereOrigin = SlideMove(sphereOrigin, slopeMove, radius, world);

                // 检测是否被阻挡，尝试跨步
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

            // 2. 垂直移动（重力/跳跃）
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

            Vec3 feetPos = sphereOrigin - Vec3.Up * (radius + GroundOffset);

            // 3. 地面检测
            var groundResult = GroundCheck(sphereOrigin, radius, world);

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

                float safeDistance = GameMath.Max(0f, hit.Distance - SkinWidth);
                origin = origin + dir * safeDistance;

                float remainingDist = dist - safeDistance;
                Vec3 remainingMovement = dir * remainingDist;

                remaining = remainingMovement - hit.Normal * Vec3.Dot(remainingMovement, hit.Normal);
            }

            return origin;
        }

        /// <summary>
        /// 尝试跨过低矮障碍物（台阶、路沿等）
        /// </summary>
        private static bool TryStepUp(Vec3 origin, Vec3 horizontalMovement, float radius,
            CollisionWorld world, out Vec3 result)
        {
            result = origin;

            float stepHeight = PhysicsConstants.MaxStepHeight;

            // 1. 向上 sweep 检查头顶空间
            var upHit = world.SweepSphere(origin, radius, Vec3.Up, stepHeight + SkinWidth);
            float upDist = upHit.Hit ? GameMath.Max(0f, upHit.Distance - SkinWidth) : stepHeight;

            if (upDist < MinMoveDistance)
                return false;

            // 2. 抬高后水平移动
            Vec3 elevated = origin + Vec3.Up * upDist;
            Vec3 afterSlide = SlideMove(elevated, horizontalMovement, radius, world);

            // 3. 向下 sweep 寻找落脚面
            var downHit = world.SweepSphere(afterSlide, radius, Vec3.Down, stepHeight + SkinWidth);

            if (!downHit.Hit)
                return false;

            float slopeAngle = Vec3.Angle(downHit.Normal, Vec3.Up);
            if (slopeAngle > PhysicsConstants.SlopeLimit)
                return false;

            float downDist = GameMath.Max(0f, downHit.Distance - SkinWidth);
            result = afterSlide - Vec3.Up * downDist;

            // 验证确实有水平位移
            Vec3 netMovement = result - origin;
            netMovement.y = 0f;
            if (netMovement.SqrMagnitude < MinMoveDistance * MinMoveDistance)
                return false;

            return true;
        }

        private static HitResult GroundCheck(Vec3 sphereOrigin, float radius, CollisionWorld world)
        {
            var hit = world.SweepSphere(sphereOrigin, radius, Vec3.Down, GroundCheckDistance + GroundOffset);

            if (hit.Hit)
            {
                float slopeAngle = Vec3.Angle(hit.Normal, Vec3.Up);
                if (slopeAngle > PhysicsConstants.SlopeLimit)
                {
                    return HitResult.None;
                }
            }

            return hit;
        }
    }
}