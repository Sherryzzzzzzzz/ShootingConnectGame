using UnityEngine;
using Animancer;

public class TurnState : PlayerStateBase
{
    private AnimancerComponent _Animancer;
    private LinearMixerState _turnMixer;

    private ClipTransition _TurnL90;
    private ClipTransition _TurnR90;
    private ClipTransition _TurnL180;
    private ClipTransition _TurnR180;
    private ClipTransition _StartWalkAnimation;
    private AnimancerState state;

    private float _turnAngle;
    private bool _isTurning;
    private float _targetYaw;

    public override void Init(IStateOwner owner)
    {
        base.Init(owner);
        _Animancer = playerModel.animancer;

        _TurnL90 = playerModel.AnimationSet.GetClip(PlayerAnimType.Rifle_WalkFwdStart90_L);
        _TurnR90 = playerModel.AnimationSet.GetClip(PlayerAnimType.Rifle_WalkFwdStart90_R);
        _TurnL180 = playerModel.AnimationSet.GetClip(PlayerAnimType.Rifle_WalkFwdStart180_L);
        _TurnR180 = playerModel.AnimationSet.GetClip(PlayerAnimType.Rifle_WalkFwdStart180_R);
        _StartWalkAnimation = playerModel.AnimationSet.GetClip(PlayerAnimType.Rifle_WalkFwdStart);
        // 初始化混合器（两个动画：左转 & 右转）
        _turnMixer = new LinearMixerState
        {
            { _TurnL90, 0f },
            { _TurnR90, 1f },
        };
    }

    public override void Enter()
    {
        base.Enter();

        Vector3 moveInput = new Vector3(playerController.movement.x, 0, playerController.movement.y);
        if (moveInput.sqrMagnitude < 0.01f)
        {
            AnimancerState state_f = _Animancer.Play(_StartWalkAnimation, 0.1f, FadeMode.FromStart);
            state_f.Events(this).OnEnd=() => playerModel.ChangeAnimationState(PlayerAnimationState.move);
            return;
        }

        // 获取相机方向
        // 获取相机的前、右方向（水平）
        Vector3 camForward = Camera.main.transform.forward;
        Vector3 camRight = Camera.main.transform.right;
        camForward.y = camRight.y = 0;
        camForward.Normalize();
        camRight.Normalize();

        // 计算玩家想要的移动方向（基于输入与相机方向）
        Vector2 input = playerController.movement;
        Vector3 moveDir = (camForward * input.y + camRight * input.x).normalized;

        if (moveDir.sqrMagnitude < 0.001f)
            return; // 没有输入就不转

        // 当前朝向与目标方向的夹角
        Vector3 currentForward = playerController.transform.forward;
        _turnAngle = Vector3.SignedAngle(currentForward, moveDir, Vector3.up);

        // 目标角度（后续旋转修正用）
        _targetYaw = playerController.transform.eulerAngles.y + _turnAngle;

        float absAngle = Mathf.Abs(_turnAngle);
        if (absAngle < 30f)
        {
            playerModel.ChangeAnimationState(PlayerAnimationState.move);
            return;
        }

        // 角度映射到混合参数 [0,1]
        float blendValue = Mathf.InverseLerp(-180f, 180f, _turnAngle);
        _turnMixer.Parameter = blendValue;

        // 根据角度选择动画组（90° / 180°）
        if (absAngle > 135f)
        {
            _turnMixer = new LinearMixerState
            {
                { _TurnL180, 0f },
                { _TurnR180, 1f },
            };
        }
        else
        {
            _turnMixer = new LinearMixerState
            {
                { _TurnL90, 0f },
                { _TurnR90, 1f },
            };
        }

        // 播放转身动画
        state = _Animancer.Play(_turnMixer, 0.15f,FadeMode.FixedSpeed);
        _isTurning = true;

        state.Events(this).OnEnd = () =>
        {
            _isTurning = false;
            playerModel.ChangeAnimationState(PlayerAnimationState.move);
        };
    }
}
