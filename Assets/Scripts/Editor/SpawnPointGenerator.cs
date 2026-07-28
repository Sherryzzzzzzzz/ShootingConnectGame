using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using ShootingGame.Shared.Math;
using ShootingGame.Shared.Physics;

public static class SpawnPointGenerator
{
    private const float SampleStep = 2f;
    private const float MinPointSpacing = 6f;
    private const float Headroom = 2.2f;
    private const float PlayerRadius = 0.35f;
    private const int MaxPoints = 36;

    /// <summary>
    /// 检测周围地面平坦度：四个方向 0.8m 外采样，高度差 > 0.5m 说明是窄东西（杆/招牌顶），拒绝。
    /// </summary>
    private static bool IsFlatGround(CollisionWorld world, float x, float z, float groundY, float maxY)
    {
        float offset = 0.8f;
        var offsets = new (float, float)[] { (offset, 0), (-offset, 0), (0, offset), (0, -offset) };
        foreach (var (ox, oz) in offsets)
        {
            var h = world.Raycast(new Vec3(x + ox, maxY + 5f, z + oz), Vec3.Down, maxY + 10f);
            if (!h.Hit) return false;
            if (Mathf.Abs(h.Point.y - groundY) > 0.5f) return false;
        }
        return true;
    }

    [MenuItem("游戏/配置/生成死斗出生点", false, 110)]
    public static void Generate()
    {
        string binPath = Path.Combine(Application.streamingAssetsPath, "collision.bin");
        if (!File.Exists(binPath))
        {
            EditorUtility.DisplayDialog("生成失败", $"找不到 {binPath}", "OK");
            return;
        }

        var world = CollisionWorld.Load(binPath);
        if (world.Count == 0)
        {
            EditorUtility.DisplayDialog("生成失败", "collision.bin 是空的", "OK");
            return;
        }

        Debug.Log($"[SpawnGen] 碰撞世界: {world.Count} boxes");

        Vec3 min = world.Boxes[0].Min, max = world.Boxes[0].Max;
        foreach (var b in world.Boxes)
        {
            min = new Vec3(Mathf.Min(min.x, b.Min.x), Mathf.Min(min.y, b.Min.y), Mathf.Min(min.z, b.Min.z));
            max = new Vec3(Mathf.Max(max.x, b.Max.x), Mathf.Max(max.y, b.Max.y), Mathf.Max(max.z, b.Max.z));
        }
        max = new Vec3(max.x, Mathf.Min(max.y, 100f), max.z);
        Debug.Log($"[SpawnGen] 地图范围: ({min.x:F0},{min.z:F0}) - ({max.x:F0},{max.z:F0})");

        var candidates = new List<Vector3>();
        int rayMiss = 0, slopeFail = 0, lowY = 0, headBlocked = 0, sealedRoom = 0, flatFail = 0;
        int totalSamples = 0;

        for (float x = min.x + SampleStep; x < max.x - SampleStep; x += SampleStep)
        for (float z = min.z + SampleStep; z < max.z - SampleStep; z += SampleStep)
        {
            totalSamples++;

            var origin = new Vec3(x, max.y + 5f, z);
            var hit = world.Raycast(origin, Vec3.Down, max.y + 10f);
            if (!hit.Hit) { rayMiss++; continue; }

            float slope = Vec3.Angle(hit.Normal, Vec3.Up);
            if (slope > 30f) { slopeFail++; continue; }

            float groundY = hit.Point.y;
            if (groundY < -5f) { lowY++; continue; }

            var headCheck = world.SweepSphere(
                new Vec3(x, groundY + PlayerRadius + 0.05f, z),
                PlayerRadius, Vec3.Up, Headroom);
            if (headCheck.Hit) { headBlocked++; continue; }

            int blocked = 0;
            Vec3 checkOrigin = new Vec3(x, groundY + PlayerRadius + 0.1f, z);
            var dirs = new[] { Vec3.Forward, Vec3.Back, Vec3.Right, Vec3.Left };
            foreach (var d in dirs)
            {
                var s = world.SweepSphere(checkOrigin, PlayerRadius, d, 2f);
                if (s.Hit && s.Distance < 1.5f) blocked++;
            }
            if (blocked >= 4) { sealedRoom++; continue; }

            if (!IsFlatGround(world, x, z, groundY, max.y)) { flatFail++; continue; }

            candidates.Add(new Vector3(x, groundY, z));
        }

        Debug.Log($"[SpawnGen] 采样:{totalSamples} 候选:{candidates.Count} 未命中:{rayMiss} 坡度:{slopeFail} 低Y:{lowY} 头顶:{headBlocked} 密封:{sealedRoom} 窄面:{flatFail}");

        if (candidates.Count == 0)
        {
            string detail = $"采样 {totalSamples} 个网格点\n未命中: {rayMiss} | 坡度: {slopeFail} | 低Y: {lowY}\n头顶遮挡: {headBlocked} | 密封: {sealedRoom} | 窄面: {flatFail}";
            EditorUtility.DisplayDialog("生成失败", $"没有找到满足条件的出生点。\n\n{detail}", "OK");
            return;
        }

        // 洗牌候选（避免每次都选同一批）
        var rng = new System.Random();
        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            var tmp = candidates[i]; candidates[i] = candidates[j]; candidates[j] = tmp;
        }

        var selected = new List<Vector3>();
        foreach (var c in candidates)
        {
            bool tooClose = false;
            foreach (var s in selected)
                if (Vector3.Distance(c, s) < MinPointSpacing) { tooClose = true; break; }
            if (!tooClose)
            {
                selected.Add(c);
                if (selected.Count >= MaxPoints) break;
            }
        }

        if (selected.Count == 0)
        {
            EditorUtility.DisplayDialog("生成失败", "候选点全部间距过近", "OK");
            return;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine("  \"spawnPoints\": [");
        for (int i = 0; i < selected.Count; i++)
        {
            var p = selected[i];
            sb.Append($"    {{ \"x\": {p.x:F2}, \"y\": {p.y:F2}, \"z\": {p.z:F2}, \"yaw\": 0.0, \"teamId\": 0 }}");
            if (i < selected.Count - 1) sb.AppendLine(","); else sb.AppendLine();
        }
        sb.AppendLine("  ]");
        sb.AppendLine("}");

        string projectRoot = Path.GetDirectoryName(Application.dataPath);
        string clientPath = Path.Combine(Application.dataPath, "Resources/SpawnPoints.json");
        string serverPath = Path.Combine(projectRoot, "Server", "SpawnPoints.json");
        File.WriteAllText(clientPath, sb.ToString());
        File.WriteAllText(serverPath, sb.ToString());

        AssetDatabase.Refresh();
        Debug.Log($"[SpawnGen] {selected.Count} 个出生点");
        EditorUtility.DisplayDialog("生成完成",
            $"已生成 {selected.Count} 个出生点（候选 {candidates.Count}/{totalSamples}）\n\n" +
            $"未命中:{rayMiss} 坡度:{slopeFail} 低Y:{lowY} 头顶:{headBlocked} 密封:{sealedRoom} 窄面:{flatFail}",
            "OK");
    }
}
