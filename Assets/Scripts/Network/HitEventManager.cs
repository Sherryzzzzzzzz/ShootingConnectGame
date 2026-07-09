using System;
using System.Collections.Generic;
using UnityEngine;
using ShootingGame.Shared.Protocol;

/// <summary>
/// Hit Event Manager handles hit event processing and visual feedback.
/// Separates hit visualization from authoritative HP sync.
/// </summary>
public class HitEventManager : MonoBehaviour
{
    [Header("Visual Effects")]
    [SerializeField] private GameObject hitEffectPrefab;
    [SerializeField] private GameObject killEffectPrefab;
    [SerializeField] private float hitEffectDuration = 1f;

    [Header("Audio")]
    [SerializeField] private AudioClip hitSound;
    [SerializeField] private AudioClip killSound;
    [SerializeField] private AudioSource audioSource;

    [Header("UI")]
    [SerializeField] private HitFeedbackUI hitFeedbackUI;

    // Processed hit events (for deduplication)
    private readonly HashSet<long> _processedEvents = new HashSet<long>();
    private readonly Queue<long> _processedQueue = new Queue<long>();
    private const int MaxProcessedEvents = 100;

    // Recent hits for UI display
    private readonly List<HitEventMsg> _recentHits = new List<HitEventMsg>();
    private const int MaxRecentHits = 10;

    // Singleton
    public static HitEventManager Instance { get; private set; }

    // Event for UI subscribers
    public event Action<HitEventMsg> OnHitEvent;

    public IReadOnlyList<HitEventMsg> RecentHits => _recentHits;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();
    }

    private void Start()
    {
        if (BattleClient.Instance != null)
        {
            BattleClient.Instance.OnHitEvent += ProcessHitEvent;
        }
    }

    private void OnDestroy()
    {
        if (BattleClient.Instance != null)
        {
            BattleClient.Instance.OnHitEvent -= ProcessHitEvent;
        }
    }

    /// <summary>
    /// Process a hit event from the server.
    /// </summary>
    public void ProcessHitEvent(HitEventMsg hitEvent)
    {
        // Create unique key for deduplication
        long key = (long)hitEvent.AttackId * 10000 + hitEvent.VictimId;

        // Check if already processed
        if (_processedEvents.Contains(key))
        {
            Debug.Log($"[HitEventManager] Duplicate hit event ignored: attack={hitEvent.AttackId} victim={hitEvent.VictimId}");
            return;
        }

        // Mark as processed
        _processedEvents.Add(key);
        _processedQueue.Enqueue(key);

        // Trim old events
        while (_processedQueue.Count > MaxProcessedEvents)
        {
            long oldKey = _processedQueue.Dequeue();
            _processedEvents.Remove(oldKey);
        }

        // Add to recent hits
        _recentHits.Add(hitEvent);
        if (_recentHits.Count > MaxRecentHits)
            _recentHits.RemoveAt(0);

        // Play visual effects
        PlayHitEffects(hitEvent);

        // Notify UI
        if (hitFeedbackUI != null)
        {
            hitFeedbackUI.ShowHitFeedback(hitEvent);
        }

        // Fire event for subscribers (like BattleUI)
        OnHitEvent?.Invoke(hitEvent);

        Debug.Log($"[HitEventManager] Hit processed: attacker={hitEvent.AttackerId} victim={hitEvent.VictimId} damage={hitEvent.Damage} isKill={hitEvent.IsKill}");
    }

    private void PlayHitEffects(HitEventMsg hitEvent)
    {
        // Get victim position (from remote player controller or local)
        Vector3 hitPoint = new Vector3(hitEvent.HitPoint.x, hitEvent.HitPoint.y, hitEvent.HitPoint.z);

        // 使用 EffectPool 中的 Explosion 特效
        bool effectSpawned = false;
        if (EffectPool.Instance != null)
        {
            var explosion = EffectPool.Instance.SpawnEffect("Explosion", hitPoint, Quaternion.identity);
            if (explosion != null)
                effectSpawned = true;
        }

        // 回退到自定义 prefab 或记录警告
        if (!effectSpawned)
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
                    Debug.LogWarning("[HitEventManager] 未找到 Explosion 特效且 hitEffectPrefab 为空，跳过命中特效");
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
                    Debug.LogWarning("[HitEventManager] 未找到 Explosion 特效且 killEffectPrefab 为空，跳过击杀特效");
                }
            }
        }

        // Play sound
        if (audioSource != null)
        {
            var clip = hitEvent.IsKill ? killSound : hitSound;
            if (clip != null)
            {
                audioSource.PlayOneShot(clip);
            }
        }

        // If we're the attacker, confirm the attack
        if (BattleClient.Instance != null && hitEvent.AttackerId == BattleClient.Instance.BattlePlayerId)
        {
            AttackManager.Instance?.ConfirmAttack(hitEvent.AttackId);
        }

        // If we're the victim, play hurt animation
        if (BattleClient.Instance != null && hitEvent.VictimId == BattleClient.Instance.BattlePlayerId)
        {
            PlayHurtEffect(hitEvent);
        }

        // Remove visual bullet
        if (VisualBulletManager.Instance != null)
        {
            VisualBulletManager.Instance.RemoveBullet(hitEvent.AttackId);
        }
    }

    private void PlayHurtEffect(HitEventMsg hitEvent)
    {
        // Play screen shake, red flash, etc.
        // This is visual feedback for being hit

        // Camera shake
        var camera = Camera.main;
        if (camera != null)
        {
            // Simple shake effect
            StartCoroutine(ShakeCamera(camera, 0.1f, 0.2f));
        }

        // Red flash vignette
        // Could implement via post-processing or UI overlay

        // Hurt sound
        // audioSource?.PlayOneShot(hurtSound);
    }

    private System.Collections.IEnumerator ShakeCamera(Camera camera, float duration, float magnitude)
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

    /// <summary>
    /// Check if an attack has hit (for attack confirmation).
    /// </summary>
    public bool HasAttackHit(int attackId)
    {
        foreach (var hit in _recentHits)
        {
            if (hit.AttackId == attackId)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Get all hits by a specific attacker.
    /// </summary>
    public List<HitEventMsg> GetHitsByAttacker(int attackerId)
    {
        var hits = new List<HitEventMsg>();
        foreach (var hit in _recentHits)
        {
            if (hit.AttackerId == attackerId)
                hits.Add(hit);
        }
        return hits;
    }

    /// <summary>
    /// Get all hits on a specific victim.
    /// </summary>
    public List<HitEventMsg> GetHitsOnVictim(int victimId)
    {
        var hits = new List<HitEventMsg>();
        foreach (var hit in _recentHits)
        {
            if (hit.VictimId == victimId)
                hits.Add(hit);
        }
        return hits;
    }

    /// <summary>
    /// Clear all hit events (for new battle).
    /// </summary>
    public void Clear()
    {
        _processedEvents.Clear();
        _processedQueue.Clear();
        _recentHits.Clear();
    }
}