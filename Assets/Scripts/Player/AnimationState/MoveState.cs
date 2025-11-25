using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Animancer;
using Unity.VisualScripting;

using Animancer;
using UnityEngine;

public class MoveState : PlayerStateBase
{
    private AnimancerComponent _Animancer;
    private LinearMixerState _moveMixer;
    private ClipTransition _WalkAnimation;
    private ClipTransition _RunAnimation;
    private ClipTransition _IdleAnimation;

    // 动画混合参数
    private float _animBlend = 0f;
    private float _targetBlend = 0f;
    private float _blendSmoothSpeed = 5f; // 越大越快

    // 起步/停步加速插值
    private float _currentSpeed = 0f;
    private float _speedSmoothVelocity = 0f;
    private float _smoothTime = 0.15f; // 越小响应越快

    // 动画淡入控制
    private float _enterBlendDuration = 0.25f;
    private float _enterBlendTimer = 0f;
    private bool _isTransitioning = false;

    // Idle延迟退出
    private float idleExitDelay = 0.25f;
    private float idleExitTimer = 0f;

    public override void Init(IStateOwner owner)
    {
        base.Init(owner);
        _Animancer = playerModel.animancer;
        _IdleAnimation = playerModel.AnimationSet.GetClip(PlayerAnimType.Rifle_Idle);
        _RunAnimation = playerModel.AnimationSet.GetClip(PlayerAnimType.Rifle_RunFwdLoop);
        _WalkAnimation = playerModel.AnimationSet.GetClip(PlayerAnimType.Rifle_WalkFwdLoop);
        

        _moveMixer = new LinearMixerState()
        {
            { _IdleAnimation, 0f },
            { _WalkAnimation, 1f },
            { _RunAnimation, 2f }
        };
    }

    public override void Enter()
    {
        base.Enter();
        _Animancer.Play(_moveMixer,0.1f,FadeMode.FixedSpeed);

        _enterBlendTimer = 0f;
        _isTransitioning = true;

        _animBlend = 0f;
        _moveMixer.Parameter = 0f;
        _currentSpeed = 0f;
        idleExitTimer = 0f;
    }

    public override void Update()
    {
        base.Update();

        Vector2 moveInput = playerController.movement;
        float moveMagnitude = moveInput.magnitude;

        // 计算期望速度（跑 or 走）
        float targetSpeed = 0f;
        if (moveMagnitude > 0.1f)
        {
            if (playerController.running)
                targetSpeed = 2f; // 跑步动画段
            else
                targetSpeed = 1f; // 走路动画段
        }

        // 使用 SmoothDamp 让起步与停步更平滑
        _currentSpeed = Mathf.SmoothDamp(_currentSpeed, targetSpeed, ref _speedSmoothVelocity, _smoothTime);

        // 动画混合参数平滑插值
        _animBlend = Mathf.MoveTowards(_animBlend, _currentSpeed, _blendSmoothSpeed * Time.deltaTime);
        _moveMixer.Parameter = _animBlend;

        // === Idle 退出延迟 ===
        if (moveMagnitude < 0.1f && _currentSpeed < 0.05f)
        {
            idleExitTimer += Time.deltaTime;
            if (idleExitTimer >= idleExitDelay)
            {
                _Animancer.Play(_IdleAnimation, 0.25f);
                playerModel.ChangeAnimationState(PlayerAnimationState.idle);
                return;
            }
        }
        else
        {
            idleExitTimer = 0f;
        }

        // === 跳跃 / 下落切换 ===
        if (playerController.jump)
        {
            playerModel.ChangeAnimationState(PlayerAnimationState.fall);
        }
        if (!playerController.isGround)
        {
            playerModel.ChangeAnimationState(PlayerAnimationState.fall);
        }
        if (playerController.movement.magnitude == 0)
        {
            playerModel.ChangeAnimationState(PlayerAnimationState.idle);
        }

        if (playerController.aim)
        {
            playerModel.ChangeAnimationState(PlayerAnimationState.aim);
        }

        // === 淡入过程控制 ===
        if (_isTransitioning)
        {
            _enterBlendTimer += Time.deltaTime;
            if (_enterBlendTimer >= _enterBlendDuration)
                _isTransitioning = false;
        }
    }

    public override void Exit()
    {
        base.Exit();
        if (_Animancer != null && _IdleAnimation != null)
        {
            _Animancer.Play(_IdleAnimation, 0.2f);
        }
    }
}