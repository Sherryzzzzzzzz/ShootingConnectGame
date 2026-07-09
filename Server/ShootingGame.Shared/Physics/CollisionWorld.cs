using System.Collections.Generic;
using System.IO;
using ShootingGame.Shared.Math;

namespace ShootingGame.Shared.Physics
{
    /// <summary>
    /// Holds static collision geometry (AABBs). Provides sweep and raycast queries.
    /// Used by both server and client.
    /// </summary>
    public class CollisionWorld
    {
        private readonly List<AABB> _boxes = new List<AABB>();

        public int Count => _boxes.Count;

        public void AddBox(AABB box) => _boxes.Add(box);
        public void Clear() => _boxes.Clear();

        /// <summary>
        /// Sweep a sphere through the world, returning the closest hit.
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
        /// Cast a ray through the world, returning the closest hit.
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
        /// Check if a capsule overlaps any AABB in the world.
        /// Uses the capsule's bounding box for a fast broad check.
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
        /// Save collision data to binary file.
        /// Format: [int32 count] [AABB * count: 6 floats each (minX,minY,minZ,maxX,maxY,maxZ)]
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
        /// Load collision data from binary file.
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
        /// Load collision data from a byte array (for Unity where file IO may differ).
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
