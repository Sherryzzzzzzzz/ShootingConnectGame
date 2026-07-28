using UnityEditor;
using UnityEngine;

/// <summary>
/// SpawnPoint 编辑器工具。
/// 菜单: 游戏/生成点/...
/// </summary>
public class SpawnPointTool
{
    [MenuItem("游戏/生成点/创建双方生成点 (Team1 + Team2)", false, 150)]
    public static void CreateBothSpawnPoints()
    {
        CreateSpawnPoint("Spawn_Team1", 1, new Vector3(-8, 0, 0));
        CreateSpawnPoint("Spawn_Team2", 2, new Vector3(8, 0, 0));
        CreateSpawnPoint("Spawn_Any", 0, new Vector3(0, 0, 5));
        Selection.activeGameObject = GameObject.Find("Spawn_Team1");
        Debug.Log("[SpawnPointTool] 已创建 3 个生成点。在 Scene View 中拖拽调整位置。");
    }

    [MenuItem("游戏/生成点/在当前选中位置创建 SpawnPoint", false, 151)]
    public static void CreateSpawnAtSelection()
    {
        var sel = Selection.activeTransform;
        Vector3 pos = sel != null ? sel.position : SceneView.lastActiveSceneView?.camera?.transform.position ?? Vector3.zero;
        var go = CreateSpawnPoint("SpawnPoint_New", 0, pos);
        Selection.activeGameObject = go;
        Debug.Log($"[SpawnPointTool] 在 {pos} 创建 SpawnPoint");
    }

    private static GameObject CreateSpawnPoint(string name, int teamId, Vector3 position)
    {
        var go = new GameObject(name);
        go.transform.position = position;

        var sp = go.AddComponent<SpawnPoint>();
        // Use reflection to set private fields in Editor
        var so = new SerializedObject(sp);
        so.FindProperty("_teamId").intValue = teamId;
        so.ApplyModifiedProperties();

        // 自动吸附到地面
        RaycastHit hit;
        if (Physics.Raycast(position + Vector3.up * 10f, Vector3.down, out hit, 20f))
        {
            go.transform.position = hit.point + Vector3.up * 0.05f;
        }

        Undo.RegisterCreatedObjectUndo(go, "Create SpawnPoint");
        return go;
    }

    [MenuItem("游戏/生成点/验证所有 SpawnPoint 安全性", false, 152)]
    public static void ValidateAllSpawnPoints()
    {
        var all = Object.FindObjectsOfType<SpawnPoint>();
        int ok = 0, blocked = 0;

        foreach (var sp in all)
        {
            Vector3 pos = sp.transform.position;
            Vector3 capsuleBottom = pos + Vector3.up * 0.4f;
            Vector3 capsuleTop = pos + Vector3.up * 1.6f;
            float radius = 0.4f;

            Collider[] overlaps = Physics.OverlapCapsule(capsuleBottom, capsuleTop, radius);
            if (overlaps.Length > 0)
            {
                blocked++;
                string names = string.Join(", ", System.Array.ConvertAll(overlaps, c => c.name));
                Debug.LogWarning($"[SpawnPoint] ⚠ {sp.name} 被遮挡! 碰撞体: {names}", sp);
            }
            else
            {
                ok++;
            }
        }

        Debug.Log($"[SpawnPointTool] 验证完成: {ok} 安全, {blocked} 被遮挡 (共 {all.Length})");
    }

    [MenuItem("游戏/生成点/导出 SpawnPoints.json (服务端自动对齐)", false, 153)]
    public static void ExportSpawnPointsJson()
    {
        var all = Object.FindObjectsOfType<SpawnPoint>();
        if (all.Length == 0)
        {
            Debug.LogWarning("[SpawnPointTool] 场景中没有 SpawnPoint，请先创建！");
            return;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine("  \"spawnPoints\": [");

        for (int i = 0; i < all.Length; i++)
        {
            var sp = all[i];
            Vector3 pos = sp.transform.position;
            string comma = i < all.Length - 1 ? "," : "";
            sb.AppendLine($"    {{ \"x\": {pos.x:F2}, \"y\": {pos.y:F2}, \"z\": {pos.z:F2}, \"yaw\": 0.0, \"teamId\": {sp.TeamId} }}{comma}");
        }

        sb.AppendLine("  ]");
        sb.AppendLine("}");

        string json = sb.ToString();

        // 导出到 Server 目录（相对项目根目录）
        string projectRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
        string serverPath = System.IO.Path.Combine(projectRoot, "Server", "SpawnPoints.json");
        System.IO.File.WriteAllText(serverPath, json);
        Debug.Log($"[SpawnPointTool] 已导出 {all.Length} 个生成点到: {serverPath}");

        // 同时导出到 Resources（客户端也能加载）
        string resourcesPath = System.IO.Path.Combine(Application.dataPath, "Resources", "SpawnPoints.json");
        System.IO.File.WriteAllText(resourcesPath, json);
        AssetDatabase.Refresh();
        Debug.Log($"[SpawnPointTool] 已导出到客户端: {resourcesPath}");

        // 打印摘要
        Debug.Log("========== SpawnPoints 摘要 ==========");
        foreach (var sp in all)
            Debug.Log($"  Team{sp.TeamId}: ({sp.transform.position.x:F2}, {sp.transform.position.y:F2}, {sp.transform.position.z:F2})");
        Debug.Log("=========================================");
    }
}
