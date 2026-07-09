using ShootingGame.Shared.Math;

namespace ShootingGame.Shared.Physics
{
    public static class Intersection
    {
        /// <summary>
        /// Ray vs AABB intersection using the slab method.
        /// Returns hit with distance and normal of the entry face.
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
                // Ray is parallel to slab — check if origin is within slab
                if (origin < min || origin > max) return false;
                return true;
            }

            float invD = 1f / direction;
            float t1 = (min - origin) * invD;
            float t2 = (max - origin) * invD;

            Vec3 n = negNormal;
            if (t1 > t2)
            {
                // Swap
                float tmp = t1; t1 = t2; t2 = tmp;
                n = posNormal;
            }

            if (t1 > tmin) { tmin = t1; normal = n; }
            if (t2 < tmax) tmax = t2;

            if (tmin > tmax) return false;
            return true;
        }

        /// <summary>
        /// Ray vs Capsule intersection (v1: expanded-AABB approximation).
        /// The capsule is treated as an AABB expanded by the capsule radius.
        /// This is less accurate than true ray-capsule but sufficient for hitscan v1.
        /// </summary>
        public static HitResult RayCapsule(Ray ray, Capsule capsule, float maxDistance)
        {
            // Build AABB from capsule bounds, then test
            AABB bounds = capsule.BoundingBox();
            return RayAABB(ray, bounds, maxDistance);
        }

        /// <summary>
        /// Sweep a sphere against an AABB (Minkowski sum approach).
        /// Expand the AABB by the sphere radius, then ray-test from sphere center.
        /// </summary>
        public static HitResult SweepSphereAABB(Vec3 sphereCenter, float radius, Vec3 direction, AABB box, float maxDistance)
        {
            AABB expanded = box.Expand(radius);
            Ray ray = new Ray(sphereCenter, direction);
            var hit = RayAABB(ray, expanded, maxDistance);

            if (hit.Hit)
            {
                // Adjust the hit point: it's on the expanded box, move it back to the actual surface
                // The normal is correct from the expanded box
                hit.Point = hit.Point - hit.Normal * radius;
            }

            return hit;
        }

        /// <summary>
        /// Compute the shortest distance from a point to a line segment.
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
        /// Compute the distance from a point to a capsule (ellipsoid).
        /// Negative value means the point is inside the capsule.
        /// Capsule = segment (cap.Bottom → cap.Top) with radius cap.Radius.
        /// </summary>
        public static float DistancePointToCapsule(Vec3 point, Capsule cap)
        {
            float distToSegment = DistancePointToSegment(point, cap.Bottom, cap.Top);
            return distToSegment - cap.Radius;
        }

        /// <summary>
        /// Check if a point is inside a capsule (for hit detection).
        /// </summary>
        public static bool PointInCapsule(Vec3 point, Capsule cap)
        {
            return DistancePointToCapsule(point, cap) <= 0f;
        }

        /// <summary>
        /// Compute the shortest distance between two line segments (Lumelsky algorithm).
        /// Used for bullet-path vs capsule-axis swept collision.
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
        /// Shortest distance from a bullet movement segment to a capsule surface.
        /// Negative value means the segment penetrates the capsule → hit.
        /// </summary>
        public static float DistanceSegmentToCapsule(Vec3 segStart, Vec3 segEnd, Capsule cap, float bulletRadius)
        {
            float distToAxis = DistanceSegmentToSegment(segStart, segEnd, cap.Bottom, cap.Top);
            return distToAxis - cap.Radius - bulletRadius;
        }

        /// <summary>
        /// Swept bullet-path vs capsule intersection test.
        /// Commercial-grade collision: sweeps the bullet's full movement segment
        /// against the player's hit capsule to prevent tunneling.
        /// </summary>
        public static bool SweepBulletHitCapsule(Vec3 prevPos, Vec3 currentPos, Capsule cap, float bulletRadius)
        {
            return DistanceSegmentToCapsule(prevPos, currentPos, cap, bulletRadius) <= 0f;
        }
    }
}
