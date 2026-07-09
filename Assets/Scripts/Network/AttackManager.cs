using System.Collections.Generic;
using UnityEngine;
using ShootingGame.Shared.Protocol;
using ShootingGame.Shared.Simulation;

/// <summary>
/// Attack Manager handles client-side attack retransmission.
/// Stores pending attacks and resends them until confirmed by server.
/// </summary>
public class AttackManager : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private int maxPendingAttacks = 32;
    [SerializeField] private float resendInterval = 0.05f; // 50ms
    [SerializeField] private int maxResendAttempts = 20; // ~1 second at 50ms

    // Pending attacks queue
    private readonly Dictionary<int, PendingAttack> _pendingAttacks = new Dictionary<int, PendingAttack>();

    // Attack dedup: tracks locally predicted bullet attack IDs to prevent double-spawning from authority frames
    private readonly HashSet<int> _predictedBulletAttackIds = new HashSet<int>();

    // Attack ID counter
    private int _nextAttackId = 1;

    // Last fire time (for fire rate limiting)
    private float _lastFireTime;
    private float _fireCooldown;

    // Guard: prevent attacks until first server frame is received
    private bool _hasReceivedServerFrame;

    // Singleton
    public static AttackManager Instance { get; private set; }

    public int PendingAttackCount => _pendingAttacks.Count;

    public bool HasReceivedServerFrame
    {
        get => _hasReceivedServerFrame;
        set => _hasReceivedServerFrame = value;
    }

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

    private void Update()
    {
        // Resend pending attacks periodically
        ResendPendingAttacks();

        // Update cooldown
        if (_fireCooldown > 0)
            _fireCooldown -= Time.deltaTime;
    }

    /// <summary>
    /// Try to create a new attack (respects fire rate).
    /// </summary>
    public bool TryCreateAttack(float aimYaw, float aimPitch, int clientFrameId, out AttackOperation attack)
    {
        attack = null;

        // Check fire cooldown
        if (_fireCooldown > 0)
            return false;

        // Check if we have too many pending attacks
        if (_pendingAttacks.Count >= maxPendingAttacks)
        {
            Debug.LogWarning("[AttackManager] Too many pending attacks");
            return false;
        }

        // Create attack
        int attackId = _nextAttackId++;
        attack = new AttackOperation
        {
            AttackId = attackId,
            TowardX = Mathf.Sin(aimYaw * Mathf.Deg2Rad), // Horizontal direction
            TowardY = Mathf.Cos(aimYaw * Mathf.Deg2Rad), // Forward direction
            AimPitch = aimPitch,
            ClientFrameId = clientFrameId
        };

        // Add to pending
        _pendingAttacks[attackId] = new PendingAttack
        {
            Attack = attack,
            AimYaw = aimYaw,
            AimPitch = aimPitch,
            SendTime = Time.unscaledTime,
            ResendAttempts = 0
        };

        // Set cooldown
        _fireCooldown = GameConstants.FireRate;
        _lastFireTime = Time.unscaledTime;

        Debug.Log($"[AttackManager] Created attack {attackId} at frame {clientFrameId}");
        return true;
    }

    /// <summary>
    /// Confirm an attack was received by the server.
    /// </summary>
    public void ConfirmAttack(int attackId)
    {
        if (_pendingAttacks.Remove(attackId))
        {
            Debug.Log($"[AttackManager] Attack {attackId} confirmed");
        }
    }

    /// <summary>
    /// Get all pending attacks to include in the next operation packet.
    /// </summary>
    public List<AttackOperation> GetPendingAttacks()
    {
        var attacks = new List<AttackOperation>();
        foreach (var pending in _pendingAttacks.Values)
        {
            attacks.Add(pending.Attack);
        }
        return attacks;
    }

    /// <summary>
    /// Check if we can fire now.
    /// </summary>
    public bool CanFire()
    {
        return _hasReceivedServerFrame && _fireCooldown <= 0 && _pendingAttacks.Count < maxPendingAttacks;
    }

    /// <summary>
    /// Get the fire cooldown remaining.
    /// </summary>
    public float GetFireCooldown()
    {
        return Mathf.Max(0, _fireCooldown);
    }

    private void ResendPendingAttacks()
    {
        float now = Time.unscaledTime;
        var toRemove = new List<int>();

        foreach (var kvp in _pendingAttacks)
        {
            var pending = kvp.Value;

            // Check if it's time to resend
            if (now - pending.SendTime < resendInterval)
                continue;

            // Check if we've exceeded max attempts
            if (pending.ResendAttempts >= maxResendAttempts)
            {
                toRemove.Add(kvp.Key);
                Debug.LogWarning($"[AttackManager] Attack {kvp.Key} timed out after {pending.ResendAttempts} attempts");
                continue;
            }

            // Resend via BattleClient
            if (BattleClient.Instance != null && BattleClient.Instance.IsInBattle)
            {
                var operation = new PlayerOperation
                {
                    PlayerId = BattleClient.Instance.BattlePlayerId,
                    AttackOperations = new List<AttackOperation> { pending.Attack }
                };

                BattleClient.Instance.SendOperation(operation, BattleClient.Instance.ClientFrameId);
            }

            // Update send time and attempts
            pending.SendTime = now;
            pending.ResendAttempts++;
            _pendingAttacks[kvp.Key] = pending;
        }

        // Remove timed out attacks
        foreach (var id in toRemove)
        {
            _pendingAttacks.Remove(id);
        }
    }

    /// <summary>
    /// Clear all pending attacks.
    /// </summary>
    public void Clear()
    {
        _pendingAttacks.Clear();
        _predictedBulletAttackIds.Clear();
        _nextAttackId = 1;
        _fireCooldown = 0;
        _hasReceivedServerFrame = false;
    }

    /// <summary>
    /// Mark an attack as locally predicted (Path A). Authority frames will skip spawning this attack.
    /// </summary>
    public void MarkAttackPredicted(int attackId)
    {
        _predictedBulletAttackIds.Add(attackId);
    }

    /// <summary>
    /// Check and consume a predicted attack. Returns true if the attack was locally predicted
    /// (and should be skipped by authority frame bullet spawning).
    /// </summary>
    public bool TryConsumePredictedAttack(int attackId)
    {
        if (_predictedBulletAttackIds.Contains(attackId))
        {
            _predictedBulletAttackIds.Remove(attackId);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Check if an attack was already spawned (either locally predicted or already spawned from authority).
    /// </summary>
    public bool IsAttackAlreadySpawned(int attackId)
    {
        return _predictedBulletAttackIds.Contains(attackId);
    }

    private struct PendingAttack
    {
        public AttackOperation Attack;
        public float AimYaw;
        public float AimPitch;
        public float SendTime;
        public int ResendAttempts;
    }
}