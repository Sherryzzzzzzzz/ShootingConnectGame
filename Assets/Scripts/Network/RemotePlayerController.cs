using System.Collections.Generic;
using UnityEngine;
using Animancer;
using Unity.Cinemachine;
using ShootingGame.Shared.Hero;
using ShootingGame.Shared.Protocol;
using ShootingGame.Shared.Simulation;
using ShootingGame.Shared.Math;
using ShootingGame.Shared.ECS;

/// <summary>
/// 远程玩家控制器。接收服务端快照，插值位置/旋转，驱动动画。
/// 支持新的 BattleClient 和权威帧同步系统。
/// </summary>
public class RemotePlayerController : MonoBehaviour
{
    [Header("玩家标识")]
    [SerializeField] private int playerId = -1;
    [SerializeField] private int teamId = 0;

    [Header("插值设置")]
    [SerializeField] private float interpolationDelay = 0.1f; // 100ms
    [SerializeField] private float positionSmoothSpeed = 10f;
    [SerializeField] private float rotationSmoothSpeed = 720f;
    [SerializeField] private float snapThreshold = 3f;

    [Header("引用")]
    [SerializeField] private PlayerModel playerModel;
    [SerializeField] private AnimancerComponent animancer;
    [SerializeField] private PlayerAnimationSet animSet;
    [SerializeField] private Renderer[] renderers;

    public PlayerModel PlayerModel => playerModel;

    // 插值缓冲
    private InterpolationBuffer _interpBuffer;

    // 目标状态（从权威帧）
    private Vector3 _targetPosition;
    private Quaternion _targetRotation;
    private Vec3 _targetVelocity;
    private float _targetVerticalVelocity;
    private bool _targetIsGrounded;
    private bool _hasTarget;

    // 当前状态
    private int _currentHp = 100;
    private bool _isDead;
    private PlayerAnimationState _lastAnimState = PlayerAnimationState.idle;
    private LinearMixerState _moveMixer;

    // 渲染器颜色（队伍）
    private MaterialPropertyBlock _propBlock;
    private static readonly int ColorProp = Shader.PropertyToID("_Color");

    // 公开属性
    public int PlayerId => playerId;
    public int TeamId => teamId;
    public int CurrentHp => _currentHp;
    public bool IsDead => _isDead;

    // 单例管理所有远程玩家
    private static readonly Dictionary<int, RemotePlayerController> _allRemotePlayers = new Dictionary<int, RemotePlayerController>();
    public static IReadOnlyDictionary<int, RemotePlayerController> AllRemotePlayers => _allRemotePlayers;

    private void Awake()
    {
        _interpBuffer = new InterpolationBuffer();
        _propBlock = new MaterialPropertyBlock();

        // 禁用 root motion：联网角色位置由插值驱动，Animator 不应移动角色
        var anim = GetComponent<Animator>();
        if (anim != null)
            anim.applyRootMotion = false;

        // 远程玩家不需要摄像机，禁用所有 FreeLook 摄像机
        var freeLooks = GetComponentsInChildren<CinemachineFreeLook>();
        foreach (var fl in freeLooks)
        {
            fl.gameObject.SetActive(false);
        }

    }

    private void Start()
    {
        // 订阅帧接收事件（Start 时 BattleClient.Instance 已就绪）
        if (BattleClient.Instance != null)
        {
            BattleClient.Instance.OnFrameReceived += OnFrameReceived;
        }

        // 尝试查找组件（从 Player 预制体的 PlayerModel 获取引用）
        if (playerModel == null) playerModel = GetComponent<PlayerModel>();
        if (animancer == null)
        {
            animancer = GetComponent<AnimancerComponent>();
            if (animancer == null) animancer = GetComponentInChildren<AnimancerComponent>();
        }
        if (animSet == null && playerModel != null)
        {
            animSet = playerModel.AnimationSet;
        }
        if (renderers == null || renderers.Length == 0)
            renderers = GetComponentsInChildren<Renderer>();

        // 构建移动混合器
        if (animSet != null && animancer != null)
        {
            var idle = animSet.GetClip(PlayerAnimType.Rifle_Idle);
            var walk = animSet.GetClip(PlayerAnimType.Rifle_WalkFwdLoop);
            var run = animSet.GetClip(PlayerAnimType.Rifle_RunFwdLoop);

            if (idle != null && walk != null && run != null)
            {
                _moveMixer = new LinearMixerState
                {
                    { idle, 0f },
                    { walk, 1f },
                    { run, 2f }
                };
            }
        }

        // 初始化目标位置
        _targetPosition = transform.position;
        _targetRotation = transform.rotation;
    }

    private void OnDestroy()
    {
        // 取消订阅
        if (BattleClient.Instance != null)
        {
            BattleClient.Instance.OnFrameReceived -= OnFrameReceived;
        }

        // 从管理器移除
        if (playerId >= 0)
        {
            _allRemotePlayers.Remove(playerId);
            ClientECSWorld.Instance?.UnregisterPlayer(playerId);
        }
    }

    private void Update()
    {
        if (!_hasTarget) return;

        // 从插值缓冲采样
        float renderTime = Time.unscaledTime - interpolationDelay;
        if (_interpBuffer.Sample(renderTime, out var from, out var to, out float t))
        {
            // 插值位置和旋转
            _targetPosition = Vec3.Lerp(from.Position, to.Position, t).ToUnity();
            _targetRotation = Quat.Slerp(from.Rotation, to.Rotation, t).ToUnity();
            _targetVelocity = Vec3.Lerp(from.Velocity, to.Velocity, t);
            _targetVerticalVelocity = Mathf.Lerp(from.VerticalVelocity, to.VerticalVelocity, t);
            _targetIsGrounded = to.IsGrounded;

            // 驱动动画
            DriveAnimation(from, to, t);
        }

        // 平滑移动到目标位置
        float distance = Vector3.Distance(transform.position, _targetPosition);
        if (distance > snapThreshold)
        {
            transform.position = _targetPosition;
        }
        else
        {
            transform.position = Vector3.Lerp(transform.position, _targetPosition, Time.deltaTime * positionSmoothSpeed);
        }

        // 诊断日志: _targetRotation
        if (++_debugLogFrame <= 5 || (_debugLogFrame % 60 == 0 && _debugLogFrame <= 300))
        {
            float targetYaw = _targetRotation.eulerAngles.y;
            float currentYaw = transform.rotation.eulerAngles.y;
            Debug.Log($"[REMOTE-UPDATE] pid={playerId} targetYaw={targetYaw:F1} currentYaw={currentYaw:F1} hasTarget={_hasTarget}");
        }

        transform.rotation = Quaternion.Slerp(transform.rotation, _targetRotation, Time.deltaTime * rotationSmoothSpeed);

        // 同步插值结果到 ECS 实体
        SyncToECS();
    }

    private void SyncToECS()
    {
        if (ClientECSWorld.Instance == null) return;
        var entity = ClientECSWorld.Instance.GetPlayerEntity(playerId);
        var em = ClientECSWorld.Instance.EntityManager;
        if (!em.IsValid(entity)) return;

        if (em.TryGetComponent<TransformComponent>(entity, out var tx))
        {
            tx.Position = transform.position.ToShared();
            tx.Rotation = transform.rotation.ToShared();
            em.SetComponent(entity, tx);
        }

        if (em.TryGetComponent<MovementComponent>(entity, out var mv))
        {
            mv.Velocity = _targetVelocity;
            mv.VerticalVelocity = _targetVerticalVelocity;
            mv.IsGrounded = _targetIsGrounded;
            em.SetComponent(entity, mv);
        }
    }

    /// <summary>
    /// 初始化远程玩家
    /// </summary>
    public void Initialize(int id, int team, Vector3 spawnPosition, HeroConfig heroConfig = null)
    {
        playerId = id;
        teamId = team;
        _currentHp = heroConfig?.MaxHP ?? GameConstants.MaxHealth;
        _isDead = false;
        _hasTarget = true;

        transform.position = spawnPosition;
        _targetPosition = spawnPosition;

        // 设置队伍颜色
        SetTeamColor(team);

        // 注册到管理器
        _allRemotePlayers[playerId] = this;

        // 注册到 ECS 世界
        if (ClientECSWorld.Instance != null)
            ClientECSWorld.Instance.RegisterRemotePlayer(playerId, spawnPosition.ToShared(), heroConfig);

        Debug.Log($"[RemotePlayerController] 初始化玩家 {playerId}，队伍 {teamId}，BattleClient={BattleClient.Instance != null}，VisualBulletManager={VisualBulletManager.Instance != null}");
    }

    /// <summary>
    /// 设置目标位置（从权威同步）
    /// </summary>
    public void SetTargetPosition(Vector3 position)
    {
        _targetPosition = position;
        _hasTarget = true;
    }

    /// <summary>
    /// 设置 HP
    /// </summary>
    public void SetHp(int hp)
    {
        int oldHp = _currentHp;
        _currentHp = Mathf.Max(0, hp);

        if (oldHp != _currentHp)
        {
            Debug.Log($"[RemotePlayerController] 玩家 {playerId} HP: {oldHp} -> {_currentHp}");
        }
    }

    /// <summary>
    /// 设置隐身可见性。有 Buff.Invisible 标签时对敌人隐藏模型。
    /// </summary>
    public void SetVisible(bool visible)
    {
        if (renderers == null) return;
        foreach (var r in renderers)
        {
            if (r != null)
                r.enabled = visible;
        }
    }

    /// <summary>
    /// 设置死亡状态
    /// </summary>
    public void SetDead()
    {
        if (_isDead) return;

        _isDead = true;
        Debug.Log($"[RemotePlayerController] 玩家 {playerId} 死亡");

        // 播放死亡动画
        if (animSet != null && animancer != null)
        {
            var deathClip = animSet.GetClip(PlayerAnimType.Death);
            if (deathClip != null)
            {
                animancer.Play(deathClip, 0.2f);
            }
        }

        // 可选：禁用碰撞体、显示死亡特效等
    }

    /// <summary>
    /// 复活
    /// </summary>
    public void Revive(Vector3 spawnPosition)
    {
        _isDead = false;
        _currentHp = GameConstants.MaxHealth;
        transform.position = spawnPosition;
        _targetPosition = spawnPosition;

        // 恢复动画
        if (animSet != null && animancer != null)
        {
            var idleClip = animSet.GetClip(PlayerAnimType.Rifle_Idle);
            if (idleClip != null)
            {
                animancer.Play(idleClip, 0.2f);
            }
        }
    }

    /// <summary>
    /// 添加快照到插值缓冲
    /// </summary>
    public void AddSnapshot(float time, PlayerSnapshot snapshot)
    {
        _interpBuffer.Add(time, snapshot);
    }

    /// <summary>
    /// 设置队伍颜色
    /// </summary>
    public void SetTeamColor(int team)
    {
        Color teamColor = team == 1 ? Color.blue : Color.red;
        if (renderers != null)
        {
            foreach (var r in renderers)
            {
                if (r != null)
                {
                    // 尝试使用 MaterialPropertyBlock（支持 SRP/URP 材质）
                    r.GetPropertyBlock(_propBlock);
                    _propBlock.SetColor(ColorProp, teamColor);
                    r.SetPropertyBlock(_propBlock);

                    // 回退：直接设置材质颜色（兼容基本材质）
                    if (r.material != null && r.material.HasProperty(ColorProp))
                    {
                        r.material.color = teamColor;
                    }
                }
            }
        }
    }

    private int _debugLogFrame;
    private int _attacksProcessed;

    private void OnFrameReceived(AllPlayerOperation frame)
    {
        if (playerId < 0) return;

        _debugLogFrame++;

        // 诊断：打印收到的全部玩家状态（前10帧无条件打印，之后每60帧）
        if (_debugLogFrame < 10 || _debugLogFrame % 60 == 0)
        {
            foreach (var s in frame.PlayerStates)
                Debug.Log($"[RECV-DIAG] frame={frame.FrameId} myPid={playerId} statePid={s.PlayerId} rotY={s.RotationY:F1} pos=({s.Position.x:F2},{s.Position.z:F2})");
        }

        // 查找本玩家的状态
        foreach (var state in frame.PlayerStates)
        {
            if (state.PlayerId == playerId)
            {
                // 每30帧打印一次接收到的远程玩家数据
                if (++_debugLogFrame % 30 == 0)
                {
                    Debug.Log($"[RECV-REMOTE] playerId={playerId} frameId={frame.FrameId} pos=({state.Position.x:F2},{state.Position.z:F2}) rotY={state.RotationY:F1} run={state.IsRunning} hp={state.Hp}");
                }

                // 更新 HP
                if (state.Hp != _currentHp)
                {
                    SetHp(state.Hp);
                    // 同步到 ECS
                    if (ClientECSWorld.Instance != null)
                    {
                        var ent = ClientECSWorld.Instance.GetPlayerEntity(playerId);
                        var em = ClientECSWorld.Instance.EntityManager;
                        if (em.IsValid(ent) && em.TryGetComponent<HealthComponent>(ent, out _))
                            em.SetComponent(ent, new HealthComponent((byte)state.Hp, GameConstants.MaxHealth));
                    }
                }

                // 更新死亡状态
                if (state.IsDead && !_isDead)
                {
                    SetDead();
                }

                // 添加到插值缓冲
                var snapshot = new PlayerSnapshot
                {
                    Position = state.Position,
                    Rotation = Quat.Euler(0f, state.RotationY, 0f),
                    Velocity = state.Velocity,
                    VerticalVelocity = state.VerticalVelocity,
                    IsGrounded = state.IsGrounded,
                    State = (PlayerStateEnum)state.StateEnum
                };
                _interpBuffer.Add(Time.unscaledTime, snapshot);

                // 诊断日志: 收到的 RotationY
                if (++_debugLogFrame <= 10 || _debugLogFrame % 30 == 0)
                    Debug.Log($"[REMOTE-SNAP] pid={playerId} frameId={frame.FrameId} recvRotY={state.RotationY:F1} snapRotEulerY={snapshot.Rotation.EulerAngles.y:F1}");

                // 更新调试面板
                var overlay = FindFirstObjectByType<NetworkDebugOverlay>();
                if (overlay != null)
                {
                    overlay.RecordRemoteState(playerId, teamId, state.RotationY,
                        state.Position.x, state.Position.z,
                        state.Velocity.x, state.Velocity.z,
                        state.IsRunning, state.Hp);
                }
                break;
            }
        }

        // 处理远程玩家的攻击操作 -> 生成视觉子弹
        ProcessAttackOperations(frame);
    }

    private void ProcessAttackOperations(AllPlayerOperation frame)
    {
        foreach (var op in frame.Operations)
        {
            if (op.PlayerId != playerId) continue;
            if (op.AttackOperations == null || op.AttackOperations.Count == 0) continue;
            if (_isDead) continue;

            _attacksProcessed += op.AttackOperations.Count;

            // 子弹生成由 AuthoritySync.SpawnVisualBulletsFromFrame() 统一处理
            // 这里只处理枪口火焰、枪声等本地视觉效果
            if (playerModel != null && playerModel.muzzleFlash != null)
                playerModel.muzzleFlash.Play();

            if (playerModel != null && playerModel.fireSoundClip != null && AudioPoolManager.Instance != null)
                AudioPoolManager.Instance.PlaySound(playerModel.fireSoundClip, transform.position);

            // Log first few attacks
            if (_attacksProcessed <= 5 || _attacksProcessed % 30 == 0)
            {
                Debug.Log($"[RemotePlayer] pid={playerId} received attack count={_attacksProcessed} frameOps={op.AttackOperations.Count}");
            }
        }
    }

    private void DriveAnimation(PlayerSnapshot from, PlayerSnapshot to, float t)
    {
        if (animancer == null || animSet == null) return;
        if (_isDead) return;

        // 确定目标动画状态
        var targetState = SnapshotAnimationBridge.SnapshotToAnimationState(to);

        if (targetState != _lastAnimState)
        {
            _lastAnimState = targetState;

            switch (targetState)
            {
                case PlayerAnimationState.idle:
                    var idleClip = animSet.GetClip(PlayerAnimType.Rifle_Idle);
                    if (idleClip != null) animancer.Play(idleClip, 0.2f);
                    break;

                case PlayerAnimationState.move:
                    if (_moveMixer != null)
                        animancer.Play(_moveMixer, 0.1f);
                    else
                    {
                        var walkClip = animSet.GetClip(PlayerAnimType.Rifle_WalkFwdLoop);
                        if (walkClip != null) animancer.Play(walkClip, 0.2f);
                    }
                    break;

                case PlayerAnimationState.aim:
                    var aimClip = animSet.GetClip(PlayerAnimType.Rifle_Idle);
                    if (aimClip != null) animancer.Play(aimClip, 0.15f);
                    break;

                case PlayerAnimationState.jump:
                case PlayerAnimationState.fall:
                    var fallClip = animSet.GetClip(PlayerAnimType.Rifle_FallingLoop);
                    if (fallClip != null) animancer.Play(fallClip, 0.25f);
                    break;
            }
        }

        // 更新移动混合参数
        if (targetState == PlayerAnimationState.move && _moveMixer != null)
        {
            Vec3 velocity = Vec3.Lerp(from.Velocity, to.Velocity, t);
            float speed = new Vec3(velocity.x, 0f, velocity.z).Magnitude;

            float blend;
            if (speed < 0.1f) blend = 0f;
            else if (speed < 7f) blend = 1f;
            else blend = 2f;

            _moveMixer.Parameter = Mathf.MoveTowards(_moveMixer.Parameter, blend, Time.deltaTime * 5f);
        }
    }

    /// <summary>
    /// 获取枪口位置（用于远程玩家子弹生成）
    /// </summary>
    public Vector3 GetFireOrigin()
    {
        if (playerModel != null && playerModel.firePoint != null)
            return playerModel.firePoint.position;
        return transform.position + Vector3.up * (GameConstants.PlayerHeight * 0.85f);
    }

    /// <summary>
    /// 根据玩家 ID 获取远程玩家控制器
    /// </summary>
    public static RemotePlayerController GetPlayer(int playerId)
    {
        return _allRemotePlayers.TryGetValue(playerId, out var ctrl) ? ctrl : null;
    }
}