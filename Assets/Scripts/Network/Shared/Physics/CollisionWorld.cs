// 碰撞世界
using System.Collections.Generic;
using System.IO;
using ShootingGame.Shared.Math;

namespace ShootingGame.Shared.Physics
{
    /// <summary>
    /// 保存静态碰撞几何体（AABB），提供扫描和射线检测查询
    /// </summary>
    public class CollisionWorld
    {
        private readonly List<AABB> _boxes = new List<AABB>();

        public int Count => _boxes.Count;

        public void AddBox(AABB box) => _boxes.Add(box);
        public void Clear() => _boxes.Clear();

        /// <summary>
        /// 扫描球体穿过世界，返回最近的命中
        /// </summary>
        public HitResult SweepSphere(Vec3 origin, float radius, Vec3 direction, float maxDistance)
        {
            HitResult closest = HitResult.None;
            float closestDist = maxDistance;

            for (int i = 0; i < _boxes.Count; i++)
            {
                var hit = Intersection.SweepSphereAABB(origin, radius, direction, _boxes[i], closestDist);
                if (hit.Hit && hit.Distance < closestDist)
                {
                    closest = hit;
                    closestDist = hit.Distance;
                }
            }

            return closest;
        }

        /// <summary>
        /// 射线检测，返回最近的命中
        /// </summary>
        public HitResult Raycast(Vec3 origin, Vec3 direction, float maxDistance)
        {
            Ray ray = new Ray(origin, direction);
            HitResult closest = HitResult.None;
            float closestDist = maxDistance;

            for (int i = 0; i < _boxes.Count; i++)
            {
                var hit = Intersection.RayAABB(ray, _boxes[i], closestDist);
                if (hit.Hit && hit.Distance < closestDist)
                {
                    closest = hit;
                    closestDist = hit.Distance;
                }
            }

            return closest;
        }

        /// <summary>
        /// 检查胶囊是否与世界中的任何AABB重叠
        /// </summary>
        public bool OverlapCapsule(Capsule capsule)
        {
            AABB capsuleAABB = capsule.BoundingBox();
            for (int i = 0; i < _boxes.Count; i++)
            {
                if (capsuleAABB.Overlaps(_boxes[i]))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 扫描胶囊体穿过世界，返回最近的命中。
        /// 将胶囊分解为底部球体和顶部球体，每个独立扫描后取最近命中。
        /// </summary>
        public HitResult SweepCapsule(Vec3 basePos, float height, float radius, Vec3 direction, float maxDistance)
        {
            HitResult closest = HitResult.None;
            float closestDist = maxDistance;

            Vec3 bottomCenter = basePos + Vec3.Up * radius;
            Vec3 topCenter = basePos + Vec3.Up * (height - radius);

            // Sweep bottom sphere
            var bottomHit = SweepSphere(bottomCenter, radius, direction, maxDistance);
            if (bottomHit.Hit && bottomHit.Distance < closestDist)
            {
                closest = bottomHit;
                closestDist = bottomHit.Distance;
            }

            // Sweep top sphere
            var topHit = SweepSphere(topCenter, radius, direction, maxDistance);
            if (topHit.Hit && topHit.Distance < closestDist)
            {
                closest = topHit;
                closestDist = topHit.Distance;
            }

            // Sweep the shaft (interpolated sphere centers)
            // Sample N points between bottom and top
            float shaftLength = height - 2f * radius;
            if (shaftLength > 0.01f)
            {
                int samples = 3;
                for (int i = 1; i <= samples; i++)
                {
                    float t = (float)i / (samples + 1);
                    Vec3 midCenter = bottomCenter + Vec3.Up * (shaftLength * t);
                    var midHit = SweepSphere(midCenter, radius, direction, maxDistance);
                    if (midHit.Hit && midHit.Distance < closestDist)
                    {
                        closest = midHit;
                        closestDist = midHit.Distance;
                    }
                }
            }

            return closest;
        }

        /// <summary>
        /// 从指定位置向下采样地面高度和法线。
        /// 用于爬坡、地面检测。
        /// </summary>
        /// <param name="position">采样起始位置</param>
        /// <param name="maxDistance">最大向下检测距离</param>
        /// <param name="groundPoint">输出：地面接触点</param>
        /// <param name="normal">输出：地面法线</param>
        /// <returns>是否检测到地面</returns>
        public bool SampleGround(Vec3 position, float maxDistance, out Vec3 groundPoint, out Vec3 normal)
        {
            groundPoint = position;
            normal = Vec3.Up;

            var hit = Raycast(position, Vec3.Down, maxDistance);
            if (hit.Hit)
            {
                float slopeAngle = Vec3.Angle(hit.Normal, Vec3.Up);
                if (slopeAngle <= PhysicsConstants.SlopeLimit)
                {
                    groundPoint = hit.Point;
                    normal = hit.Normal;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 检查球体是否与世界中任何 AABB 重叠。
        /// 用于手雷爆炸范围检测等。
        /// </summary>
        /// <param name="center">球心</param>
        /// <param name="radius">球半径</param>
        /// <returns>是否有重叠</returns>
        public bool OverlapSphere(Vec3 center, float radius)
        {
            // 对每个 AABB，检查最近点距离是否 < radius
            float radiusSq = radius * radius;
            for (int i = 0; i < _boxes.Count; i++)
            {
                Vec3 closest = _boxes[i].ClosestPoint(center);
                if (Vec3.SqrDistance(center, closest) < radiusSq)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 获取地面坡度角度（单位：度）。
        /// </summary>
        public static float GetSlopeAngle(Vec3 normal)
        {
            return Vec3.Angle(normal, Vec3.Up);
        }

        /// <summary>
        /// 保存碰撞数据到二进制文件
        /// </summary>
        public void Save(string path)
        {
            using (var fs = File.Create(path))
            using (var bw = new BinaryWriter(fs))
            {
                bw.Write(_boxes.Count);
                for (int i = 0; i < _boxes.Count; i++)
                {
                    bw.Write(_boxes[i].Min.x);
                    bw.Write(_boxes[i].Min.y);
                    bw.Write(_boxes[i].Min.z);
                    bw.Write(_boxes[i].Max.x);
                    bw.Write(_boxes[i].Max.y);
                    bw.Write(_boxes[i].Max.z);
                }
            }
        }

        /// <summary>
        /// 从二进制文件加载碰撞数据
        /// </summary>
        public static CollisionWorld Load(string path)
        {
            var world = new CollisionWorld();
            using (var fs = File.OpenRead(path))
            using (var br = new BinaryReader(fs))
            {
                int count = br.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    var min = new Vec3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                    var max = new Vec3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                    world.AddBox(new AABB(min, max));
                }
            }
            return world;
        }

        /// <summary>
        /// 从字节数组加载碰撞数据
        /// </summary>
        public static CollisionWorld LoadFromBytes(byte[] data)
        {
            var world = new CollisionWorld();
            using (var ms = new MemoryStream(data))
            using (var br = new BinaryReader(ms))
            {
                int count = br.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    var min = new Vec3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                    var max = new Vec3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                    world.AddBox(new AABB(min, max));
                }
            }
            return world;
        }
    }
}