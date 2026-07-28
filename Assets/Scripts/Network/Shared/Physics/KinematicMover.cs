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
    /// 运动学角色移动器，带有滑动碰撞响应和地面检测。
    /// 移动前先做解穿透（Depenetrate），防止角色陷入重叠盒接缝后滑动死锁、误触发跨步上爬。
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
        /// 水平与垂直移动分离，避免横向移动时切入地面
        /// </summary>
        public static MoveResult Move(Vec3 position, Vec3 movement, float radius, CollisionWorld world)
        {
            // 球心 = 脚部 + 半径 + 离地间隙。
            // 注意：不要在这里加 SkinWidth——球心加多少就必须在转回脚部时减多少，
            // 否则滞空时每帧净增 SkinWidth 的高度（着地时靠 ground snap 抵消，滞空时无抵消），
            // 这正是"角色莫名向上移动"的根源之一。球心恰好落在扩展体边界导致的退化命中，
            // 由下面的 DepenetrateSphere 负责处理。
            Vec3 sphereOrigin = position + Vec3.Up * (radius + GroundOffset);

            // 解穿透：若球心已陷入碰撞体（重叠盒接缝/高速嵌入），先推出再移动。
            // 否则后续 sweep 会返回 tmin=0、法线为零的退化命中，
            // 导致滑动原地卡死 → 误触发跨步 → 角色沿地形逐级上爬
            sphereOrigin = world.DepenetrateSphere(sphereOrigin, radius);

            // 预检测地面法线，用于斜面行走；同时记录着地状态（跨步前提）
            Vec3 groundNormal = Vec3.Up;
            bool grounded = false;
            var preGround = world.SweepSphere(sphereOrigin, radius, Vec3.Down, GroundCheckDistance + GroundOffset);
            if (preGround.Hit)
            {
                float slopeAngle = Vec3.Angle(preGround.Normal, Vec3.Up);
                if (slopeAngle <= PhysicsConstants.SlopeLimit)
                {
                    groundNormal = preGround.Normal;
                    grounded = true;
                }
            }

            // 1. 水平移动（沿坡面投影 + 沿墙滑动）+ 跨步检测
            Vec3 horizontal = new Vec3(movement.x, 0f, movement.z);
            if (horizontal.SqrMagnitude > MinMoveDistance * MinMoveDistance)
            {
                // 将水平移动投影到地面平面上，使角色沿斜面行走
                Vec3 slopeMove = horizontal - groundNormal * Vec3.Dot(horizontal, groundNormal);

                Vec3 beforeSlide = sphereOrigin;
                sphereOrigin = SlideMove(sphereOrigin, slopeMove, radius, world, out bool blockedByWall);

                // 只有真正被墙（法线坡度超过 SlopeLimit）挡住且处于着地状态时才尝试跨步。
                // 不能用"实际水平位移 < 期望"作为条件：坡面投影天然变短会误触发，
                // 陷入重叠盒滑动卡死也会误触发——两者都会导致角色莫名向上移动
                if (blockedByWall && grounded)
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

            // 球心转回脚部位置
            Vec3 feetPos = sphereOrigin - Vec3.Up * (radius + GroundOffset);

            // 3. 地面检测
            var groundResult = GroundCheck(sphereOrigin, radius, world);

            // 着地时吸附到地面（消除 GroundOffset 间隙）。
            // 吸附时保留 SkinWidth 间隙，使球心不恰好停在扩展体边界上
            // （边界上的球心会让 sweep 返回 tmin=0/normal=Zero 的退化命中）
            if (groundResult.Hit && groundResult.Distance > SkinWidth)
            {
                float snap = groundResult.Distance - SkinWidth;
                feetPos = feetPos - Vec3.Up * snap;
                sphereOrigin = sphereOrigin - Vec3.Up * snap;
            }

            return new MoveResult
            {
                Position = feetPos,
                IsGrounded = groundResult.Hit,
                GroundNormal = groundResult.Hit ? groundResult.Normal : Vec3.Up
            };
        }

        /// <summary>
        /// 沿墙面滑动移动。blockedByWall：是否被"墙"（法线坡度超过 SlopeLimit 的面）阻挡。
        /// </summary>
        private static Vec3 SlideMove(Vec3 origin, Vec3 movement, float radius, CollisionWorld world, out bool blockedByWall)
        {
            blockedByWall = false;
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

                // 退化命中（法线为零）。Depenetrate 之后理论上不应出现；
                // 若仍出现则放弃本次剩余位移，防止投影到零法线导致原地死循环
                if (hit.Normal.SqrMagnitude < 0.5f)
                    break;

                // 记录是否被墙挡住（用于跨步判定）
                if (Vec3.Angle(hit.Normal, Vec3.Up) > PhysicsConstants.SlopeLimit)
                    blockedByWall = true;

                // 移动到接触点之前
                float safeDistance = GameMath.Max(0f, hit.Distance - SkinWidth);
                origin = origin + dir * safeDistance;

                // 计算剩余位移并投影到表面
                float remainingDist = dist - safeDistance;
                Vec3 remainingMovement = dir * remainingDist;

                // 滑动：去除沿命中法线方向的分量
                remaining = remainingMovement - hit.Normal * Vec3.Dot(remainingMovement, hit.Normal);
            }

            return origin;
        }

        /// <summary>
        /// 尝试跨过低矮障碍物（台阶、路沿等）：抬起 → 水平移动 → 落下
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
            Vec3 afterSlide = SlideMove(elevated, horizontalMovement, radius, world, out _);

            // 3. 向下 sweep 寻找落脚面
            var downHit = world.SweepSphere(afterSlide, radius, Vec3.Down, stepHeight + SkinWidth);

            if (!downHit.Hit)
                return false;

            float slopeAngle = Vec3.Angle(downHit.Normal, Vec3.Up);
            if (slopeAngle > PhysicsConstants.SlopeLimit)
                return false;

            float downDist = GameMath.Max(0f, downHit.Distance - SkinWidth);
            result = afterSlide - Vec3.Up * downDist;

            // 落点仍嵌入碰撞体则放弃（防止借助重叠盒逐级上爬）
            if (world.OverlapSphere(result, radius))
                return false;

            // 验证确实有水平位移
            Vec3 netMovement = result - origin;
            netMovement.y = 0f;
            if (netMovement.SqrMagnitude < MinMoveDistance * MinMoveDistance)
                return false;

            return true;
        }

        private static HitResult GroundCheck(Vec3 sphereOrigin, float radius, CollisionWorld world)
        {
            // 从当前位置向下扫描球体
            var hit = world.SweepSphere(sphereOrigin, radius, Vec3.Down, GroundCheckDistance + GroundOffset);

            if (hit.Hit)
            {
                // 检查坡度
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
