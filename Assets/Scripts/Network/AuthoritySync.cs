using System;
using System.Collections.Generic;
using UnityEngine;
using ShootingGame.Shared.Protocol;
using ShootingGame.Shared.Simulation;
using ShootingGame.Shared.GameplayTags;
using SharedVec3 = ShootingGame.Shared.Math.Vec3;

/// <summary>
/// Authority Sync handles authoritative state synchronization from server.
/// Manages position override, HP sync, death determination, and GameOver.
/// </summary>
public class AuthoritySync : MonoBehaviour
{
    [Header("Smoothing Settings")]
    [SerializeField] private float positionSmoothTime = 0.1f;
    [SerializeField] private float snapThreshold = 5f;
    [SerializeField] private float rotationSmoothSpeed = 10f;

    [Header("References")]
    [SerializeField] private NetPlayerController localPlayerController;
    [SerializeField] private RemotePlayerController[] remotePlayerControllers;

    // Player states from server
    private readonly Dictionary<int, AuthoritativePlayerState> _playerStates = new Dictionary<int, AuthoritativePlayerState>();

    // Attack dedup: track which authority attacks have already been spawned visually
    // Uses composite key (attackerId << 16 | attackId) since attack IDs are only unique per-client
    private readonly HashSet<long> _spawnedAuthorityAttacks = new HashSet<long>();
    private int _spawnedBulletCount = 0;
    private int _skippedPredictedCount = 0;
    private int _skippedDedupCount = 0;

    // Local prediction history
    private readonly Dictionary<int, PlayerSnapshot> _predictionHistory = new Dictionary<int, PlayerSnapshot>();
    private int _lastReconciledFrame = -1;

    // Game state
    private bool _isGameOver;
    private int _winnerTeamId;

    // Public accessors
    public bool IsGameOver => _isGameOver;
    public int WinnerTeamId => _winnerTeamId;
    public IReadOnlyDictionary<int, AuthoritativePlayerState> PlayerStates => _playerStates;

    // Events
    public event Action<int> OnPlayerDeath; // playerId
    public event Action<int> OnGameOver; // winnerTeamId
    public event Action<int, int> OnPlayerHpChanged; // playerId, newHp

    // Singleton
    public static AuthoritySync Instance { get; private set; }

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
        // Subscribe to BattleClient events
        if (BattleClient.Instance != null)
        {
            BattleClient.Instance.OnFrameReceived += OnFrameReceived;
            BattleClient.Instance.OnGameOver += OnGameOverReceived;
        }
    }

    private void OnDestroy()
    {
        if (BattleClient.Instance != null)
        {
            BattleClient.Instance.OnFrameReceived -= OnFrameReceived;
            BattleClient.Instance.OnGameOver -= OnGameOverReceived;
        }
    }

    /// <summary>
    /// Process a received frame from the server.
    /// </summary>
    private void OnFrameReceived(AllPlayerOperation frame)
    {
        // Process player states
        foreach (var state in frame.PlayerStates)
        {
            ProcessPlayerState(state);
        }

        // Spawn visual bullets from authority frames (Path B)
        // This ensures ALL clients see ALL bullets, not just their own
        SpawnVisualBulletsFromFrame(frame);
    }

    /// <summary>
    /// Spawn visual bullets from all attack operations in an authority frame.
    /// This is Path B (authority path) for visual bullets. Path A (local prediction)
    /// is handled in NetPlayerController.SendInputToServer.
    /// </summary>
    private void SpawnVisualBulletsFromFrame(AllPlayerOperation frame)
    {
        if (VisualBulletManager.Instance == null)
        {
            Debug.LogWarning("[AuthSync] VisualBulletManager.Instance is null, cannot spawn visual bullets");
            return;
        }

        int localPlayerId = BattleClient.Instance?.BattlePlayerId ?? -1;
        int serverFrameId = frame.FrameId;
        int totalOpsChecked = 0;
        int totalAttacksFound = 0;

        foreach (var op in frame.Operations)
        {
            totalOpsChecked++;
            if (op.AttackOperations == null || op.AttackOperations.Count == 0)
                continue;

            int attackerId = op.PlayerId;
            totalAttacksFound += op.AttackOperations.Count;

            foreach (var atk in op.AttackOperations)
            {
                long compositeKey = ((long)attackerId << 32) | (uint)atk.AttackId;

                // Dedup: skip attacks already spawned via local prediction (Path A)
                if (attackerId == localPlayerId &&
                    AttackManager.Instance != null &&
                    AttackManager.Instance.TryConsumePredictedAttack(atk.AttackId))
                {
                    _skippedPredictedCount++;
                    // 服务端权威帧已包含此攻击 → 确认攻击已被服务端处理
                    AttackManager.Instance.ConfirmAttack(atk.AttackId);
                    continue;
                }

                // Dedup: skip authority attacks we've already spawned
                if (_spawnedAuthorityAttacks.Contains(compositeKey))
                {
                    _skippedDedupCount++;
                    continue;
                }

                _spawnedAuthorityAttacks.Add(compositeKey);

                // 如果这是本地玩家的攻击（通过 Path B 权威路径），确认攻击已被服务端处理
                if (attackerId == localPlayerId && AttackManager.Instance != null)
                    AttackManager.Instance.ConfirmAttack(atk.AttackId);

                // Compute direction from AttackOperation's stored values
                float aimYaw = Mathf.Atan2(atk.TowardX, atk.TowardY) * Mathf.Rad2Deg;
                Vector3 fireDir = Quaternion.Euler(atk.AimPitch, aimYaw, 0f) * Vector3.forward;

                // 获取子弹生成位置。远程玩家使用插值后的视觉位置，确保子弹从对方可见的枪口射出
                Vector3 spawnPos;
                if (attackerId != localPlayerId)
                {
                    // 远程玩家：使用插值后的视觉位置 + 枪口偏移
                    var remoteCtrl = RemotePlayerController.GetPlayer(attackerId);
                    if (remoteCtrl != null)
                    {
                        spawnPos = remoteCtrl.GetFireOrigin();
                    }
                    else if (atk.SpawnPos.x != 0f || atk.SpawnPos.y != 0f || atk.SpawnPos.z != 0f)
                    {
                        spawnPos = new Vector3(atk.SpawnPos.x, atk.SpawnPos.y, atk.SpawnPos.z);
                    }
                    else if (_playerStates.TryGetValue(attackerId, out var authState))
                    {
                        spawnPos = new Vector3(authState.Position.x, authState.Position.y + GameConstants.PlayerHeight * 0.85f, authState.Position.z);
                    }
                    else
                    {
                        Debug.LogWarning($"[AuthSync] No remoteCtrl/SpawnPos/authState for attacker {attackerId}, attack {atk.AttackId}");
                        continue;
                    }
                }
                else
                {
                    // 本地玩家：使用 SpawnPos（Path B 权威回退）
                    if (atk.SpawnPos.x != 0f || atk.SpawnPos.y != 0f || atk.SpawnPos.z != 0f)
                    {
                        spawnPos = new Vector3(atk.SpawnPos.x, atk.SpawnPos.y, atk.SpawnPos.z);
                    }
                    else if (_playerStates.TryGetValue(attackerId, out var authState))
                    {
                        spawnPos = new Vector3(authState.Position.x, authState.Position.y + GameConstants.PlayerHeight * 0.85f, authState.Position.z);
                        Debug.LogWarning($"[AuthSync] SpawnPos was zero for local attack {atk.AttackId}, using authState fallback");
                    }
                    else
                    {
                        Debug.LogWarning($"[AuthSync] No SpawnPos and no authState for local attacker {attackerId}, attack {atk.AttackId}");
                        continue;
                    }
                }

                // 计算帧差补偿距离（使用 VisualBulletManager 的实际速度）
                int spawnFrame = atk.ClientFrameId;
                int frameDiff = Mathf.Max(0, serverFrameId - spawnFrame);
                frameDiff = Mathf.Min(frameDiff, GameConstants.MaxCompensationTicks);
                float bulletSpeed = VisualBulletManager.Instance != null ? VisualBulletManager.Instance.BulletSpeed : 100f;
                float advanceDistance = bulletSpeed * (frameDiff * GameConstants.TickDelta);

                // 子弹位置由 SpawnAuthorityBullet 内部做网络延迟补偿
                Vector3 visualSpawnPos = spawnPos;

                VisualBulletManager.Instance.SpawnAuthorityBullet(
                    visualSpawnPos, fireDir, atk.AttackId, attackerId, advanceDistance);
                _spawnedBulletCount++;

                // Log first 5 bullets and every 30th
                if (_spawnedBulletCount <= 5 || _spawnedBulletCount % 30 == 1)
                {
                    Debug.Log($"[AuthSync] Spawned visual bullet #{_spawnedBulletCount}: atkId={atk.AttackId} attacker={attackerId} serverFrame={serverFrameId} spawnFrame={spawnFrame} frameDiff={frameDiff} advance={advanceDistance:F1} pos={visualSpawnPos} dir={fireDir}");
                }
            }
        }

        // Clean old dedup entries
        if (_spawnedAuthorityAttacks.Count > 200)
        {
            var toRemove = new List<long>();
            int toKeep = 100;
            foreach (var key in _spawnedAuthorityAttacks)
            {
                if (toKeep-- <= 0) break;
                toRemove.Add(key);
            }
            // Actually, we want to remove the OLDEST, not the first iterated
            // Just clear all if too many
            if (_spawnedAuthorityAttacks.Count > 500)
            {
                _spawnedAuthorityAttacks.Clear();
                Debug.Log("[AuthSync] Cleared authority attack dedup set (size exceeded 500)");
            }
        }

        // Log summary: first 5 frames, then every 60 frames
        if (serverFrameId <= 5 || serverFrameId % 60 == 0)
        {
            Debug.Log($"[AuthSync] Frame {serverFrameId}: opsChecked={totalOpsChecked} attacksFound={totalAttacksFound} spawned={_spawnedBulletCount} skippedPredicted={_skippedPredictedCount} skippedDedup={_skippedDedupCount} dedupSetSize={_spawnedAuthorityAttacks.Count}");
        }
    }

    /// <summary>
    /// Process authoritative player state from server.
    /// </summary>
    private void ProcessPlayerState(PlayerStateMsg state)
    {
        int playerId = state.PlayerId;

        // Get or create authoritative state
        if (!_playerStates.ContainsKey(playerId))
        {
            _playerStates[playerId] = new AuthoritativePlayerState();
        }

        var authState = _playerStates[playerId];
        int oldHp = authState.Hp;
        bool wasDead = authState.IsDead;

        // Update state
        authState.PlayerId = playerId;
        authState.Position = state.Position;
        authState.Velocity = state.Velocity;
        authState.Hp = state.Hp;
        authState.IsDead = state.IsDead;
        authState.IsGrounded = state.IsGrounded;
        authState.StateEnum = state.StateEnum;
        authState.FireCooldown = state.FireCooldown;
        authState.LastUpdateTime = Time.unscaledTime;

        // Check if this is our local player
        bool isLocalPlayer = BattleClient.Instance != null && playerId == BattleClient.Instance.BattlePlayerId;

        if (isLocalPlayer)
        {
            // Reconcile local prediction
            ReconcileLocalPlayer(state);
        }
        else
        {
            // Update remote player visualization
            UpdateRemotePlayer(playerId, state);
        }

        // Check for HP change
        if (oldHp != authState.Hp)
        {
            Debug.Log($"[HP-CHANGE] playerId={playerId} oldHp={oldHp} newHp={authState.Hp} isLocal={isLocalPlayer}");
            OnPlayerHpChanged?.Invoke(playerId, authState.Hp);
        }

        // Check for death
        if (!wasDead && authState.IsDead)
        {
            if (isLocalPlayer && localPlayerController != null)
                localPlayerController.SetDead();
            OnPlayerDeath?.Invoke(playerId);
            Debug.Log($"[AuthoritySync] Player {playerId} died");
        }
        // Check for revive
        else if (wasDead && !authState.IsDead)
        {
            if (isLocalPlayer && localPlayerController != null)
            {
                Vector3 spawnPos = new Vector3(state.Position.x, state.Position.y, state.Position.z);
                localPlayerController.Revive(spawnPos);
            }
            Debug.Log($"[AuthoritySync] Player {playerId} revived");
        }
    }

    /// <summary>
    /// Reconcile local player prediction with authoritative state.
    /// </summary>
    private void ReconcileLocalPlayer(PlayerStateMsg serverState)
    {
        if (localPlayerController == null) return;

        // Get predicted state at server frame
        // Note: In full implementation, we'd store prediction history by frame
        // For now, we'll do a simple distance check

        var localPos = localPlayerController.transform.position;
        var serverPos = new Vector3(serverState.Position.x, serverState.Position.y, serverState.Position.z);
        float distance = Vector3.Distance(localPos, serverPos);

        if (distance > snapThreshold)
        {
            // Significant drift, snap to server position
            Debug.Log($"[AuthoritySync] Snapping local player to server position (drift: {distance:F2}m)");
            localPlayerController.transform.position = serverPos;
        }
        else if (distance > 0.1f)
        {
            // Small drift, smooth correction
            // Could implement gradual correction here
        }

        // Sync HP
        // The local player's HP should always match server
    }

    /// <summary>
    /// Update remote player visualization.
    /// </summary>
    private void UpdateRemotePlayer(int playerId, PlayerStateMsg state)
    {
        // Find remote player controller
        RemotePlayerController controller = GetRemotePlayerController(playerId);
        if (controller == null) return;

        // Update position with interpolation
        Vector3 targetPos = new Vector3(state.Position.x, state.Position.y, state.Position.z);
        controller.SetTargetPosition(targetPos);

        // Update HP
        controller.SetHp(state.Hp);

        // Stealth visibility: hide model if Buff.Invisible tag is active
        bool isStealthed = GameplayTagConfig.Tag_Buff_Invisible.Matches(state.TagBitmask);
        controller.SetVisible(!isStealthed);

        // Update death/revive state
        if (state.IsDead && !controller.IsDead)
        {
            controller.SetDead();
        }
        else if (!state.IsDead && controller.IsDead)
        {
            Vector3 spawnPos = new Vector3(state.Position.x, state.Position.y, state.Position.z);
            controller.Revive(spawnPos);
            controller.SetHp(state.Hp);
        }
    }

    private RemotePlayerController GetRemotePlayerController(int playerId)
    {
        if (remotePlayerControllers == null) return null;

        foreach (var controller in remotePlayerControllers)
        {
            if (controller.PlayerId == playerId)
                return controller;
        }

        return null;
    }

    /// <summary>
    /// Handle GameOver from server.
    /// </summary>
    private void OnGameOverReceived(int winnerTeamId)
    {
        _isGameOver = true;
        _winnerTeamId = winnerTeamId;

        // Determine if we won or lost
        bool isWinner = BattleClient.Instance != null && BattleClient.Instance.TeamId == winnerTeamId;

        Debug.Log($"[AuthoritySync] Game Over! Winner team: {winnerTeamId}. We {(isWinner ? "won" : "lost")}!");

        OnGameOver?.Invoke(winnerTeamId);
    }

    /// <summary>
    /// Get authoritative state for a player.
    /// </summary>
    public AuthoritativePlayerState GetPlayerState(int playerId)
    {
        return _playerStates.TryGetValue(playerId, out var state) ? state : null;
    }

    /// <summary>
    /// Get HP for a player.
    /// </summary>
    public int GetPlayerHp(int playerId)
    {
        return _playerStates.TryGetValue(playerId, out var state) ? state.Hp : 100;
    }

    /// <summary>
    /// Check if a player is dead.
    /// </summary>
    public bool IsPlayerDead(int playerId)
    {
        return _playerStates.TryGetValue(playerId, out var state) && state.IsDead;
    }

    /// <summary>
    /// Store prediction snapshot for reconciliation.
    /// </summary>
    public void StorePrediction(int frameId, PlayerSnapshot snapshot)
    {
        _predictionHistory[frameId] = snapshot;

        // Trim old predictions
        int maxHistory = 64;
        while (_predictionHistory.Count > maxHistory)
        {
            int oldestFrame = frameId - maxHistory;
            _predictionHistory.Remove(oldestFrame);
        }
    }

    /// <summary>
    /// Reset for a new battle.
    /// </summary>
    public void Reset()
    {
        _playerStates.Clear();
        _predictionHistory.Clear();
        _spawnedAuthorityAttacks.Clear();
        _lastReconciledFrame = -1;
        _isGameOver = false;
        _winnerTeamId = 0;
    }
}

/// <summary>
/// Authoritative player state from server.
/// </summary>
public class AuthoritativePlayerState
{
    public int PlayerId;
    public SharedVec3 Position;
    public SharedVec3 Velocity;
    public int Hp;
    public bool IsDead;
    public bool IsGrounded;
    public int StateEnum;
    public float FireCooldown;
    public float LastUpdateTime;
}