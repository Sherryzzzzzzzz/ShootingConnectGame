// 相交检测
using ShootingGame.Shared.Math;

namespace ShootingGame.Shared.Physics
{
    public static class Intersection
    {
        /// <summary>
        /// 使用slab方法的射线与AABB相交检测
        /// 返回包含距离和入口面法线的命中结果
        /// </summary>
        public static HitResult RayAABB(Ray ray, AABB box, float maxDistance)
        {
            float tmin = 0f;
            float tmax = maxDistance;
            Vec3 normal = Vec3.Zero;

            // X slab
            if (!SlabTest(ray.Origin.x, ray.Direction.x, box.Min.x, box.Max.x,
                          Vec3.Left, Vec3.Right, ref tmin, ref tmax, ref normal))
                return HitResult.None;

            // Y slab
            if (!SlabTest(ray.Origin.y, ray.Direction.y, box.Min.y, box.Max.y,
                          Vec3.Down, Vec3.Up, ref tmin, ref tmax, ref normal))
                return HitResult.None;

            // Z slab
            if (!SlabTest(ray.Origin.z, ray.Direction.z, box.Min.z, box.Max.z,
                          Vec3.Back, Vec3.Forward, ref tmin, ref tmax, ref normal))
                return HitResult.None;

            if (tmin < 0f) tmin = 0f;

            return new HitResult
            {
                Hit = true,
                Distance = tmin,
                Point = ray.GetPoint(tmin),
                Normal = normal
            };
        }

        private static bool SlabTest(float origin, float direction, float min, float max,
                                      Vec3 negNormal, Vec3 posNormal,
                                      ref float tmin, ref float tmax, ref Vec3 normal)
        {
            if (GameMath.Abs(direction) < GameMath.Epsilon)
            {
                // 射线平行于slab - 检查原点是否在slab内
                if (origin < min || origin > max) return false;
                return true;
            }

            float invD = 1f / direction;
            float t1 = (min - origin) * invD;
            float t2 = (max - origin) * invD;

            Vec3 n = negNormal;
            if (t1 > t2)
            {
                float tmp = t1; t1 = t2; t2 = tmp;
                n = posNormal;
            }

            if (t1 > tmin) { tmin = t1; normal = n; }
            if (t2 < tmax) tmax = t2;

            if (tmin > tmax) return false;
            return true;
        }

        /// <summary>
        /// 射线与胶囊相交检测（使用扩展AABB近似）
        /// </summary>
        public static HitResult RayCapsule(Ray ray, Capsule capsule, float maxDistance)
        {
            AABB bounds = capsule.BoundingBox();
            return RayAABB(ray, bounds, maxDistance);
        }

        /// <summary>
        /// 球体与AABB的扫描检测（Minkowski和方法）
        /// </summary>
        public static HitResult SweepSphereAABB(Vec3 sphereCenter, float radius, Vec3 direction, AABB box, float maxDistance)
        {
            AABB expanded = box.Expand(radius);
            Ray ray = new Ray(sphereCenter, direction);
            var hit = RayAABB(ray, expanded, maxDistance);

            if (hit.Hit)
            {
                hit.Point = hit.Point - hit.Normal * radius;
            }

            return hit;
        }

        /// <summary>
        /// 计算点到线段的最短距离
        /// </summary>
        public static float DistancePointToSegment(Vec3 point, Vec3 segA, Vec3 segB)
        {
            Vec3 ab = segB - segA;
            float abLenSqr = ab.SqrMagnitude;
            if (abLenSqr < 0.0001f)
                return Vec3.Distance(point, segA);

            float t = Vec3.Dot(point - segA, ab) / abLenSqr;
            t = GameMath.Clamp01(t);
            Vec3 closest = segA + ab * t;
            return Vec3.Distance(point, closest);
        }

        /// <summary>
        /// 计算点到胶囊体（椭球形）的距离。负值表示点在胶囊体内部。
        /// 胶囊体 = 线段 (cap.Bottom → cap.Top) + 半径 cap.Radius
        /// </summary>
        public static float DistancePointToCapsule(Vec3 point, Capsule cap)
        {
            float distToSegment = DistancePointToSegment(point, cap.Bottom, cap.Top);
            return distToSegment - cap.Radius;
        }

        /// <summary>
        /// 点是否在胶囊体内部（用于命中判定）
        /// </summary>
        public static bool PointInCapsule(Vec3 point, Capsule cap)
        {
            return DistancePointToCapsule(point, cap) <= 0f;
        }

        /// <summary>
        /// 计算两条线段之间的最短距离（Lumelsky 算法）
        /// 用于子弹路径(segStart→segEnd)与胶囊体轴线的碰撞检测
        /// </summary>
        public static float DistanceSegmentToSegment(Vec3 a0, Vec3 a1, Vec3 b0, Vec3 b1)
        {
            Vec3 d1 = a1 - a0;
            Vec3 d2 = b1 - b0;
            Vec3 r = a0 - b0;
            float a = Vec3.Dot(d1, d1);
            float e = Vec3.Dot(d2, d2);
            float f = Vec3.Dot(d2, r);

            float s, t;

            if (a <= GameMath.Epsilon && e <= GameMath.Epsilon)
                return Vec3.Distance(a0, b0);

            if (a <= GameMath.Epsilon)
            {
                s = 0f;
                t = GameMath.Clamp01(f / e);
            }
            else
            {
                float c = Vec3.Dot(d1, r);
                if (e <= GameMath.Epsilon)
                {
                    t = 0f;
                    s = GameMath.Clamp01(-c / a);
                }
                else
                {
                    float b = Vec3.Dot(d1, d2);
                    float denom = a * e - b * b;

                    if (GameMath.Abs(denom) > GameMath.Epsilon)
                        s = GameMath.Clamp01((b * f - c * e) / denom);
                    else
                        s = 0f;

                    t = (b * s + f) / e;

                    if (t < 0f)
                    {
                        t = 0f;
                        s = GameMath.Clamp01(-c / a);
                    }
                    else if (t > 1f)
                    {
                        t = 1f;
                        s = GameMath.Clamp01((b - c) / a);
                    }
                }
            }

            Vec3 closestA = a0 + d1 * s;
            Vec3 closestB = b0 + d2 * t;
            return Vec3.Distance(closestA, closestB);
        }

        /// <summary>
        /// 子弹移动路径与胶囊体的最短距离（扫描碰撞检测核心）
        /// 负值表示路径穿过胶囊体 → 命中
        /// </summary>
        public static float DistanceSegmentToCapsule(Vec3 segStart, Vec3 segEnd, Capsule cap, float bulletRadius)
        {
            float distToAxis = DistanceSegmentToSegment(segStart, segEnd, cap.Bottom, cap.Top);
            return distToAxis - cap.Radius - bulletRadius;
        }

        /// <summary>
        /// 子弹路径是否与胶囊体相交（商用级扫描碰撞）
        /// </summary>
        public static bool SweepBulletHitCapsule(Vec3 prevPos, Vec3 currentPos, Capsule cap, float bulletRadius)
        {
            return DistanceSegmentToCapsule(prevPos, currentPos, cap, bulletRadius) <= 0f;
        }
    }
}