using System;
using System.Collections.Generic;
using UnityEngine;
using ShootingGame.Shared.Protocol;

/// <summary>
/// 客户端命中事件系统（替代 HitEventManager）。
/// 命中去重 + 攻击确认 + 远程射击动画触发 + 表现分发（特效/音效/弹孔/UI/相机震动）。
/// 纯逻辑：表现播放通过现有工具（EffectPool/AudioPoolManager/BulletHoleManager/HitFeedbackUI）。
/// </summary>
public static class ClientHitEventSystem
{
    // 去重集合（attackId*10000 + victimId）
    private static readonly HashSet<long> _processedEvents = new HashSet<long>();
    private static readonly Queue<long> _processedQueue = new Queue<long>();
    private const int MaxProcessedEvents = 100;

    private static readonly List<HitEventMsg> _recentHits = new List<HitEventMsg>();
    private const int MaxRecentHits = 10;

    /// <summary>UI 订阅用事件（替代 HitEventManager.OnHitEvent）。</summary>
    public static event Action<HitEventMsg> OnHitEvent;

    public static IReadOnlyList<HitEventMsg> RecentHits => _recentHits;

    /// <summary>处理服务端命中事件（BattleClient.OnHitEvent 驱动）。</summary>
    public static void ProcessHitEvent(HitEventMsg hitEvent)
    {
        long key = (long)hitEvent.AttackId * 10000 + hitEvent.VictimId;
        if (_processedEvents.Contains(key))
        {
            Debug.Log($"[ClientHitEventSystem] Duplicate hit event ignored: attack={hitEvent.AttackId} victim={hitEvent.VictimId}");
            return;
        }

        _processedEvents.Add(key);
        _processedQueue.Enqueue(key);
        while (_processedQueue.Count > MaxProcessedEvents)
        {
            long oldKey = _processedQueue.Dequeue();
            _processedEvents.Remove(oldKey);
        }

        _recentHits.Add(hitEvent);
        if (_recentHits.Count > MaxRecentHits)
            _recentHits.RemoveAt(0);

        // 远程玩家射击动画：攻击者的 RemotePlayerController 表现桥接触发 FireTrigger
        if (BattleManager.Instance != null)
        {
            var remotePlayers = BattleManager.Instance.RemotePlayers;
            if (remotePlayers.TryGetValue(hitEvent.AttackerId, out var remoteGo) && remoteGo != null)
            {
                var view = remoteGo.GetComponent<PlayerAnimationView>();
                if (view != null)
                {
                    var em = ClientECSWorld.Instance?.EntityManager;
                    if (em != null)
                    {
                        var entity = ClientECSWorld.Instance.GetPlayerEntity(hitEvent.AttackerId);
                        if (em.IsValid(entity) && em.HasComponent<PlayerViewComponent>(entity))
                        {
                            var pv = em.GetComponent<PlayerViewComponent>(entity);
                            pv.FireTrigger = true;
                            em.SetComponent(entity, pv);
                        }
                    }
                }
            }
        }

        // 攻击者确认：我们打出的攻击被服务端记录命中 → 移除 pending
        if (BattleClient.Instance != null && hitEvent.AttackerId == BattleClient.Instance.BattlePlayerId)
        {
            var world = ClientECSWorld.Instance;
            var em = world != null ? world.EntityManager : null;
            var entity = world != null ? world.GetPlayerEntity(hitEvent.AttackerId) : default;
            if (em != null && em.IsValid(entity))
                ClientAttackSystem.ConfirmAttack(em, entity, hitEvent.AttackId);
        }

        // 表现分发
        PlayHitEffects(hitEvent);

        // UI / 击杀信息
        OnHitEvent?.Invoke(hitEvent);

        // 命中移除视觉子弹
        if (ClientBulletSystem.Instance != null)
            ClientBulletSystem.Instance.RemoveBullet(hitEvent.AttackId);

        Debug.Log($"[ClientHitEventSystem] Hit processed: attacker={hitEvent.AttackerId} victim={hitEvent.VictimId} damage={hitEvent.Damage} isKill={hitEvent.IsKill}");
    }

    private static void PlayHitEffects(HitEventMsg hitEvent)
    {
        Vector3 hitPoint = new Vector3(hitEvent.HitPoint.x, hitEvent.HitPoint.y, hitEvent.HitPoint.z);

        if (!hitEvent.IsKill)
            BulletHoleManager.Spawn(hitPoint, ResolveSurfaceNormal(hitPoint));

        bool effectSpawned = false;
        if (EffectPool.Instance != null)
        {
            var explosion = EffectPool.Instance.SpawnEffect("Explosion", hitPoint, Quaternion.identity);
            if (explosion != null)
                effectSpawned = true;
        }

        if (!effectSpawned)
        {
            if (HitEventView.Instance != null)
                HitEventView.Instance.PlayFallbackEffect(hitEvent, hitPoint);
        }

        if (HitEventView.Instance != null)
            HitEventView.Instance.PlayHitSound(hitEvent);

        // 受害者是本地玩家 → 相机震动
        if (BattleClient.Instance != null && hitEvent.VictimId == BattleClient.Instance.BattlePlayerId)
        {
            HitEventView.Instance?.ShakeCamera();
        }
    }

    /// <summary>从命中点向相机方向做短射线，获取命中表面真实法线（弹孔贴墙用）。</summary>
    private static Vector3 ResolveSurfaceNormal(Vector3 hitPoint)
    {
        var cam = Camera.main != null ? Camera.main : UnityEngine.Object.FindFirstObjectByType<Camera>();
        if (cam != null)
        {
            Vector3 dirToCam = (cam.transform.position - hitPoint).normalized;
            if (Physics.Raycast(hitPoint - dirToCam * 0.15f, dirToCam, out var hit, 0.5f))
                return hit.normal;
        }
        return Vector3.up;
    }

    public static bool HasAttackHit(int attackId)
    {
        foreach (var hit in _recentHits)
            if (hit.AttackId == attackId) return true;
        return false;
    }

    public static List<HitEventMsg> GetHitsByAttacker(int attackerId)
    {
        var hits = new List<HitEventMsg>();
        foreach (var hit in _recentHits)
            if (hit.AttackerId == attackerId) hits.Add(hit);
        return hits;
    }

    public static List<HitEventMsg> GetHitsOnVictim(int victimId)
    {
        var hits = new List<HitEventMsg>();
        foreach (var hit in _recentHits)
            if (hit.VictimId == victimId) hits.Add(hit);
        return hits;
    }

    public static void Clear()
    {
        _processedEvents.Clear();
        _processedQueue.Clear();
        _recentHits.Clear();
    }
}

/// <summary>
/// 命中表现薄壳（替代 HitEventManager 的表现部分：特效/音效/相机震动）。
/// 挂场景单例，由 ClientHitEventSystem 调用。
/// </summary>
public class HitEventView : MonoBehaviour
{
    public static HitEventView Instance { get; private set; }

    /// <summary>UI 订阅事件（转发 ClientHitEventSystem.OnHitEvent）。</summary>
    public event System.Action<HitEventMsg> OnHitEvent
    {
        add => ClientHitEventSystem.OnHitEvent += value;
        remove => ClientHitEventSystem.OnHitEvent -= value;
    }

    [Header("Visual Effects")]
    [SerializeField] private GameObject hitEffectPrefab;
    [SerializeField] private GameObject killEffectPrefab;
    [SerializeField] private float hitEffectDuration = 1f;

    [Header("Audio")]
    [SerializeField] private AudioClip hitSound;
    [SerializeField] private AudioClip killSound;
    [SerializeField] private AudioSource audioSource;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
    }

    /// <summary>回退特效（EffectPool 缺失时）。</summary>
    public void PlayFallbackEffect(HitEventMsg hitEvent, Vector3 hitPoint)
    {
        if (!hitEvent.IsKill)
        {
            if (hitEffectPrefab != null)
            {
                var effect = Instantiate(hitEffectPrefab, hitPoint, Quaternion.identity);
                Destroy(effect, hitEffectDuration);
            }
            else
            {
                Debug.LogWarning("[HitEventView] 未找到 Explosion 特效且 hitEffectPrefab 为空，跳过命中特效");
            }
        }
        else
        {
            if (killEffectPrefab != null)
            {
                var effect = Instantiate(killEffectPrefab, hitPoint, Quaternion.identity);
                Destroy(effect, hitEffectDuration);
            }
            else
            {
                Debug.LogWarning("[HitEventView] 未找到 Explosion 特效且 killEffectPrefab 为空，跳过击杀特效");
            }
        }
    }

    public void PlayHitSound(HitEventMsg hitEvent)
    {
        if (audioSource == null) return;
        var clip = hitEvent.IsKill ? killSound : hitSound;
        if (clip != null) audioSource.PlayOneShot(clip);
    }

    /// <summary>本地玩家受击相机震动。</summary>
    public void ShakeCamera()
    {
        var camera = Camera.main;
        if (camera != null)
            StartCoroutine(ShakeCoroutine(camera, 0.1f, 0.2f));
    }

    private System.Collections.IEnumerator ShakeCoroutine(Camera camera, float duration, float magnitude)
    {
        Vector3 originalPos = camera.transform.localPosition;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            float x = UnityEngine.Random.Range(-1f, 1f) * magnitude;
            float y = UnityEngine.Random.Range(-1f, 1f) * magnitude;
            camera.transform.localPosition = new Vector3(originalPos.x + x, originalPos.y + y, originalPos.z);
            elapsed += Time.deltaTime;
            yield return null;
        }
        camera.transform.localPosition = originalPos;
    }
}
