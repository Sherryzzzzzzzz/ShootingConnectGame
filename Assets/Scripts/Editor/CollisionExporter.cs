using UnityEditor;
using UnityEngine;
using System.IO;

/// <summary>
/// Editor tool to export all BoxColliders in the current scene to a binary collision file.
/// Format: [int32 count] [AABB * count: 6 floats each (minX,minY,minZ,maxX,maxY,maxZ)]
/// The exported file can be loaded by ShootingGame.Shared.Physics.CollisionWorld.
/// </summary>
public class CollisionExporter : EditorWindow
{
    private string exportPath = "Assets/StreamingAssets/collision.bin";

    [MenuItem("Tools/Export Collision Data")]
    public static void ShowWindow()
    {
        GetWindow<CollisionExporter>("Collision Exporter");
    }

    private void OnGUI()
    {
        GUILayout.Label("Collision Data Exporter", EditorStyles.boldLabel);
        GUILayout.Space(10);

        exportPath = EditorGUILayout.TextField("Export Path", exportPath);

        GUILayout.Space(10);

        if (GUILayout.Button("Export All BoxColliders", GUILayout.Height(40)))
        {
            Export();
        }
    }

    private void Export()
    {
        BoxCollider[] colliders = FindObjectsOfType<BoxCollider>();

        if (colliders.Length == 0)
        {
            EditorUtility.DisplayDialog("Export", "No BoxColliders found in scene.", "OK");
            return;
        }

        // Ensure directory exists
        string dir = Path.GetDirectoryName(exportPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using (var fs = File.Create(exportPath))
        using (var bw = new BinaryWriter(fs))
        {
            bw.Write(colliders.Length);

            foreach (var col in colliders)
            {
                // Convert collider bounds to world-space AABB
                // For rotated/scaled colliders, we use the axis-aligned Bounds
                Bounds bounds = col.bounds;

                Vector3 min = bounds.min;
                Vector3 max = bounds.max;

                bw.Write(min.x);
                bw.Write(min.y);
                bw.Write(min.z);
                bw.Write(max.x);
                bw.Write(max.y);
                bw.Write(max.z);
            }
        }

        AssetDatabase.Refresh();
        Debug.Log($"Exported {colliders.Length} BoxColliders to {exportPath}");
        EditorUtility.DisplayDialog("Export Complete",
            $"Exported {colliders.Length} BoxColliders to:\n{exportPath}", "OK");
    }
}
