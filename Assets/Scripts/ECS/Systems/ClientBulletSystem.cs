using System.Collections.Generic;
using UnityEngine;
using ShootingGame.Shared.ECS;
using ShootingGame.Shared.Math;
using ShootingGame.Shared.Protocol;
using ShootingGame.Shared.Simulation;

/// <summary>
/// 客户端视觉子弹系统（替代 VisualBulletManager）。
/// 数据层：每个子弹是一个 ECS 实体 + VisualBulletComponent（方向/速度/生命周期）。
/// 表现层：GameObject 从对象池取得，每帧由系统按 ECS 数据更新位置。
/// </summary>
public class ClientBulletSystem : MonoBehaviour
{
    [Header("Visual Settings")]
    [SerializeField] private GameObject bulletPrefab;
    [SerializeField] private float bulletSpeed = 100f;
    [SerializeField] private float bulletLifetime = 3f;
    [SerializeField] private int maxBullets = 100;

    public float BulletSpeed => bulletSpeed;

    // 子弹对象池
    private readonly Queue<GameObject> _bulletPool = new Queue<GameObject>();
    private readonly List<GameObject> _activeObjects = new List<GameObject>();

    // 攻击去重：(attackerId << 32) | attackId
    private readonly HashSet<long> _spawnedAttackKeys = new HashSet<long>();

    public static ClientBulletSystem Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        if (bulletPrefab == null)
        {
            bulletPrefab = Resources.Load<GameObject>("Bullet");
            if (bulletPrefab == null)
                Debug.LogWarning("[ClientBulletSystem] Resources.Load<GameObject>(\"Bullet\") 返回 null，将使用程序化回退。");
        }

        for (int i = 0; i < maxBullets / 2; i++)
            CreateBullet();
    }

    private void Update()
    {
        var em = ClientECSWorld.Instance?.EntityManager;
        if (em == null) return;

        var bullets = new List<Entity>();
        em.GetEntitiesWith<VisualBulletComponent>(bullets);

        foreach (var entity in bullets)
        {
            if (!em.TryGetComponent<VisualBulletComponent>(entity, out var bullet))
                continue;

            bullet.Time += Time.deltaTime;
            if (bullet.Time >= bullet.Lifetime || bullet.TraveledDistance >= bullet.MaxDistance)
            {
                em.DestroyEntity(entity);
                ReturnBullet(bullet);
                continue;
            }

            Vector3 movement = bullet.Direction * bullet.Speed * Time.deltaTime;
            if (bullet.Transform != null) bullet.Transform.position += movement;
            bullet.TraveledDistance += movement.magnitude;
            em.SetComponent(entity, bullet);
        }
    }

    /// <summary>尝试标记攻击已生成。返回 false 表示重复（去重）。</summary>
    public bool TryMarkAttackSpawned(int attackId, int attackerId)
    {
        long key = ((long)attackerId << 32) | (uint)attackId;
        if (_spawnedAttackKeys.Contains(key)) return false;
        _spawnedAttackKeys.Add(key);
        return true;
    }

    /// <summary>本地玩家开火：生成一个视觉子弹实体。</summary>
    public void SpawnLocalBullet(Vector3 origin, Vector3 direction, int attackId)
    {
        int attackerId = BattleClient.Instance?.BattlePlayerId ?? -1;
        SpawnBullet(origin, direction, attackId, attackerId, 0f);
    }

    /// <summary>权威路径（Path B）：按服务端帧差补偿生成子弹。</summary>
    public void SpawnAuthorityBullet(Vector3 position, Vector3 direction, int attackId, int attackerId, float catchupDistance)
    {
        if (!TryMarkAttackSpawned(attackId, attackerId)) return;
        var go = GetBullet();
        if (go == null) return;

        go.transform.position = position + direction.normalized * catchupDistance;

        var em = ClientECSWorld.Instance?.EntityManager;
        if (em == null) return;

        var entity = em.CreateEntity();
        em.AddComponent(entity, new VisualBulletComponent
        {
            GameObject = go,
            Transform = go.transform,
            Direction = direction.normalized,
            Speed = bulletSpeed,
            Time = catchupDistance / bulletSpeed,
            TraveledDistance = catchupDistance,
            MaxDistance = 200f + catchupDistance,
            Lifetime = bulletLifetime,
            AttackId = attackId,
            AttackerId = attackerId
        });

        _activeObjects.Add(go);
        if (TracerVFX.Instance != null) TracerVFX.Instance.SpawnTracer(position, direction);
    }

    /// <summary>服务端下发子弹（使用服务端 SpawnPos）。</summary>
    public void SpawnServerBullet(AttackOperation atk, Vector3 direction, int attackerId)
    {
        if (!TryMarkAttackSpawned(atk.AttackId, attackerId)) return;

        Vector3 spawnPos;
        if (atk.SpawnPos.x != 0f || atk.SpawnPos.y != 0f || atk.SpawnPos.z != 0f)
        {
            spawnPos = new Vector3(atk.SpawnPos.x, atk.SpawnPos.y, atk.SpawnPos.z);
        }
        else if (AuthoritySync.Instance != null)
        {
            var authState = AuthoritySync.Instance.GetPlayerState(attackerId);
            if (authState == null)
            {
                Debug.LogWarning($"[ClientBulletSystem] SpawnServerBullet: no authState for attacker {attackerId}");
                return;
            }
            spawnPos = new Vector3(authState.Position.x, authState.Position.y + GameConstants.PlayerHeight * 0.85f, authState.Position.z);
        }
        else
        {
            Debug.LogWarning("[ClientBulletSystem] SpawnServerBullet: SpawnPos=zero and AuthoritySync.Instance=null");
            return;
        }

        int serverFrame = BattleClient.Instance?.ServerFrameId ?? 0;
        int spawnFrame = atk.ClientFrameId;
        int frameDiff = Mathf.Max(0, serverFrame - spawnFrame);
        frameDiff = Mathf.Min(frameDiff, GameConstants.MaxCompensationTicks);
        float catchupDistance = bulletSpeed * (frameDiff * GameConstants.TickDelta);

        SpawnAuthorityBullet(spawnPos, direction, atk.AttackId, attackerId, catchupDistance);
    }

    /// <summary>命中时按攻击 ID 移除子弹实体。</summary>
    public void RemoveBullet(int attackId)
    {
        var em = ClientECSWorld.Instance?.EntityManager;
        if (em == null) return;

        var bullets = new List<Entity>();
        em.GetEntitiesWith<VisualBulletComponent>(bullets);
        foreach (var entity in bullets)
        {
            if (!em.TryGetComponent<VisualBulletComponent>(entity, out var bullet)) continue;
            if (bullet.AttackId != attackId) continue;
            em.DestroyEntity(entity);
            ReturnBullet(bullet);
            return;
        }
    }

    public void ClearAll()
    {
        var em = ClientECSWorld.Instance?.EntityManager;
        if (em != null)
        {
            var bullets = new List<Entity>();
            em.GetEntitiesWith<VisualBulletComponent>(bullets);
            foreach (var entity in bullets)
            {
                if (em.TryGetComponent<VisualBulletComponent>(entity, out var bullet))
                    ReturnBullet(bullet);
                em.DestroyEntity(entity);
            }
        }
        _spawnedAttackKeys.Clear();
    }

    public int GetActiveBulletCount()
    {
        var em = ClientECSWorld.Instance?.EntityManager;
        if (em == null) return 0;
        var bullets = new List<Entity>();
        em.GetEntitiesWith<VisualBulletComponent>(bullets);
        return bullets.Count;
    }

    private void SpawnBullet(Vector3 origin, Vector3 direction, int attackId, int attackerId, float catchupDistance)
    {
        if (!TryMarkAttackSpawned(attackId, attackerId)) return;
        var go = GetBullet();
        if (go == null) return;

        go.transform.position = origin + direction.normalized * catchupDistance;

        var em = ClientECSWorld.Instance?.EntityManager;
        if (em == null) return;

        var entity = em.CreateEntity();
        em.AddComponent(entity, new VisualBulletComponent
        {
            GameObject = go,
            Transform = go.transform,
            Direction = direction.normalized,
            Speed = bulletSpeed,
            Time = catchupDistance / bulletSpeed,
            TraveledDistance = catchupDistance,
            MaxDistance = 200f + catchupDistance,
            Lifetime = bulletLifetime,
            AttackId = attackId,
            AttackerId = attackerId
        });

        _activeObjects.Add(go);
        if (TracerVFX.Instance != null) TracerVFX.Instance.SpawnTracer(origin, direction);
    }

    private GameObject GetBullet()
    {
        if (_bulletPool.Count > 0)
        {
            var go = _bulletPool.Dequeue();
            go.SetActive(true);
            return go;
        }
        if (_activeObjects.Count < maxBullets)
        {
            var go = CreateBullet();
            go.SetActive(true);
            return go;
        }
        // 复用最旧子弹
        var oldest = _activeObjects[0];
        _activeObjects.RemoveAt(0);
        var em = ClientECSWorld.Instance?.EntityManager;
        if (em != null)
        {
            var bullets = new List<Entity>();
            em.GetEntitiesWith<VisualBulletComponent>(bullets);
            foreach (var entity in bullets)
            {
                if (em.TryGetComponent<VisualBulletComponent>(entity, out var b) && b.GameObject == oldest)
                {
                    em.DestroyEntity(entity);
                    break;
                }
            }
        }
        oldest.SetActive(true);
        return oldest;
    }

    private GameObject CreateBullet()
    {
        GameObject go;
        if (bulletPrefab != null)
        {
            go = Instantiate(bulletPrefab, transform);
            foreach (var col in go.GetComponentsInChildren<Collider>(includeInactive: true))
                col.enabled = false;
        }
        else
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.transform.SetParent(transform);
            go.transform.localScale = Vector3.one * 0.3f;
            go.name = "VisualBullet_Fallback";
            var renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                var mat = new Material(Shader.Find("Standard")) { color = Color.yellow };
                mat.SetColor("_EmissionColor", Color.yellow * 0.5f);
                mat.EnableKeyword("_EMISSION");
                renderer.material = mat;
            }
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            Destroy(go.GetComponent<Collider>());
        }
        go.SetActive(false);
        _bulletPool.Enqueue(go);
        return go;
    }

    private void ReturnBullet(VisualBulletComponent bullet)
    {
        if (bullet.GameObject != null)
        {
            bullet.GameObject.SetActive(false);
            _activeObjects.Remove(bullet.GameObject);
            _bulletPool.Enqueue(bullet.GameObject);
        }
    }
}
