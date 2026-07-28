using UnityEngine;

/// <summary>
/// 可视化生成点。挂到空 GameObject 上，在场景中直接拖位置。
/// TeamId: 0=双方通用, 1=队伍1, 2=队伍2
/// BattleManager 会在 SpawnPlayers 时自动收集所有 SpawnPoint。
/// </summary>
public class SpawnPoint : MonoBehaviour
{
    [Header("队伍")]
    [Tooltip("0 = 双方通用, 1 = 队伍1, 2 = 队伍2")]
    [SerializeField] private int _teamId;

    [Header("朝向")]
    [Tooltip("生成后角色的初始朝向（Y 轴旋转角度）")]
    [SerializeField] private float _spawnYaw;

    [Header("防卡墙")]
    [Tooltip("生成前向上搜索安全位置的最大距离")]
    [SerializeField] private float _antiStuckUpCheck = 5f;
    [Tooltip("生成位置的碰撞检测半径")]
    [SerializeField] private float _clearanceRadius = 0.5f;

    public int TeamId => _teamId;
    public float SpawnYaw => _spawnYaw;
    public float AntiStuckUpCheck => _antiStuckUpCheck;
    public float ClearanceRadius => _clearanceRadius;

    /// <summary>场景中注册的所有生成点（运行时由 BattleManager 收集）</summary>
    public static System.Collections.Generic.List<SpawnPoint> AllSpawnPoints { get; private set; }
        = new System.Collections.Generic.List<SpawnPoint>();

    private void OnEnable()
    {
        if (!AllSpawnPoints.Contains(this))
            AllSpawnPoints.Add(this);
    }

    private void OnDisable()
    {
        AllSpawnPoints.Remove(this);
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        // 根据队伍显示不同颜色
        Gizmos.color = _teamId switch
        {
            1 => new Color(0.3f, 0.5f, 1f, 0.7f),   // 蓝队
            2 => new Color(1f, 0.3f, 0.3f, 0.7f),   // 红队
            _ => new Color(0.3f, 1f, 0.3f, 0.7f),    // 通用-绿
        };

        // 地面圆盘
        Vector3 center = transform.position;
        DrawDisc(center, 0.5f);

        // 角色高度线
        Vector3 head = center + Vector3.up * 2f;
        Gizmos.DrawLine(center, head);

        // 头部小球
        Gizmos.DrawSphere(head, 0.2f);

        // 朝向箭头
        Vector3 forward = Quaternion.Euler(0, _spawnYaw, 0) * Vector3.forward;
        Gizmos.DrawRay(center + Vector3.up * 1f, forward * 0.6f);

        // 标签
        UnityEditor.Handles.Label(center + Vector3.up * 2.3f,
            _teamId == 0 ? $"Spawn\n(Any)" : $"Spawn\nTeam {_teamId}");
    }

    private static void DrawDisc(Vector3 center, float radius)
    {
        int segments = 32;
        float angleStep = 360f / segments;
        Vector3 prevPoint = center + Vector3.right * radius;
        for (int i = 1; i <= segments; i++)
        {
            float angle = angleStep * i * Mathf.Deg2Rad;
            Vector3 point = center + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius;
            Gizmos.DrawLine(prevPoint, point);
            prevPoint = point;
        }
    }
#endif
}
