using UnityEngine;
using ShootingGame.Shared.Physics;
using ShootingGame.Shared.Math;

/// <summary>
/// 运行时从场景 BoxCollider 构建 CollisionWorld。
/// 挂载到 Level GameObject 上，自动收集子对象的所有 BoxCollider。
/// </summary>
[DefaultExecutionOrder(100)]
public class CollisionWorldBuilder : MonoBehaviour
{
    private void Awake()
    {
        if (CollisionWorldLoader.Instance == null)
        {
            CollisionWorldLoader.Instance = new CollisionWorld();
        }

        var colliders = GetComponentsInChildren<BoxCollider>();
        int added = 0;

        foreach (var col in colliders)
        {
            if (col == null) continue;

            Bounds bounds = col.bounds;
            Vec3 min = new Vec3(bounds.min.x, bounds.min.y, bounds.min.z);
            Vec3 max = new Vec3(bounds.max.x, bounds.max.y, bounds.max.z);
            CollisionWorldLoader.Instance.AddBox(new AABB(min, max));
            added++;
        }

        Debug.Log($"CollisionWorldBuilder: Added {added} BoxColliders to CollisionWorld (total: {CollisionWorldLoader.Instance.Count})");
    }

    [ContextMenu("Export to Console")]
    private void ExportToConsole()
    {
        if (CollisionWorldLoader.Instance == null)
        {
            Debug.LogWarning("CollisionWorldLoader.Instance is null. Run the scene first.");
            return;
        }

        var colliders = GetComponentsInChildren<BoxCollider>();
        Debug.Log($"=== CollisionWorld AABBs ({colliders.Length} BoxColliders) ===");
        foreach (var col in colliders)
        {
            if (col == null) continue;
            Bounds b = col.bounds;
            Debug.Log($"  [{col.name}] Min=({b.min.x:F3}, {b.min.y:F3}, {b.min.z:F3})  Max=({b.max.x:F3}, {b.max.y:F3}, {b.max.z:F3})  Size=({b.size.x:F3}, {b.size.y:F3}, {b.size.z:F3})");
        }
    }
}
