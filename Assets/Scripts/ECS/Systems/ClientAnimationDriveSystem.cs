using UnityEngine;
using ShootingGame.Shared.ECS;
using ShootingGame.Shared.Simulation;

/// <summary>
/// 从 ECS 组件状态驱动 Animancer 动画。
/// 挂载到玩家 GameObject 上，每帧读取 ECS 状态来切换动画。
/// </summary>
public class ClientAnimationDriveSystem : MonoBehaviour
{
    [SerializeField] private int _playerId = -1;

    private PlayerModel _playerModel;
    private Animancer.AnimancerComponent _animancer;
    private PlayerAnimationSet _animSet;
    private PlayerAnimationState _lastAnimState = PlayerAnimationState.idle;

    // 移动混合器（用于 Walk/Run 混合）
    private Animancer.LinearMixerState _moveMixer;

    public int PlayerId { get => _playerId; set => _playerId = value; }

    private void Awake()
    {
        _playerModel = GetComponent<PlayerModel>();
        if (_playerModel == null)
            _playerModel = GetComponentInChildren<PlayerModel>();

        _animancer = GetComponent<Animancer.AnimancerComponent>();
        if (_animancer == null)
            _animancer = GetComponentInChildren<Animancer.AnimancerComponent>();

        if (_playerModel != null)
            _animSet = _playerModel.AnimationSet;
    }

    private void Start()
    {
        // 构建移动混合器
        if (_animSet != null && _animancer != null)
        {
            var idle = _animSet.GetClip(PlayerAnimType.Rifle_Idle);
            var walk = _animSet.GetClip(PlayerAnimType.Rifle_WalkFwdLoop);
            var run = _animSet.GetClip(PlayerAnimType.Rifle_RunFwdLoop);

            if (idle != null && walk != null && run != null)
            {
                _moveMixer = new Animancer.LinearMixerState
                {
                    { idle, 0f },
                    { walk, 1f },
                    { run, 2f }
                };
            }
        }
    }

    private void Update()
    {
        if (ClientECSWorld.Instance == null) return;
        var em = ClientECSWorld.Instance.EntityManager;

        var entity = ClientECSWorld.Instance.GetPlayerEntity(_playerId);
        if (!em.IsValid(entity)) return;

        // 从 ECS 读取状态
        if (!em.TryGetComponent<MovementComponent>(entity, out var movement)) return;
        if (!em.TryGetComponent<PlayerStateComponent>(entity, out var state)) return;

        // 确定目标动画状态
        var targetState = DetermineAnimationState(state.State, movement);

        if (targetState != _lastAnimState)
        {
            _lastAnimState = targetState;
            PlayAnimation(targetState);
        }

        // 更新移动混合参数
        if (targetState == PlayerAnimationState.move && _moveMixer != null)
        {
            float speed = new ShootingGame.Shared.Math.Vec3(
                movement.Velocity.x, 0f, movement.Velocity.z).Magnitude;

            float blend;
            if (speed < 0.1f) blend = 0f;
            else if (speed < 7f) blend = 1f;
            else blend = 2f;

            _moveMixer.Parameter = Mathf.MoveTowards(_moveMixer.Parameter, blend, Time.deltaTime * 5f);
        }
    }

    private PlayerAnimationState DetermineAnimationState(PlayerStateEnum state, MovementComponent movement)
    {
        switch (state)
        {
            case PlayerStateEnum.Aim:
                return PlayerAnimationState.aim;

            case PlayerStateEnum.Sky:
                if (movement.VerticalVelocity > 0f)
                    return PlayerAnimationState.jump;
                else
                    return PlayerAnimationState.fall;

            case PlayerStateEnum.Ground:
            default:
                float speed = new ShootingGame.Shared.Math.Vec3(
                    movement.Velocity.x, 0f, movement.Velocity.z).Magnitude;
                if (speed > 0.1f)
                    return PlayerAnimationState.move;
                else
                    return PlayerAnimationState.idle;
        }
    }

    private void PlayAnimation(PlayerAnimationState targetState)
    {
        if (_animancer == null || _animSet == null) return;

        switch (targetState)
        {
            case PlayerAnimationState.idle:
                var idleClip = _animSet.GetClip(PlayerAnimType.Rifle_Idle);
                if (idleClip != null) _animancer.Play(idleClip, 0.2f);
                break;

            case PlayerAnimationState.move:
                if (_moveMixer != null)
                    _animancer.Play(_moveMixer, 0.1f);
                else
                {
                    var walkClip = _animSet.GetClip(PlayerAnimType.Rifle_WalkFwdLoop);
                    if (walkClip != null) _animancer.Play(walkClip, 0.2f);
                }
                break;

            case PlayerAnimationState.aim:
                var aimClip = _animSet.GetClip(PlayerAnimType.Rifle_Idle);
                if (aimClip != null) _animancer.Play(aimClip, 0.15f);
                break;

            case PlayerAnimationState.jump:
            case PlayerAnimationState.fall:
                var fallClip = _animSet.GetClip(PlayerAnimType.Rifle_FallingLoop);
                if (fallClip != null) _animancer.Play(fallClip, 0.25f);
                break;
        }
    }

    /// <summary>
    /// 强制重置动画状态。
    /// </summary>
    public void ResetToIdle()
    {
        _lastAnimState = PlayerAnimationState.idle;
        PlayAnimation(PlayerAnimationState.idle);
    }
}
