using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using ShootingGame.Shared.Physics;
using ShootingGame.Shared.Math;

/// <summary>
/// 从场景 Collider 生成 CollisionWorld 二进制数据（AABB）。
///
/// 复杂几何处理策略：
/// - 未旋转 BoxCollider：直接导出世界 AABB（精确）。
/// - 旋转 BoxCollider：按 OBB 体素细分为小 AABB（保守近似）。
/// - MeshCollider：按三角形覆盖做体素化细分（shrink-wrap），避免单个巨大 AABB
///   把角色包进去导致 sweep 退化、跨步上爬。网格不可读时退化为物理查询体素化。
/// 导出后做共面相邻盒合并（保体积），消除体素接缝、减少盒子数量。
///
/// 菜单: 游戏/碰撞/...
/// </summary>
public class CollisionDataExporter : EditorWindow
{
    // MeshCollider 体素分辨率（米）。越小越贴合，盒子数越多
    private const float MeshVoxelSize = 0.5f;
    // 大型碰撞体的最大体素尺寸（按最大边长/50 自适应放大，上限 2m）
    private const float MeshVoxelSizeMax = 2.0f;

    /// <summary>按碰撞体尺寸自适应体素分辨率：大件用大格，小件保持精细</summary>
    private static float AdaptiveCellSize(Bounds b, float minCell)
    {
        float maxDim = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        return Mathf.Clamp(maxDim / 50f, minCell, MeshVoxelSizeMax);
    }
    // 旋转 BoxCollider 细分分辨率（米）
    private const float BoxVoxelSize = 0.5f;
    // 导出盒子数安全上限
    private const int MaxExportBoxes = 800000;
    // 单个碰撞体的体素数预算（超限自动放大体素重试）
    private const int MaxBoxesPerCollider = 30000;
    // 体素网格对齐原点（世界原点），保证不同 Collider 的体素对齐可合并
    private const float GridAlignEpsilon = 0.001f;

    [MenuItem("游戏/碰撞/导出场景碰撞数据 (collision.bin)", false, 200)]
    public static void ExportCollisionData()
    {
        ExportCollisionDataForOpenScene();
    }

    /// <summary>
    /// 批处理入口：先打开指定场景再导出。
    /// 用法: Unity.exe -batchmode -executeMethod CollisionDataExporter.ExportFightScene
    /// </summary>
    public static void ExportFightScene()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Fight.unity");
        ExportCollisionDataForOpenScene();
    }

    public static void ExportCollisionDataForOpenScene()
    {
        var allColliders = Object.FindObjectsOfType<Collider>(true);
        if (allColliders.Length == 0)
        {
            Debug.LogWarning("[CollisionExport] 场景中没有 Collider！");
            return;
        }

        var boxList = new List<AABB>();
        int processed = 0;
        int skippedTrigger = 0;
        int skippedPlayerLayer = 0;
        int skippedTiny = 0;
        int skippedFlat = 0;
        int voxelizedMesh = 0;
        int voxelizedBox = 0;
        int fallbackAABB = 0;

        int playerLayer = LayerMask.NameToLayer("Ignore Raycast");

        foreach (var col in allColliders)
        {
            // 跳过触发器
            if (col.isTrigger) { skippedTrigger++; continue; }
            // 跳过玩家层
            if (col.gameObject.layer == playerLayer) { skippedPlayerLayer++; continue; }
            // 跳过体积太小的（排除薄地面——Y 极薄但 XZ 面积大是合法平面，不能跳过）
            Bounds b = col.bounds;
            float volume = b.size.x * b.size.y * b.size.z;
            float xzArea = b.size.x * b.size.z;
            bool isFlatGround = b.size.y < 0.15f && xzArea > 10f;
            if (!isFlatGround && volume < 0.001f) { skippedTiny++; continue; }

            // 之前跳过薄地面片是因为单个大 AABB 会导致角色在里面卡死。
            // 现在有了体素化，薄片会被表面采样为多层小盒，保持精确，不再跳过。

            var meshCol = col as MeshCollider;
            var boxCol = col as BoxCollider;

            if (meshCol != null)
            {
                // 单碰撞体体素预算：超限则逐级放大体素重试（最多 4 倍）
                int added = 0;
                float tryCell = AdaptiveCellSize(b, MeshVoxelSize);
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    int before = boxList.Count;
                    added = VoxelizeMesh(meshCol, tryCell, boxList);
                    if (added > 0 && added <= MaxBoxesPerCollider) break;
                    // 超限或失败：回滚并重试
                    boxList.RemoveRange(before, boxList.Count - before);
                    added = 0;
                    tryCell *= 2f;
                }
                if (added > 0) { voxelizedMesh++; }
                else
                {
                    // 网格不可读等失败情况：退化为物理查询体素化
                    added = VoxelizeWithPhysics(col, b, tryCell, boxList);
                    if (added > 0) { voxelizedMesh++; Debug.LogWarning($"[CollisionExport] {col.name} 网格不可读，已用物理查询体素化（较慢）"); }
                    else { AddBounds(boxList, b); fallbackAABB++; Debug.LogWarning($"[CollisionExport] {col.name} 体素化失败，退化为单个 AABB"); }
                }
            }
            else if (boxCol != null && !IsAxisAligned(boxCol.transform))
            {
                // 旋转 BoxCollider：单个世界 AABB 会过度近似，按 OBB 细分
                int added = VoxelizeRotatedBox(boxCol, b, boxList);
                if (added > 0) { voxelizedBox++; }
                else { AddBounds(boxList, b); fallbackAABB++; }
            }
            else
            {
                // 轴对齐 Box / Sphere / Capsule 等：直接用世界 AABB
                AddBounds(boxList, b);
            }

            if ((processed++ % 25) == 0)
                Debug.Log($"[CollisionExport] 进度 {processed}/{allColliders.Length}, 当前盒子数 {boxList.Count}");

            if (boxList.Count > MaxExportBoxes)
            {
                Debug.LogError($"[CollisionExport] 盒子数超过上限 {MaxExportBoxes}，已中止(处理到 {processed}/{allColliders.Length})。请调大体素分辨率。");
                return;
            }
        }

        int beforeMerge = boxList.Count;

        // 共面相邻盒合并（保体积）：消除体素接缝，减少盒子数量
        boxList = FaceMerge(boxList);

        // 注意：不要对碰撞盒做整体膨胀！
        // 1) 膨胀会使相邻盒过度重叠，角色 Depenetrate 时被推出表面→落回→推出→震荡→不能移动
        // 2) 子弹穿透靠子弹的 Swept-sphere 碰撞（sweep 整段路径）解决，不靠盒子厚度
        // 3) 脚碰不到地是因为 KinematicMover 的 rest 间隙（SkinWidth=0.01），视觉误差 <1cm，不影响移动

        int valid = boxList.Count;
        Debug.Log($"[CollisionExport] 总 {allColliders.Length} Collider:");
        Debug.Log($"  体素化(Mesh): {voxelizedMesh}  体素化(旋转Box): {voxelizedBox}  退化AABB: {fallbackAABB}");
        Debug.Log($"  合并前: {beforeMerge} → 合并后: {valid}");
        Debug.Log($"  跳过(触发器): {skippedTrigger}");
        Debug.Log($"  跳过(玩家层): {skippedPlayerLayer}");
        Debug.Log($"  跳过(微小):   {skippedTiny}");
        Debug.Log($"  跳过(薄地面): {skippedFlat}");

        if (boxList.Count == 0)
        {
            Debug.LogWarning("[CollisionExport] 无有效碰撞体！");
            return;
        }

        // 保底地面：y=0 处铺一层薄地板，防体素化遗漏导致掉虚空
        boxList.Add(new AABB(new Vec3(-500, -0.1f, -500), new Vec3(500, 0.1f, 500)));

        var world = new CollisionWorld();
        foreach (var box in boxList)
            world.AddBox(box);

        // 客户端
        string streamingPath = Path.Combine(Application.streamingAssetsPath, "collision.bin");
        Directory.CreateDirectory(Application.streamingAssetsPath);
        world.Save(streamingPath);
        Debug.Log($"[CollisionExport] ✅ 客户端: {streamingPath} ({world.Count} boxes)");

        // 服务端
        string projectRoot = Path.GetDirectoryName(Application.dataPath);
        string serverPath = Path.Combine(projectRoot, "Server", "collision.bin");
        world.Save(serverPath);
        Debug.Log($"[CollisionExport] ✅ 服务端: {serverPath} ({world.Count} boxes)");

        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("碰撞导出完成",
            $"已导出 {world.Count} 个 AABB（合并前 {beforeMerge}）\n\n" +
            $"客户端: StreamingAssets/collision.bin\n" +
            $"服务端: Server/collision.bin\n\n" +
            $"体素化 Mesh: {voxelizedMesh}  体素化旋转Box: {voxelizedBox}  退化AABB: {fallbackAABB}\n" +
            $"跳过: 触发器{skippedTrigger} 玩家层{skippedPlayerLayer} 微小{skippedTiny} 薄地{skippedFlat}",
            "OK");
    }

    // ================= 体素化 =================

    private static void AddBounds(List<AABB> output, Bounds b)
    {
        output.Add(new AABB(
            new Vec3(b.min.x, b.min.y, b.min.z),
            new Vec3(b.max.x, b.max.y, b.max.z)));
    }

    private static bool IsAxisAligned(Transform t)
    {
        Quaternion q = t.rotation;
        // 与单位四元数或 180° 轴翻转对齐的都视为轴对齐（180° 翻转的 AABB 仍然精确）
        Vector3 axis;
        float angle;
        q.ToAngleAxis(out angle, out axis);
        if (angle < 0.5f) return true;
        // 90° 倍数绕主轴旋转，AABB 同样精确
        float a = Mathf.Abs(angle);
        bool rightAngle = Mathf.Abs(a - 90f) < 0.5f || Mathf.Abs(a - 180f) < 0.5f || Mathf.Abs(a - 270f) < 0.5f;
        if (!rightAngle) return false;
        axis.Normalize();
        bool mainAxis = Mathf.Abs(Mathf.Abs(axis.x) - 1f) < 0.01f
                     || Mathf.Abs(Mathf.Abs(axis.y) - 1f) < 0.01f
                     || Mathf.Abs(Mathf.Abs(axis.z) - 1f) < 0.01f;
        return mainAxis;
    }

    /// <summary>体素累加器：shrink-wrap，记录每个体素内几何的紧致包围盒</summary>
    private class CellAcc
    {
        public Vector3 Min;
        public Vector3 Max;
    }

    private static long VoxelKey(int x, int y, int z)
    {
        return (((long)x & 0x1FFFFF) << 42) | (((long)y & 0x1FFFFF) << 21) | ((long)z & 0x1FFFFF);
    }

    private static int VoxelIndex(float v, float cell) => (int)System.Math.Floor(v / cell);

    /// <summary>
    /// MeshCollider 三角形覆盖体素化：三角形 AABB 覆盖到的体素标记为实心，
    /// 并将体素收缩（shrink-wrap）到内部几何的紧致范围，减少表面误差。
    /// 网格不可读时返回 0。
    /// </summary>
    private static int VoxelizeMesh(MeshCollider meshCol, float cell, List<AABB> output)
    {
        Mesh mesh = meshCol.sharedMesh;
        if (mesh == null || !mesh.isReadable)
            return 0;

        Vector3[] verts;
        int[] tris;
        try
        {
            verts = mesh.vertices;
            tris = mesh.triangles;
        }
        catch
        {
            return 0;
        }
        if (verts.Length == 0 || tris.Length == 0)
            return 0;

        Transform t = meshCol.transform;
        var cells = new Dictionary<long, CellAcc>(4096);

        // 表面采样体素化：按三角形面积在表面均匀布点（间距≈cell/2），
        // 标记采样点所在体素。避免"三角形 AABB 覆盖法"对细长三角形
        // （电缆/栏杆，AABB 横跨几十米）标记出大量假实心体素。
        for (int i = 0; i < tris.Length; i += 3)
        {
            Vector3 w0 = t.TransformPoint(verts[tris[i]]);
            Vector3 w1 = t.TransformPoint(verts[tris[i + 1]]);
            Vector3 w2 = t.TransformPoint(verts[tris[i + 2]]);

            Vector3 e1 = w1 - w0, e2 = w2 - w0;
            float area = Vector3.Cross(e1, e2).magnitude * 0.5f;
            // 采样数：每个 cell²/4 面积一个点，三角形至少 3 点（顶点），上限防巨三角
            int samples = Mathf.Clamp(Mathf.CeilToInt(area / (cell * cell * 0.25f)), 3, 4096);
            int n = Mathf.CeilToInt(Mathf.Sqrt(samples));

            for (int a = 0; a <= n; a++)
            for (int b = 0; b <= n - a; b++)
            {
                // 重心格点（偏移 1/3 避免落在边上）
                float u = (a + 0.333f) / (n + 1f);
                float v = (b + 0.333f) / (n + 1f);
                if (u + v > 1f) continue;
                Vector3 p = w0 + e1 * u + e2 * v;

                int cx = VoxelIndex(p.x, cell), cy = VoxelIndex(p.y, cell), cz = VoxelIndex(p.z, cell);
                long key = VoxelKey(cx, cy, cz);
                if (cells.TryGetValue(key, out var acc))
                {
                    acc.Min = Vector3.Min(acc.Min, p);
                    acc.Max = Vector3.Max(acc.Max, p);
                }
                else
                {
                    cells[key] = new CellAcc { Min = p, Max = p };
                }
            }
        }

        foreach (var kv in cells)
        {
            Vector3 mn = kv.Value.Min;
            Vector3 mx = kv.Value.Max;
            // 防止零厚度盒（后面的查询对它不友好）
            mx = Vector3.Max(mx, mn + Vector3.one * GridAlignEpsilon);
            output.Add(new AABB(new Vec3(mn.x, mn.y, mn.z), new Vec3(mx.x, mx.y, mx.z)));
        }
        return cells.Count;
    }

    /// <summary>
    /// 旋转 BoxCollider 体素化：将世界 AABB 细分后，在 Box 本地空间做保守相交测试
    /// </summary>
    private static int VoxelizeRotatedBox(BoxCollider boxCol, Bounds worldBounds, List<AABB> output)
    {
        Transform t = boxCol.transform;
        float cell = BoxVoxelSize;
        float halfDiag = cell * 0.5f * 1.7320508f; // 体素半对角线（保守球近似）
        float halfDiagSq = halfDiag * halfDiag;

        Vector3 localCenter = boxCol.center;
        Vector3 localHalf = boxCol.size * 0.5f;

        int x0 = VoxelIndex(worldBounds.min.x, cell), x1 = VoxelIndex(worldBounds.max.x, cell);
        int y0 = VoxelIndex(worldBounds.min.y, cell), y1 = VoxelIndex(worldBounds.max.y, cell);
        int z0 = VoxelIndex(worldBounds.min.z, cell), z1 = VoxelIndex(worldBounds.max.z, cell);

        // 体素数保护：过密则放大体素
        long total = (long)(x1 - x0 + 1) * (y1 - y0 + 1) * (z1 - z0 + 1);
        if (total > 200000)
        {
            Debug.LogWarning($"[CollisionExport] {boxCol.name} 旋转盒体素数 {total} 过大，退化为单个 AABB");
            return 0;
        }

        int startCount = output.Count;
        for (int x = x0; x <= x1; x++)
        for (int y = y0; y <= y1; y++)
        for (int z = z0; z <= z1; z++)
        {
            Vector3 cellCenter = new Vector3((x + 0.5f) * cell, (y + 0.5f) * cell, (z + 0.5f) * cell);
            Vector3 local = t.InverseTransformPoint(cellCenter) - localCenter;

            // 点到本地盒的最近距离 ≤ 体素半对角线 → 保留（保守）
            float dx = Mathf.Max(Mathf.Abs(local.x) - localHalf.x, 0f);
            float dy = Mathf.Max(Mathf.Abs(local.y) - localHalf.y, 0f);
            float dz = Mathf.Max(Mathf.Abs(local.z) - localHalf.z, 0f);
            if (dx * dx + dy * dy + dz * dz > halfDiagSq)
                continue;

            Vector3 cellMin = new Vector3(x * cell, y * cell, z * cell);
            output.Add(new AABB(
                new Vec3(cellMin.x, cellMin.y, cellMin.z),
                new Vec3(cellMin.x + cell, cellMin.y + cell, cellMin.z + cell)));
        }
        return output.Count - startCount;
    }

    /// <summary>
    /// 物理查询体素化（网格不可读时的降级方案）：
    /// 用 OverlapBox 逐体素检测与目标 Collider 的重叠。较慢但无需网格读权限。
    /// </summary>
    private static int VoxelizeWithPhysics(Collider target, Bounds worldBounds, float cell, List<AABB> output)
    {
        int x0 = VoxelIndex(worldBounds.min.x, cell), x1 = VoxelIndex(worldBounds.max.x, cell);
        int y0 = VoxelIndex(worldBounds.min.y, cell), y1 = VoxelIndex(worldBounds.max.y, cell);
        int z0 = VoxelIndex(worldBounds.min.z, cell), z1 = VoxelIndex(worldBounds.max.z, cell);

        long total = (long)(x1 - x0 + 1) * (y1 - y0 + 1) * (z1 - z0 + 1);
        if (total > 500000)
        {
            Debug.LogWarning($"[CollisionExport] {target.name} 物理体素化数量 {total} 过大，跳过");
            return 0;
        }

        Physics.SyncTransforms();
        var buffer = new Collider[32];
        Vector3 half = Vector3.one * (cell * 0.5f);

        int startCount = output.Count;
        for (int x = x0; x <= x1; x++)
        for (int y = y0; y <= y1; y++)
        for (int z = z0; z <= z1; z++)
        {
            Vector3 cellCenter = new Vector3((x + 0.5f) * cell, (y + 0.5f) * cell, (z + 0.5f) * cell);
            int n = Physics.OverlapBoxNonAlloc(cellCenter, half, buffer, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            bool hit = false;
            for (int i = 0; i < n; i++)
            {
                if (buffer[i] == target) { hit = true; break; }
            }
            if (!hit) continue;

            Vector3 cellMin = new Vector3(x * cell, y * cell, z * cell);
            output.Add(new AABB(
                new Vec3(cellMin.x, cellMin.y, cellMin.z),
                new Vec3(cellMin.x + cell, cellMin.y + cell, cellMin.z + cell)));
        }
        return output.Count - startCount;
    }

    // ================= 共面相邻盒合并（保体积） =================

    /// <summary>
    /// 沿 X/Y/Z 轴迭代合并"其余两轴范围完全相同"的相邻/重叠盒。
    /// 只合并同范围相邻盒，合并是保体积的，不会引入新的实心区域。
    /// </summary>
    private static List<AABB> FaceMerge(List<AABB> boxes)
    {
        for (int round = 0; round < 2; round++)
        {
            int before = boxes.Count;
            boxes = MergeAlongAxis(boxes, 0);
            boxes = MergeAlongAxis(boxes, 1);
            boxes = MergeAlongAxis(boxes, 2);
            if (boxes.Count == before)
                break;
        }
        return boxes;
    }

    private static float Quantize(float v) => Mathf.Round(v * 10000f) / 10000f;

    private static List<AABB> MergeAlongAxis(List<AABB> boxes, int axis)
    {
        // 按"其余两轴的量化范围"分组
        var groups = new Dictionary<string, List<AABB>>();
        foreach (var b in boxes)
        {
            string key;
            if (axis == 0)
                key = $"{Quantize(b.Min.y)},{Quantize(b.Max.y)}|{Quantize(b.Min.z)},{Quantize(b.Max.z)}";
            else if (axis == 1)
                key = $"{Quantize(b.Min.x)},{Quantize(b.Max.x)}|{Quantize(b.Min.z)},{Quantize(b.Max.z)}";
            else
                key = $"{Quantize(b.Min.x)},{Quantize(b.Max.x)}|{Quantize(b.Min.y)},{Quantize(b.Max.y)}";

            if (!groups.TryGetValue(key, out var list))
            {
                list = new List<AABB>();
                groups[key] = list;
            }
            list.Add(b);
        }

        var result = new List<AABB>(boxes.Count);
        foreach (var kv in groups)
        {
            var list = kv.Value;
            // 按合并轴的 min 排序，贪心延伸
            list.Sort((a, b) => GetMin(a, axis).CompareTo(GetMin(b, axis)));

            AABB cur = list[0];
            for (int i = 1; i < list.Count; i++)
            {
                AABB next = list[i];
                // 相邻（接触）或重叠才合并；有间隙则保持分开
                if (GetMin(next, axis) <= GetMax(cur, axis) + GridAlignEpsilon)
                {
                    cur = UnionAlongAxis(cur, next, axis);
                }
                else
                {
                    result.Add(cur);
                    cur = next;
                }
            }
            result.Add(cur);
        }
        return result;
    }

    private static float GetMin(AABB b, int axis) => axis == 0 ? b.Min.x : (axis == 1 ? b.Min.y : b.Min.z);
    private static float GetMax(AABB b, int axis) => axis == 0 ? b.Max.x : (axis == 1 ? b.Max.y : b.Max.z);

    private static AABB UnionAlongAxis(AABB a, AABB b, int axis)
    {
        Vec3 min = a.Min, max = a.Max;
        if (axis == 0) { max.x = Mathf.Max(a.Max.x, b.Max.x); min.x = Mathf.Min(a.Min.x, b.Min.x); }
        else if (axis == 1) { max.y = Mathf.Max(a.Max.y, b.Max.y); min.y = Mathf.Min(a.Min.y, b.Min.y); }
        else { max.z = Mathf.Max(a.Max.z, b.Max.z); min.z = Mathf.Min(a.Min.z, b.Min.z); }
        return new AABB(min, max);
    }

    // ================= 统计 =================

    [MenuItem("游戏/碰撞/查看场景碰撞统计", false, 201)]
    public static void ShowCollisionStats()
    {
        var all = Object.FindObjectsOfType<Collider>(true);
        int triggers = 0, playerLayer = 0, tiny = 0, flat = 0, valid = 0;
        int mesh = 0, rotatedBox = 0;
        int playerL = LayerMask.NameToLayer("Ignore Raycast");

        foreach (var col in all)
        {
            if (col.isTrigger) { triggers++; continue; }
            if (col.gameObject.layer == playerL) { playerLayer++; continue; }
            Bounds b = col.bounds;
            float volume = b.size.x * b.size.y * b.size.z;
            if (volume < 0.001f) { tiny++; continue; }
            float xzArea = b.size.x * b.size.z;
            if (b.size.y < 0.15f && xzArea > 100f) { flat++; continue; }
            valid++;
            if (col is MeshCollider) mesh++;
            else if (col is BoxCollider bc && !IsAxisAligned(bc.transform)) rotatedBox++;
        }

        Debug.Log($"========== 场景碰撞统计 ==========");
        Debug.Log($"总 Collider:  {all.Length}");
        Debug.Log($"有效:         {valid}  (Mesh: {mesh}, 旋转Box: {rotatedBox})");
        Debug.Log($"触发器(跳过): {triggers}");
        Debug.Log($"玩家层(跳过): {playerLayer}");
        Debug.Log($"微小(跳过):   {tiny}");
        Debug.Log($"薄地面(跳过): {flat}");
        Debug.Log($"==================================");
    }
}
