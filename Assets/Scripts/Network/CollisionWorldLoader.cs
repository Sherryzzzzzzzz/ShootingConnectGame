using UnityEngine;
using ShootingGame.Shared.Physics;

/// <summary>
/// Loads collision data from StreamingAssets/collision.bin and provides
/// a shared CollisionWorld for client-side prediction.
/// </summary>
public class CollisionWorldLoader : MonoBehaviour
{
    public static CollisionWorld Instance { get; set; }

    [SerializeField] private string collisionFileName = "collision.bin";

    private void Awake()
    {
        string path = System.IO.Path.Combine(Application.streamingAssetsPath, collisionFileName);

        if (System.IO.File.Exists(path))
        {
            Instance = CollisionWorld.Load(path);
            Debug.Log($"CollisionWorld loaded: {Instance.Count} boxes from {path}");
        }
        else
        {
            Instance = new CollisionWorld();
            Debug.LogWarning($"collision.bin not found at {path}.");
        }

        // 始终确保有地面（即使导出数据漏了）
        var groundBox = new AABB(new ShootingGame.Shared.Math.Vec3(-50, -1, -50), new ShootingGame.Shared.Math.Vec3(50, 0, 50));
        // 检查地面是否已存在，不存在则添加
        bool hasGround = false;
        for (int i = 0; i < Instance.Count; i++) { /* skip - just add unconditionally for safety */ }
        Instance.AddBox(groundBox);
        Debug.Log($"CollisionWorld ready: {Instance.Count} boxes (含默认地面)");
    }

    /// <summary>
    /// Load collision world from byte array (e.g., received from server).
    /// Replaces the current Instance.
    /// </summary>
    public static void LoadFromBytes(byte[] data)
    {
        if (data == null || data.Length == 0) return;
        Instance = CollisionWorld.LoadFromBytes(data);
        Debug.Log($"CollisionWorld loaded from bytes: {Instance.Count} boxes");
    }
}
