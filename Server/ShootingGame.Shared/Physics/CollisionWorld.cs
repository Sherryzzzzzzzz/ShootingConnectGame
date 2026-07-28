// 碰撞世界
using System.Collections.Generic;
using System.IO;
using ShootingGame.Shared.Math;

namespace ShootingGame.Shared.Physics
{
    /// <summary>
    /// 保存静态碰撞几何体（AABB），提供扫描和射线检测查询。服务器与客户端共用。
    /// 内置均匀网格空间索引，所有查询只检测查询范围覆盖的格子，避免 O(N) 线性扫描。
    /// 注意：非线程安全（内部复用候选缓冲区），仅限模拟线程使用。
    /// </summary>
    public class CollisionWorld
    {
        private readonly List<AABB> _boxes = new List<AABB>();

        // ---- 均匀网格空间索引 ----
        private const float CellSize = 4f;
        private const float CellInv = 1f / CellSize;
        private const float QueryEpsilon = 0.01f;
        private Dictionary<long, List<int>> _grid;
        private bool _gridBuilt;
        // 跨越格子数过多的大盒（如保底地面）不进网格，每次查询直接全测
        private readonly List<int> _bigBoxes = new List<int>();
        private const int BigBoxCellThreshold = 64;
        private int[] _stamp;       // 候选盒去重用的代际标记，避免每帧分配 HashSet
        private int _stampGen;
        private readonly List<int> _scratch = new List<int>(256);

        public int Count => _boxes.Count;

        /// <summary>测试/调试用：只读访问盒子列表</summary>
        public IReadOnlyList<AABB> Boxes => _boxes;

        public void AddBox(AABB box)
        {
            _boxes.Add(box);
            _gridBuilt = false;
        }

        public void Clear()
        {
            _boxes.Clear();
            _grid?.Clear();
            _bigBoxes.Clear();
            _gridBuilt = false;
        }

        /// <summary>
        /// 扫描球体穿过世界，返回最近的命中
        /// </summary>
        public HitResult SweepSphere(Vec3 origin, float radius, Vec3 direction, float maxDistance)
        {
            HitResult closest = HitResult.None;
            float closestDist = maxDistance;

            Vec3 end = origin + direction * maxDistance;
            AABB query = SegmentQuery(origin, end, radius);

            var candidates = GatherCandidates(query);
            for (int i = 0; i < candidates.Count; i++)
            {
                var hit = Intersection.SweepSphereAABB(origin, radius, direction, _boxes[candidates[i]], closestDist);
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

            Vec3 end = origin + direction * maxDistance;
            AABB query = SegmentQuery(origin, end, 0f);

            var candidates = GatherCandidates(query);
            for (int i = 0; i < candidates.Count; i++)
            {
                var hit = Intersection.RayAABB(ray, _boxes[candidates[i]], closestDist);
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
        /// 使用胶囊的包围盒做快速粗略检查
        /// </summary>
        public bool OverlapCapsule(Capsule capsule)
        {
            AABB capsuleAABB = capsule.BoundingBox();
            var candidates = GatherCandidates(capsuleAABB);
            for (int i = 0; i < candidates.Count; i++)
            {
                if (capsuleAABB.Overlaps(_boxes[candidates[i]]))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 球是否与任何盒子重叠（Minkowski 和模型：球心在盒子外扩 radius 的体内即为重叠）
        /// </summary>
        public bool OverlapSphere(Vec3 center, float radius)
        {
            Vec3 r = new Vec3(radius, radius, radius);
            AABB query = new AABB(center - r, center + r);
            var candidates = GatherCandidates(query);
            for (int i = 0; i < candidates.Count; i++)
            {
                if (_boxes[candidates[i]].Expand(radius).Contains(center))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 解穿透：若球心陷入某些盒子的扩展体（Minkowski 和）内，
        /// 每轮找出嵌入最深的盒子并沿最小穿透轴推出，直到无重叠或达到迭代上限。
        /// 用于消除"卡在重叠盒接缝里 → sweep 返回 tmin=0/normal=Zero → 滑动死锁"的问题。
        /// </summary>
        public Vec3 DepenetrateSphere(Vec3 center, float radius, int maxIterations = 4)
        {
            const float PushEpsilon = 0.002f;

            for (int iter = 0; iter < maxIterations; iter++)
            {
                Vec3 r = new Vec3(radius, radius, radius);
                AABB query = new AABB(center - r, center + r);
                var candidates = GatherCandidates(query);

                Vec3 bestPush = Vec3.Zero;
                float deepest = 0f;
                bool found = false;

                for (int i = 0; i < candidates.Count; i++)
                {
                    AABB expanded = _boxes[candidates[i]].Expand(radius);
                    if (!expanded.Contains(center))
                        continue;

                    // 六面穿透深度，取最小者作为推出方向
                    float pxNeg = center.x - expanded.Min.x, pxPos = expanded.Max.x - center.x;
                    float pyNeg = center.y - expanded.Min.y, pyPos = expanded.Max.y - center.y;
                    float pzNeg = center.z - expanded.Min.z, pzPos = expanded.Max.z - center.z;

                    float minPen = pxNeg;
                    Vec3 push = new Vec3(-pxNeg, 0f, 0f);
                    if (pxPos < minPen) { minPen = pxPos; push = new Vec3(pxPos, 0f, 0f); }
                    if (pyNeg < minPen) { minPen = pyNeg; push = new Vec3(0f, -pyNeg, 0f); }
                    if (pyPos < minPen) { minPen = pyPos; push = new Vec3(0f, pyPos, 0f); }
                    if (pzNeg < minPen) { minPen = pzNeg; push = new Vec3(0f, 0f, -pzNeg); }
                    if (pzPos < minPen) { minPen = pzPos; push = new Vec3(0f, 0f, pzPos); }

                    // 忽略 ≤SkinWidth 的微穿透（球体"接触"表面而非"嵌入"）
                    if (minPen <= 0.01f) continue;

                    if (!found || minPen > deepest)
                    {
                        deepest = minPen;
                        bestPush = push;
                        found = true;
                    }
                }

                if (!found)
                    break;

                // 沿推出方向多加一点 epsilon，避免下一轮仍因浮点误差判定为重叠
                float pushLen = bestPush.Magnitude;
                if (pushLen < 1e-6f)
                    break;
                center += bestPush * (1f + PushEpsilon / pushLen);
            }

            return center;
        }

        // ================= 空间索引内部实现 =================

        private static AABB SegmentQuery(Vec3 a, Vec3 b, float expand)
        {
            float e = expand + QueryEpsilon;
            return new AABB(
                new Vec3(GameMath.Min(a.x, b.x) - e, GameMath.Min(a.y, b.y) - e, GameMath.Min(a.z, b.z) - e),
                new Vec3(GameMath.Max(a.x, b.x) + e, GameMath.Max(a.y, b.y) + e, GameMath.Max(a.z, b.z) + e));
        }

        private static int CellIndex(float v) => (int)System.Math.Floor(v * CellInv);

        // 21 位打包，支持 ±1048575 格（±4M 米），足够覆盖任何地图
        private static long CellKey(int x, int y, int z)
        {
            return (((long)x & 0x1FFFFF) << 42) | (((long)y & 0x1FFFFF) << 21) | ((long)z & 0x1FFFFF);
        }

        private void EnsureGrid()
        {
            if (_gridBuilt)
                return;

            if (_grid == null)
                _grid = new Dictionary<long, List<int>>(1024);
            else
                _grid.Clear();

            if (_stamp == null || _stamp.Length < _boxes.Count)
                _stamp = new int[_boxes.Count + 64];

            _bigBoxes.Clear();

            for (int i = 0; i < _boxes.Count; i++)
            {
                AABB b = _boxes[i];
                int x0 = CellIndex(b.Min.x), x1 = CellIndex(b.Max.x);
                int y0 = CellIndex(b.Min.y), y1 = CellIndex(b.Max.y);
                int z0 = CellIndex(b.Min.z), z1 = CellIndex(b.Max.z);

                long cellCount = (long)(x1 - x0 + 1) * (y1 - y0 + 1) * (z1 - z0 + 1);
                if (cellCount > BigBoxCellThreshold)
                {
                    _bigBoxes.Add(i);
                    continue;
                }

                for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++)
                for (int z = z0; z <= z1; z++)
                {
                    long key = CellKey(x, y, z);
                    if (!_grid.TryGetValue(key, out var list))
                    {
                        list = new List<int>(4);
                        _grid[key] = list;
                    }
                    list.Add(i);
                }
            }

            _gridBuilt = true;
        }

        /// <summary>
        /// 收集查询 AABB 覆盖的候选盒索引（升序，保证与线性扫描相同的判定顺序，确保确定性）。
        /// 复用内部缓冲区，返回的列表在下一次查询前有效。
        /// </summary>
        private List<int> GatherCandidates(AABB query)
        {
            EnsureGrid();

            _scratch.Clear();
            _stampGen++;

            // 大盒总是候选（固定在前，顺序稳定）
            for (int i = 0; i < _bigBoxes.Count; i++)
            {
                int idx = _bigBoxes[i];
                _stamp[idx] = _stampGen;
                _scratch.Add(idx);
            }

            int x0 = CellIndex(query.Min.x), x1 = CellIndex(query.Max.x);
            int y0 = CellIndex(query.Min.y), y1 = CellIndex(query.Max.y);
            int z0 = CellIndex(query.Min.z), z1 = CellIndex(query.Max.z);

            for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
            for (int z = z0; z <= z1; z++)
            {
                if (!_grid.TryGetValue(CellKey(x, y, z), out var list))
                    continue;

                for (int i = 0; i < list.Count; i++)
                {
                    int idx = list[i];
                    if (_stamp[idx] != _stampGen)
                    {
                        _stamp[idx] = _stampGen;
                        _scratch.Add(idx);
                    }
                }
            }

            _scratch.Sort();
            return _scratch;
        }

        // ================= 序列化 =================

        /// <summary>
        /// 保存碰撞数据到二进制文件
        /// 格式: [int32 count] [AABB * count: 每个 6 floats (minX,minY,minZ,maxX,maxY,maxZ)]
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
        /// 从字节数组加载碰撞数据（用于 Unity 等文件 IO 不同的环境）
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
