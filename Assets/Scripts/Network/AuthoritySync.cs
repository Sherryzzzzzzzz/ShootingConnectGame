using System;
using System.Collections.Generic;
using UnityEngine;
using ShootingGame.Shared.Protocol;
using ShootingGame.Shared.Simulation;
using ShootingGame.Shared.GameplayTags;
using ShootingGame.Shared.ECS;
using SharedVec3 = ShootingGame.Shared.Math.Vec3;

/// <summary>
/// 权威状态缓存层（L3 IO/编排层）。
/// 职责（ECS 化后瘦身）：
///  - 缓存服务端玩家状态（供查询 API）
///  - 远程玩家状态 → 委托 ClientRemoteInterpolationSystem 缓存进 ECS
///  - 本地玩家死亡/复活 → 委托 ClientECSWorld
///  - 权威帧子弹生成 → 委托 ClientBulletSystem
///  - 游戏结束事件
/// 状态查询 API（GetPlayerState/GetPlayerHp/IsPlayerDead）供表现层使用。
/// </summary>
public class AuthoritySync : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private RemotePlayerController[] remotePlayerControllers; // 兼容字段（表现层保留）

    // Player states from server
    private readonly Dictionary<int, AuthoritativePlayerState> _playerStates = new Dictionary<int, AuthoritativePlayerState>();

    // Attack dedup: 权威攻击已生成集合（委托给 ClientBulletSystem 后仅用于统计）
    private readonly HashSet<long> _spawnedAuthorityAttacks = new HashSet<long>();
    private int _spawnedBulletCount = 0;
    private int _skippedPredictedCount = 0;
    private int _skippedDedupCount = 0;

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

    /// <summary>处理服务端帧：缓存状态 + 分发到 ECS 系统。</summary>
    private void OnFrameReceived(AllPlayerOperation frame)
    {
        foreach (var state in frame.PlayerStates)
        {
            ProcessPlayerState(state);
        }

        SpawnVisualBulletsFromFrame(frame);
    }

    /// <summary>从权威帧生成视觉子弹（委托 ClientBulletSystem，Path B）。</summary>
    private void SpawnVisualBulletsFromFrame(AllPlayerOperation frame)
    {
        if (ClientBulletSystem.Instance == null)
        {
            Debug.LogWarning("[AuthSync] ClientBulletSystem.Instance is null, cannot spawn visual bullets");
            return;
        }

        int localPlayerId = BattleClient.Instance?.BattlePlayerId ?? -1;
        int serverFrameId = frame.FrameId;
        int totalAttacksFound = 0;

        foreach (var op in frame.Operations)
        {
            if (op.AttackOperations == null || op.AttackOperations.Count == 0)
                continue;

            int attackerId = op.PlayerId;
            totalAttacksFound += op.AttackOperations.Count;

            foreach (var atk in op.AttackOperations)
            {
                long compositeKey = ((long)attackerId << 32) | (uint)atk.AttackId;

                // 本地预测攻击：权威帧已包含 → 消费预测标记 + 确认攻击
                if (attackerId == localPlayerId && ClientECSWorld.Instance != null)
                {
                    var world = ClientECSWorld.Instance;
                    var entity = world.GetLocalPlayerEntity();
                    if (world.EntityManager.IsValid(entity) &&
                        ClientAttackSystem.TryConsumePredictedAttack(world.EntityManager, entity, atk.AttackId))
                    {
                        _skippedPredictedCount++;
                        ClientAttackSystem.ConfirmAttack(world.EntityManager, entity, atk.AttackId);
                        continue;
                    }
                }

                // 权威攻击去重（本地维护，子弹系统自己也有去重，双保险）
                if (_spawnedAuthorityAttacks.Contains(compositeKey))
                {
                    _skippedDedupCount++;
                    continue;
                }
                _spawnedAuthorityAttacks.Add(compositeKey);

                // 本地玩家攻击确认
                if (attackerId == localPlayerId && ClientECSWorld.Instance != null)
                {
                    var world = ClientECSWorld.Instance;
                    var entity = world.GetLocalPlayerEntity();
                    if (world.EntityManager.IsValid(entity))
                        ClientAttackSystem.ConfirmAttack(world.EntityManager, entity, atk.AttackId);
                }

                // 弹道方向
                float aimYaw = Mathf.Atan2(atk.TowardX, atk.TowardY) * Mathf.Rad2Deg;
                Vector3 fireDir = Quaternion.Euler(atk.AimPitch, aimYaw, 0f) * Vector3.forward;

                // 生成位置：远程用表现位置，本地用 SpawnPos/权威状态
                Vector3 spawnPos;
                if (attackerId != localPlayerId)
                {
                    var remote = GetRemoteFireOrigin(attackerId);
                    if (remote.HasValue)
                    {
                        spawnPos = remote.Value;
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
                        Debug.LogWarning($"[AuthSync] No spawn source for attacker {attackerId}, attack {atk.AttackId}");
                        continue;
                    }
                }
                else
                {
                    if (atk.SpawnPos.x != 0f || atk.SpawnPos.y != 0f || atk.SpawnPos.z != 0f)
                    {
                        spawnPos = new Vector3(atk.SpawnPos.x, atk.SpawnPos.y, atk.SpawnPos.z);
                    }
                    else if (_playerStates.TryGetValue(attackerId, out var authState))
                    {
                        spawnPos = new Vector3(authState.Position.x, authState.Position.y + GameConstants.PlayerHeight * 0.85f, authState.Position.z);
                    }
                    else
                    {
                        Debug.LogWarning($"[AuthSync] No SpawnPos for local attack {atk.AttackId}");
                        continue;
                    }
                }

                // 帧差补偿
                int spawnFrame = atk.ClientFrameId;
                int frameDiff = Mathf.Max(0, serverFrameId - spawnFrame);
                frameDiff = Mathf.Min(frameDiff, GameConstants.MaxCompensationTicks);
                float bulletSpeed = ClientBulletSystem.Instance != null ? ClientBulletSystem.Instance.BulletSpeed : 100f;
                float advanceDistance = bulletSpeed * (frameDiff * GameConstants.TickDelta);

                ClientBulletSystem.Instance.SpawnAuthorityBullet(spawnPos, fireDir, atk.AttackId, attackerId, advanceDistance);
                _spawnedBulletCount++;
            }
        }

        if (_spawnedAuthorityAttacks.Count > 500)
        {
            _spawnedAuthorityAttacks.Clear();
        }

        if (serverFrameId <= 5 || serverFrameId % 60 == 0)
        {
            Debug.Log($"[AuthSync] Frame {serverFrameId}: attacksFound={totalAttacksFound} spawned={_spawnedBulletCount} skippedPredicted={_skippedPredictedCount} skippedDedup={_skippedDedupCount}");
        }
    }

    /// <summary>远程玩家枪口位置（从 ECS 表现组件读取）。</summary>
    private Vector3? GetRemoteFireOrigin(int playerId)
    {
        var world = ClientECSWorld.Instance;
        if (world == null) return null;

        var entity = world.GetPlayerEntity(playerId);
        if (!world.EntityManager.IsValid(entity)) return null;
        if (!world.EntityManager.TryGetComponent<PlayerViewComponent>(entity, out var pv)) return null;
        if (pv.View == null) return null;

        var animView = pv.AnimationView ?? pv.View.GetComponent<PlayerAnimationView>();
        if (animView != null && animView.firePoint != null)
            return animView.firePoint.position;
        return pv.View.transform.position + Vector3.up * GameConstants.PlayerHeight * 0.85f;
    }

    /// <summary>处理服务端玩家状态：缓存 + 分发到 ECS 系统。</summary>
    private void ProcessPlayerState(PlayerStateMsg state)
    {
        int playerId = state.PlayerId;

        if (!_playerStates.ContainsKey(playerId))
            _playerStates[playerId] = new AuthoritativePlayerState();

        var authState = _playerStates[playerId];
        int oldHp = authState.Hp;
        bool wasDead = authState.IsDead;

        authState.PlayerId = playerId;
        authState.Position = state.Position;
        authState.Velocity = state.Velocity;
        authState.Hp = state.Hp;
        authState.IsDead = state.IsDead;
        authState.IsGrounded = state.IsGrounded;
        authState.StateEnum = state.StateEnum;
        authState.FireCooldown = state.FireCooldown;
        authState.LastUpdateTime = Time.unscaledTime;

        bool isLocalPlayer = BattleClient.Instance != null && playerId == BattleClient.Instance.BattlePlayerId;

        if (isLocalPlayer)
        {
            // 本地玩家：死亡/复活 → 委托 ClientECSWorld（和解由 ClientNetworkSyncSystem 处理）
            if (!wasDead && authState.IsDead)
            {
                ClientECSWorld.Instance?.SetDead();
            }
            else if (wasDead && !authState.IsDead)
            {
                Vector3 spawnPos = new Vector3(state.Position.x, state.Position.y, state.Position.z);
                ClientECSWorld.Instance?.Revive(spawnPos);
            }
        }
        else
        {
            // 远程玩家：缓存进 ECS 插值系统 + HP 同步 + 隐身可见性
            var world = ClientECSWorld.Instance;
            if (world != null)
            {
                var entity = world.GetPlayerEntity(playerId);
                if (world.EntityManager.IsValid(entity))
                {
                    ClientRemoteInterpolationSystem.CacheFrame(world.EntityManager, entity, state);
                    ClientRemoteInterpolationSystem.SyncHp(world.EntityManager, entity, state.Hp);
                    ApplyRemoteVisibility(world.EntityManager, entity, state);
                }
            }
        }

        if (oldHp != authState.Hp)
        {
            Debug.Log($"[HP-CHANGE] playerId={playerId} oldHp={oldHp} newHp={authState.Hp} isLocal={isLocalPlayer}");
            OnPlayerHpChanged?.Invoke(playerId, authState.Hp);
        }

        if (!wasDead && authState.IsDead)
        {
            OnPlayerDeath?.Invoke(playerId);
            Debug.Log($"[AuthoritySync] Player {playerId} died");
        }
    }

    /// <summary>远程玩家隐身（Stealth 标签）→ 表现层隐藏模型。</summary>
    private void ApplyRemoteVisibility(EntityManager em, Entity entity, PlayerStateMsg state)
    {
        if (!em.HasComponent<PlayerViewComponent>(entity)) return;
        var pv = em.GetComponent<PlayerViewComponent>(entity);
        if (pv.View == null) return;

        bool isStealthed = GameplayTagConfig.Tag_Buff_Invisible.Matches(state.TagBitmask);
        foreach (var r in pv.View.GetComponentsInChildren<Renderer>(true))
        {
            if (r != null) r.enabled = !isStealthed;
        }
    }

    /// <summary>处理游戏结束。</summary>
    private void OnGameOverReceived(int winnerTeamId)
    {
        _isGameOver = true;
        _winnerTeamId = winnerTeamId;

        bool isWinner = BattleClient.Instance != null && BattleClient.Instance.TeamId == winnerTeamId;
        Debug.Log($"[AuthoritySync] Game Over! Winner team: {winnerTeamId}. We {(isWinner ? "won" : "lost")}!");
        OnGameOver?.Invoke(winnerTeamId);
    }

    // ==================== 查询 API（表现层使用） ====================

    public AuthoritativePlayerState GetPlayerState(int playerId)
    {
        return _playerStates.TryGetValue(playerId, out var state) ? state : null;
    }

    public int GetPlayerHp(int playerId)
    {
        return _playerStates.TryGetValue(playerId, out var state) ? state.Hp : 100;
    }

    public bool IsPlayerDead(int playerId)
    {
        return _playerStates.TryGetValue(playerId, out var state) && state.IsDead;
    }

    /// <summary>战斗重置。</summary>
    public void Reset()
    {
        _playerStates.Clear();
        _spawnedAuthorityAttacks.Clear();
        _spawnedBulletCount = 0;
        _skippedPredictedCount = 0;
        _skippedDedupCount = 0;
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
