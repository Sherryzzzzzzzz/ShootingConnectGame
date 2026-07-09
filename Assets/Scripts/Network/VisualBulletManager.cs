using System.Collections.Generic;
using UnityEngine;
using ShootingGame.Shared.Math;
using ShootingGame.Shared.Protocol;
using ShootingGame.Shared.Simulation;
using SharedVec3 = ShootingGame.Shared.Math.Vec3;

/// <summary>
/// Visual Bullet Manager for client-side bullet visualization.
/// Handles visual bullet spawning, flight, and cleanup.
/// Bullets are purely visual - collision is handled by the server.
/// </summary>
public class VisualBulletManager : MonoBehaviour
{
    [Header("Visual Settings")]
    [SerializeField] private GameObject bulletPrefab;
    [SerializeField] private float bulletSpeed = 100f;
    [SerializeField] private float bulletLifetime = 3f;
    [SerializeField] private int maxBullets = 100;

    public float BulletSpeed => bulletSpeed;

    [Header("Trail Effect")]
    [SerializeField] private bool useTrail = true;
    [SerializeField] private float trailDuration = 0.2f;
    [SerializeField] private Gradient trailColor;

    // Bullet pool
    private readonly Queue<VisualBullet> _bulletPool = new Queue<VisualBullet>();
    private readonly List<VisualBullet> _activeBullets = new List<VisualBullet>();

    // Attack dedup: prevents double-spawning bullets for the same attack
    // Uses composite key: (attackerId << 32) | attackId
    private readonly HashSet<long> _spawnedAttackKeys = new HashSet<long>();

    // Singleton
    public static VisualBulletManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        // 如果 Inspector 未赋值，尝试从 Resources 加载
        if (bulletPrefab == null)
        {
            bulletPrefab = Resources.Load<GameObject>("Bullet");
            if (bulletPrefab != null)
                Debug.Log($"[VisualBulletManager] 从 Resources 加载 Bullet prefab: {bulletPrefab.name}");
            else
                Debug.LogWarning("[VisualBulletManager] Resources.Load<GameObject>(\"Bullet\") 返回 null，将使用程序化回退。请确保 Assets/Resources/Bullet.prefab 存在。");
        }

        // Pre-populate pool
        for (int i = 0; i < maxBullets / 2; i++)
        {
            CreateBullet();
        }

        Debug.Log($"[VisualBulletManager] Initialized: pool={_bulletPool.Count} max={maxBullets} prefab={bulletPrefab?.name ?? "null(fallback)"}");
    }

    private int _updateFrameCount;
    private void Update()
    {
        _updateFrameCount++;

        // Update all active bullets
        for (int i = _activeBullets.Count - 1; i >= 0; i--)
        {
            var bullet = _activeBullets[i];
            bullet.Time += Time.deltaTime;

            if (bullet.Time >= bullet.Lifetime || bullet.TraveledDistance >= bullet.MaxDistance)
            {
                ReturnBullet(bullet);
                continue;
            }

            // Move bullet
            Vector3 movement = bullet.Direction * bullet.Speed * Time.deltaTime;
            bullet.Transform.position += movement;
            bullet.TraveledDistance += movement.magnitude;

            // 在 Scene 视图中可视化子弹（红线=飞行方向，黄点=子弹位置）
            if (_activeBullets.Count <= 3 || _updateFrameCount % 30 == 0)
                Debug.DrawLine(bullet.Transform.position, bullet.Transform.position + bullet.Direction * 0.5f, Color.red, 0.05f);
        }

        // 有子弹时每30帧打印状态
        if (_activeBullets.Count > 0 && _updateFrameCount % 30 == 0)
        {
            var b = _activeBullets[0];
            Debug.Log($"[VBM] activeBullets={_activeBullets.Count} pool={_bulletPool.Count} firstBullet=({b.Transform.position.x:F1},{b.Transform.position.y:F1},{b.Transform.position.z:F1}) dist={b.TraveledDistance:F1} time={b.Time:F2}");
        }
    }

    /// <summary>
    /// Try to mark an attack as spawned. Returns false if already spawned (dedup).
    /// </summary>
    public bool TryMarkAttackSpawned(int attackId, int attackerId)
    {
        long key = ((long)attackerId << 32) | (uint)attackId;
        if (_spawnedAttackKeys.Contains(key))
            return false;
        _spawnedAttackKeys.Add(key);
        return true;
    }

    /// <summary>
    /// Spawn a visual bullet from local player firing.
    /// </summary>
    public void SpawnLocalBullet(Vector3 origin, Vector3 direction, int attackId)
    {
        if (!TryMarkAttackSpawned(attackId, BattleClient.Instance?.BattlePlayerId ?? -1))
            return;

        var bullet = GetBullet();
        if (bullet == null) return;

        bullet.Transform.position = origin;
        bullet.Direction = direction.normalized;
        bullet.Speed = bulletSpeed;
        bullet.Time = 0f;
        bullet.TraveledDistance = 0f;
        bullet.MaxDistance = 200f;
        bullet.Lifetime = bulletLifetime;
        bullet.AttackId = attackId;
        bullet.IsVisualOnly = true;

        _activeBullets.Add(bullet);

        if (TracerVFX.Instance != null)
        {
            TracerVFX.Instance.SpawnTracer(origin, direction);
        }
    }

    /// <summary>
    /// Spawn a visual bullet from authority frame data (centralized Path B).
    /// Position is advanced by catchupDistance to compensate for network delay.
    /// </summary>
    public void SpawnAuthorityBullet(Vector3 position, Vector3 direction, int attackId, int attackerId, float catchupDistance)
    {
        if (!TryMarkAttackSpawned(attackId, attackerId))
            return;

        var bullet = GetBullet();
        if (bullet == null) return;

        bullet.Transform.position = position + direction.normalized * catchupDistance;
        bullet.Direction = direction.normalized;
        bullet.Speed = bulletSpeed;
        bullet.Time = catchupDistance / bulletSpeed;
        bullet.TraveledDistance = catchupDistance;
        bullet.MaxDistance = 200f + catchupDistance;
        bullet.Lifetime = bulletLifetime;
        bullet.AttackId = attackId;
        bullet.AttackerId = attackerId;
        bullet.IsVisualOnly = true;

        _activeBullets.Add(bullet);

        if (TracerVFX.Instance != null)
        {
            TracerVFX.Instance.SpawnTracer(position, direction);
        }
    }

    /// <summary>
    /// Spawn a visual bullet from server data.
    /// Uses the spawn position set by the server (authoritative).
    /// </summary>
    public void SpawnServerBullet(ShootingGame.Shared.Protocol.AttackOperation atk, Vector3 direction, int attackerId)
    {
        if (!TryMarkAttackSpawned(atk.AttackId, attackerId))
            return;

        var bullet = GetBullet();
        if (bullet == null) return;

        // 使用服务器设置的生成位置；若为零则从 AuthoritySync 的权威状态回退
        Vector3 spawnPos;
        if (atk.SpawnPos.x != 0f || atk.SpawnPos.y != 0f || atk.SpawnPos.z != 0f)
        {
            spawnPos = new Vector3(atk.SpawnPos.x, atk.SpawnPos.y, atk.SpawnPos.z);
        }
        else if (AuthoritySync.Instance != null)
        {
            var authState = AuthoritySync.Instance.GetPlayerState(attackerId);
            if (authState != null)
            {
                spawnPos = new Vector3(authState.Position.x, authState.Position.y + GameConstants.PlayerHeight * 0.85f, authState.Position.z);
            }
            else
            {
                Debug.LogWarning($"[VisualBulletManager] SpawnServerBullet: SpawnPos=zero and no authState for attacker {attackerId}");
                return;
            }
        }
        else
        {
            Debug.LogWarning($"[VisualBulletManager] SpawnServerBullet: SpawnPos=zero and AuthoritySync.Instance=null");
            return;
        }

        // 帧差补偿
        int serverFrame = BattleClient.Instance?.ServerFrameId ?? 0;
        int spawnFrame = atk.ClientFrameId;
        int frameDiff = Mathf.Max(0, serverFrame - spawnFrame);
        frameDiff = Mathf.Min(frameDiff, GameConstants.MaxCompensationTicks);

        float catchupDistance = bulletSpeed * (frameDiff * GameConstants.TickDelta);

        bullet.Transform.position = spawnPos + direction.normalized * catchupDistance;
        bullet.Direction = direction.normalized;
        bullet.Speed = bulletSpeed;
        bullet.Time = catchupDistance / bulletSpeed;
        bullet.TraveledDistance = catchupDistance;
        bullet.MaxDistance = 200f + catchupDistance;
        bullet.Lifetime = bulletLifetime;
        bullet.AttackId = atk.AttackId;
        bullet.AttackerId = attackerId;
        bullet.IsVisualOnly = true;

        _activeBullets.Add(bullet);

        // Play spawn effect
        if (TracerVFX.Instance != null)
        {
            TracerVFX.Instance.SpawnTracer(spawnPos, direction);
        }
    }

    /// <summary>
    /// Remove a bullet by attack ID (when hit event is received).
    /// </summary>
    public void RemoveBullet(int attackId)
    {
        for (int i = _activeBullets.Count - 1; i >= 0; i--)
        {
            if (_activeBullets[i].AttackId == attackId)
            {
                ReturnBullet(_activeBullets[i]);
                return;
            }
        }
    }

    /// <summary>
    /// Clear all bullets.
    /// </summary>
    public void ClearAll()
    {
        foreach (var bullet in _activeBullets)
        {
            if (bullet.GameObject != null)
                bullet.GameObject.SetActive(false);
            _bulletPool.Enqueue(bullet);
        }
        _activeBullets.Clear();
        _spawnedAttackKeys.Clear();
    }

    private VisualBullet GetBullet()
    {
        if (_bulletPool.Count > 0)
        {
            var bullet = _bulletPool.Dequeue();
            bullet.GameObject.SetActive(true);
            return bullet;
        }

        if (_activeBullets.Count < maxBullets)
        {
            var bullet = CreateBullet();
            bullet.GameObject.SetActive(true);
            return bullet;
        }

        // Reuse oldest bullet
        var oldest = _activeBullets[0];
        _activeBullets.RemoveAt(0);
        oldest.GameObject.SetActive(true);
        return oldest;
    }

    private VisualBullet CreateBullet()
    {
        GameObject go;
        if (bulletPrefab != null)
        {
            go = Instantiate(bulletPrefab, transform);
            // 禁用碰撞体（视觉子弹由服务端判定命中）
            var cols = go.GetComponentsInChildren<Collider>(includeInactive: true);
            foreach (var col in cols)
                col.enabled = false;
        }
        else
        {
            // 回退：创建一个高可见度小球
            go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.transform.SetParent(transform);
            go.transform.localScale = Vector3.one * 0.3f;
            go.name = "VisualBullet_Fallback";
            // 设置亮色材质
            var renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                var mat = new Material(Shader.Find("Standard"));
                mat.color = Color.yellow;
                mat.SetColor("_EmissionColor", Color.yellow * 0.5f);
                mat.EnableKeyword("_EMISSION");
                renderer.material = mat;
            }
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            Destroy(go.GetComponent<Collider>());
        }
        go.SetActive(false);

        var bullet = new VisualBullet
        {
            GameObject = go,
            Transform = go.transform,
            Renderer = go.GetComponentInChildren<Renderer>(),
            Lifetime = bulletLifetime
        };

        _bulletPool.Enqueue(bullet);
        return bullet;
    }

    private void ReturnBullet(VisualBullet bullet)
    {
        _activeBullets.Remove(bullet);
        bullet.GameObject.SetActive(false);
        _bulletPool.Enqueue(bullet);
    }

    /// <summary>
    /// Get active bullet count for debugging.
    /// </summary>
    public int GetActiveBulletCount() => _activeBullets.Count;

    private struct ServerBulletData
    {
        public int AttackId;
        public Vector3 Position;
        public Vector3 Direction;
        public float Speed;
        public float TraveledDistance;
    }
}

/// <summary>
/// Represents a visual bullet instance.
/// </summary>
public class VisualBullet
{
    public GameObject GameObject;
    public Transform Transform;
    public Renderer Renderer;

    public Vector3 Direction;
    public float Speed;
    public float Time;
    public float TraveledDistance;
    public float MaxDistance;
    public float Lifetime;
    public int AttackId;
    public int AttackerId;
    public bool IsVisualOnly;
}