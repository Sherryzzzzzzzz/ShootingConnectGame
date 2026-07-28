using System.Collections.Generic;
using UnityEngine;
using Unity.Cinemachine;
using ShootingGame.Shared.Hero;
using ShootingGame.Shared.Protocol;
using ShootingGame.Shared.Simulation;
using ShootingGame.Shared.Math;
using ShootingGame.Shared.ECS;

/// <summary>
/// 远程玩家控制器精简版。动画由 PistolAnimationDriver 接管，本类只负责插值/状态同步。
/// </summary>
public class RemotePlayerController : MonoBehaviour
{
    [Header("玩家标识")] [SerializeField] private int playerId = -1;
    [SerializeField] private int teamId = 0;
    [Header("插值设置")] [SerializeField] private float interpolationDelay = 0.1f;
    [SerializeField] private float positionSmoothSpeed = 10f;
    [SerializeField] private float rotationSmoothSpeed = 720f;
    [SerializeField] private float snapThreshold = 3f;
    [Header("引用")] [SerializeField] private Renderer[] renderers;

    private InterpolationBuffer _interpBuffer;
    private Vector3 _targetPosition;
    private Quaternion _targetRotation;
    private Vec3 _targetVelocity;
    private float _targetVerticalVelocity;
    private bool _targetIsGrounded, _hasTarget;
    private int _currentHp = 100;
    private bool _isDead;
    private MaterialPropertyBlock _propBlock;
    private int _debugLogFrame;
    private static readonly int ColorProp = Shader.PropertyToID("_Color");
    private static readonly Dictionary<int, RemotePlayerController> _all = new();
    public static IReadOnlyDictionary<int, RemotePlayerController> All => _all;

    public int PlayerId => playerId; public int TeamId => teamId;
    public int CurrentHp => _currentHp; public bool IsDead => _isDead;
    public PlayerSnapshot CurrentSnapshot { get; private set; }
    public bool IsAiming { get; private set; }
    public bool IsCrouching { get; private set; }
    public bool FireTrigger { get; set; }
    public static RemotePlayerController GetPlayer(int id) => _all.TryGetValue(id, out var c) ? c : null;

    private void Awake()
    {
        _interpBuffer = new InterpolationBuffer();
        _propBlock = new MaterialPropertyBlock();
        var anim = GetComponent<Animator>();
        if (anim != null) anim.applyRootMotion = false;
        foreach (var fl in GetComponentsInChildren<CinemachineFreeLook>()) fl.gameObject.SetActive(false);
        // 初始化快照，防止 PistolAnimationDriver 在收到第一帧网络数据前误播死亡动画
        CurrentSnapshot = PlayerSnapshot.Default(transform.position.ToShared());
    }

    private void Start()
    {
        if (BattleClient.Instance != null) BattleClient.Instance.OnFrameReceived += OnFrameReceived;
        if (renderers == null || renderers.Length == 0) renderers = GetComponentsInChildren<Renderer>();
        _targetPosition = transform.position; _targetRotation = transform.rotation;
    }

    private void OnDestroy()
    {
        if (BattleClient.Instance != null) BattleClient.Instance.OnFrameReceived -= OnFrameReceived;
        if (playerId >= 0) { _all.Remove(playerId); ClientECSWorld.Instance?.UnregisterPlayer(playerId); }
    }

    private void Update()
    {
        if (!_hasTarget) return;
        float renderTime = Time.unscaledTime - interpolationDelay;
        if (_interpBuffer.Sample(renderTime, out var from, out var to, out float t))
        {
            _targetPosition = Vec3.Lerp(from.Position, to.Position, t).ToUnity();
            _targetRotation = Quat.Slerp(from.Rotation, to.Rotation, t).ToUnity();
            _targetVelocity = Vec3.Lerp(from.Velocity, to.Velocity, t);
            _targetVerticalVelocity = Mathf.Lerp(from.VerticalVelocity, to.VerticalVelocity, t);
            _targetIsGrounded = to.IsGrounded;
        }
        float dist = Vector3.Distance(transform.position, _targetPosition);
        transform.position = dist > snapThreshold ? _targetPosition : Vector3.Lerp(transform.position, _targetPosition, Time.deltaTime * positionSmoothSpeed);
        transform.rotation = Quaternion.Slerp(transform.rotation, _targetRotation, Time.deltaTime * rotationSmoothSpeed);
        SyncToECS();
    }

    private void SyncToECS()
    {
        if (ClientECSWorld.Instance == null) return;
        var e = ClientECSWorld.Instance.GetPlayerEntity(playerId);
        var em = ClientECSWorld.Instance.EntityManager;
        if (!em.IsValid(e)) return;
        if (em.TryGetComponent<TransformComponent>(e, out var tx)) { tx.Position = transform.position.ToShared(); tx.Rotation = transform.rotation.ToShared(); em.SetComponent(e, tx); }
        if (em.TryGetComponent<MovementComponent>(e, out var mv)) { mv.Velocity = _targetVelocity; mv.VerticalVelocity = _targetVerticalVelocity; mv.IsGrounded = _targetIsGrounded; em.SetComponent(e, mv); }
    }

    public void Initialize(int id, int team, Vector3 spawnPos, HeroConfig hc = null)
    {
        playerId = id; teamId = team;
        _currentHp = hc?.MaxHP ?? GameConstants.MaxHealth;
        _isDead = false; _hasTarget = true;
        transform.position = spawnPos; _targetPosition = spawnPos;
        if (playerId >= 0) _all[playerId] = this;
        ClientECSWorld.Instance?.RegisterRemotePlayer(playerId, spawnPos.ToShared(), hc);
    }

    public void SetTargetPosition(Vector3 pos) => _targetPosition = pos;
    public void SetHp(int hp) => _currentHp = Mathf.Max(0, hp);
    public void SetDead() => _isDead = true;
    public void SetVisible(bool v) { foreach (var r in renderers) if (r != null) r.enabled = v; }
    public void SetTeamColor(Color c) { _propBlock.SetColor(ColorProp, c); foreach (var r in renderers) if (r != null) r.SetPropertyBlock(_propBlock); }
    public void Revive(Vector3 pos) { _isDead = false; _currentHp = 100; transform.position = pos; _targetPosition = pos; }
    public Vector3 GetFireOrigin() => transform.position + Vector3.up * 1.5f;

    private void OnFrameReceived(AllPlayerOperation frame)
    {
        if (!_hasTarget) return;
        foreach (var state in frame.PlayerStates)
        {
            if (state.PlayerId != playerId) continue;
            if (state.Hp != _currentHp) SetHp(state.Hp);
            if (state.IsDead && !_isDead) SetDead();
            IsAiming = state.IsAiming; IsCrouching = state.IsCrouching;
            CurrentSnapshot = new PlayerSnapshot
            {
                Position = state.Position, Rotation = Quat.Euler(0f, state.RotationY, 0f),
                Velocity = state.Velocity, VerticalVelocity = state.VerticalVelocity,
                IsGrounded = state.IsGrounded, State = (PlayerStateEnum)state.StateEnum, Health = (byte)state.Hp
            };
            _interpBuffer.Add(Time.unscaledTime, CurrentSnapshot);
        }
    }
}
