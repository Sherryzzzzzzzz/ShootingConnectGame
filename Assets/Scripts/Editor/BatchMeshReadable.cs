using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 批处理：把 Fight 场景所有 MeshCollider 用到的网格设为可读，
/// 使碰撞导出走精确的三角形体素化（而不是慢速物理查询回退）。
/// 用法: Unity.exe -batchmode -executeMethod BatchMeshReadable.MakeFightSceneMeshesReadable
/// </summary>
public static class BatchMeshReadable
{
    public static void MakeFightSceneMeshesReadable()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Fight.unity");

        var meshes = new HashSet<Mesh>();
        foreach (var mc in Object.FindObjectsOfType<MeshCollider>(true))
            if (mc.sharedMesh != null) meshes.Add(mc.sharedMesh);

        AssetDatabase.StartAssetEditing();
        int fixedCount = 0, already = 0;
        try
        {
            foreach (var mesh in meshes)
            {
                string path = AssetDatabase.GetAssetPath(mesh);
                if (string.IsNullOrEmpty(path)) continue;
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null) continue;
                if (importer.isReadable) { already++; continue; }
                importer.isReadable = true;
                importer.SaveAndReimport();
                fixedCount++;
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }
        Debug.Log($"[MeshReadable] {fixedCount} 个网格设为可读, {already} 个本就可读, 共 {meshes.Count} 个");
    }
}
