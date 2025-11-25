using UnityEngine;
using Animancer;

using UnityEngine;
using Animancer;

public class AimState : PlayerStateBase
{
    private AnimancerComponent _Animancer;
    private CartesianMixerState _moveMixer;
    private CartesianMixerState _aimMixer;
    private AnimancerLayer _aimLayer;
    private AnimancerState _startAnimState;

    private bool _isStartPlaying = false;
    private bool _moveMixerPlaying = false;

    // 平滑参数
    private float _animBlend = 0f;
    private float _currentSpeed = 0f;
    private float _speedSmoothVelocity = 0f;
    private float _smoothTime = 0.15f;
    private float _blendSmoothSpeed = 5f;

    // 动画引用（略 - 假设你已经在 Init 获取了这些 clip）
    private ClipTransition _L, _R, _F, _B;
    private ClipTransition _FR, _BR, _LR, _RR;
    private ClipTransition _FS, _BS, _LS, _RS;
    private ClipTransition _IdleAnimation;

    private ClipTransition _LookUp45, _LookUp90, _LookDown45, _LookDown90;
    private ClipTransition _LookLeft45, _LookLeft90, _LookRight45, _LookRight90;
    private ClipTransition _LookLD45, _LookRD45, _LookLU45, _LookRU45,_LookCC;

    public override void Init(IStateOwner owner)
    {
        base.Init(owner);
        _Animancer = playerModel.animancer;

        // === 获取动画资源（保持你原来的获取方式） ===
        _IdleAnimation = playerModel.AnimationSet.GetClip(PlayerAnimType.Rifle_Idle);

        _F = playerModel.AnimationSet.GetClip(PlayerAnimType.Rifle_WalkFwdLoop);
        _B = playerModel.AnimationSet.GetClip(PlayerAnimType.Rifle_WalkBwdLoop);
        _L = playerModel.AnimationSet.GetClip(PlayerAnimType.Rifle_StrafeLeftLoop);
        _R = playerModel.AnimationSet.GetClip(PlayerAnimType.Rifle_StrafeRightLoop);

        _FR = playerModel.AnimationSet.GetClip(PlayerAnimType.Rifle_RunFwdLoop);
        _BR = playerModel.AnimationSet.GetClip(PlayerAnimType.Rifle_RunBwdLoop);
        _LR = playerModel.AnimationSet.GetClip(PlayerAnimType.Rifle_StrafeRunLeftLoop);
        _RR = playerModel.AnimationSet.GetClip(PlayerAnimType.Rifle_StrafeRunRightLoop);

        _FS = playerModel.AnimationSet.GetClip(PlayerAnimType.Rifle_WalkFwdStart);
        _BS = playerModel.AnimationSet.GetClip(PlayerAnimType.Rifle_WalkBwdStart);
        _LS = playerModel.AnimationSet.GetClip(PlayerAnimType.Rifle_StrafeLeftStart);
        _RS = playerModel.AnimationSet.GetClip(PlayerAnimType.Rifle_StrafeRightStart);

        _LookUp45 = playerModel.AnimationSet.GetClip(PlayerAnimType.Rifle_Look_45U_Additive);
        _LookUp90 = playerModel.AnimationSet.GetClip(PlayerAnimType.Rifle_Look_90U_Additive);
        _LookDown45 = playerModel.AnimationSet.GetClip(PlayerAnimType.Rifle_Look_45D_Additive);
        _LookDown90 = playerModel.AnimationSet.GetClip(PlayerAnimType.Rifle_Look_90D_Additive);
        _LookLeft45 = playerModel.AnimationSet.GetClip(PlayerAnimType.Rifle_Look_45L_Additive);
        _LookLeft90 = playerModel.AnimationSet.GetClip(PlayerAnimType.Rifle_Look_90L_Additive);
        _LookRight45 = playerModel.AnimationSet.GetClip(PlayerAnimType.Rifle_Look_45R_Additive);
        _LookRight90 = playerModel.AnimationSet.GetClip(PlayerAnimType.Rifle_Look_90R_Additive);
        _LookLU45 = playerModel.AnimationSet.GetClip(PlayerAnimType.Rifle_Look_45LU_Additive);
        _LookRU45 = playerModel.AnimationSet.GetClip(PlayerAnimType.Rifle_Look_45RU_Additive);
        _LookLD45 = playerModel.AnimationSet.GetClip(PlayerAnimType.Rifle_Look_45LD_Additive);
        _LookRD45 = playerModel.AnimationSet.GetClip(PlayerAnimType.Rifle_Look_45RD_Additive);
        _LookCC = playerModel.AnimationSet.GetClip(PlayerAnimType.Rifle_Look_CC_Additive);

        // === 瞄准混合器（Additive） ===
        _aimMixer = new CartesianMixerState
        {
            { _LookUp90, new Vector2(0,  90) },
            { _LookUp45, new Vector2(0,  45) },
            { _LookDown45, new Vector2(0, -45) },
            { _LookDown90, new Vector2(0, -90) },
            { _LookLeft45, new Vector2(-45, 0) },
            { _LookLeft90, new Vector2(-90, 0) },
            { _LookRight45, new Vector2(45, 0) },
            { _LookRight90, new Vector2(90, 0) },
            { _LookLU45, new Vector2(-45, 45) },
            { _LookRU45, new Vector2(45, 45) },
            { _LookLD45, new Vector2(-45, -45) },
            { _LookRD45, new Vector2(45, -45) },
            {_LookCC,new Vector2(0, 0)}
        };
    }

    public override void Enter()
    {
        base.Enter();
        Debug.Log("Enter");

        // 初始化状态
        _isStartPlaying = false;
        _moveMixerPlaying = false;

        // 先确保 aim layer 已准备（但不一定播放 move）
        PrepareAimLayer();

        // 如果一进入就有移动输入 -> 播放起步动画
        Vector2 input = playerController.movement.normalized;
        if (input.sqrMagnitude > 0.01f)
        {
            StartStartAnimation(input);
        }
        else
        {
            // 没有输入：播放 idle（底层）并启用 aim layer（不播放 move mixer）
            _Animancer.Play(_IdleAnimation, 0.1f);
            PlayAimLayerOnly();
        }
    }

    public override void Update()
    {
        base.Update();

        // 如果起步动画正在播放，不进行重复触发 & 不更新 move mixer 参数
        if (_isStartPlaying)
        {
            // 但仍需要处理取消瞄准：如果玩家在起步期间松开瞄准，立刻停止起步并退出 AimState
            if (!playerController.aim)
            {
                // stop start anim if playing
                if (_startAnimState != null && _startAnimState.IsPlaying)
                    _startAnimState.Stop();

                ExitAimImmediate();
            }
            return;
        }

        // 如果 move mixer 正在播放，则更新它
        if (_moveMixerPlaying)
        {
            UpdateMovementMixer();
        }
        else
        {
            // moveMixer 还没播放（处于 idle + aim layer 状态）
            // 如果现在按下移动键，则触发起步动画（但要避免重复触发）
            Vector2 input = playerController.movement.normalized;
            if (input.sqrMagnitude > 0.01f)
            {
                StartStartAnimation(input);
                return;
            }
        }

        // 更新瞄准混合器（始终更新，只要 aim layer 在）
        UpdateAimMixer();

        // 跳跃或离地时切换出去
        if (playerController.jump || !playerController.isGround)
        {
            ExitAimImmediate();
            playerModel.ChangeAnimationState(PlayerAnimationState.fall);
            return;
        }

        // 如果玩家取消瞄准（右键放开），立刻退出 AimState
        if (!playerController.aim)
        {
            ExitAimImmediate();
            // 切回 move 或 idle 根据移动输入决定
            if (playerController.movement.magnitude > 0.1f)
                playerModel.ChangeAnimationState(PlayerAnimationState.move);
            else
                playerModel.ChangeAnimationState(PlayerAnimationState.idle);
        }
    }

    private void StartStartAnimation(Vector2 input)
    {
        if (_isStartPlaying || _moveMixerPlaying) return; // 已经在起步或已在 moveMixer 中

        _isStartPlaying = true;
        ClipTransition startClip = GetStartClip(input);

        // 播放并用 OnEnd.Add 保证兼容
        _startAnimState = _Animancer.Play(startClip, 0.1f, FadeMode.FromStart);
        _startAnimState.Events(this).OnEnd = (() =>
        {
            // 如果在起步期间玩家取消瞄准，别再进入 moveMixer
            if (!playerController.aim)
            {
                _isStartPlaying = false;
                return;
            }

            PlayMoveMixer();
            _isStartPlaying = false;
        });
    }

    private void PlayMoveMixer()
    {
        bool running = playerController.running;

        // 构造并播放 moveMixer（走或跑的版本）
        _moveMixer = new CartesianMixerState
        {
            { _IdleAnimation, Vector2.zero },
            { running ? _FR : _F, new Vector2(0, 1) },
            { running ? _BR : _B, new Vector2(0, -1) },
            { running ? _LR : _L, new Vector2(-1, 0) },
            { running ? _RR : _R, new Vector2(1, 0) },
        };

        _Animancer.Play(_moveMixer, 0.15f, FadeMode.FixedSpeed);
        _moveMixerPlaying = true;

        // 确保 aim layer 在播放（上半身）
        PlayAimLayerOnly();
    }

    private void PrepareAimLayer()
    {
        _aimLayer = _Animancer.Layers.Count > 1 ? _Animancer.Layers[1] : _Animancer.Layers.Add();
        _aimLayer.IsAdditive = true;
    }

    private void PlayAimLayerOnly()
    {
        if (_aimLayer == null) PrepareAimLayer();
        _aimLayer.Play(_aimMixer);
        _aimLayer.Weight = 1f;
    }

    private void UpdateMovementMixer()
    {
        if (_moveMixer == null) return;

        Vector2 moveInput = playerController.movement;
        float moveMagnitude = moveInput.magnitude;

        float targetSpeed = 0f;
        if (moveMagnitude > 0.1f)
            targetSpeed = playerController.running ? 1.5f : 1f;

        _currentSpeed = Mathf.SmoothDamp(_currentSpeed, targetSpeed, ref _speedSmoothVelocity, _smoothTime);
        _animBlend = Mathf.MoveTowards(_animBlend, _currentSpeed, _blendSmoothSpeed * Time.deltaTime);

        Vector2 targetParam = moveInput.normalized * _animBlend;
        _moveMixer.Parameter = Vector2.Lerp(_moveMixer.Parameter, targetParam, Time.deltaTime * _blendSmoothSpeed);

        // 如果停止移动则回 idle（并停掉 moveMixer）
        if (moveMagnitude < 0.1f && _currentSpeed < 0.05f)
        {
            _Animancer.Play(_IdleAnimation, 0.25f);
            _moveMixerPlaying = false;
            _moveMixer = null;
        }
    }

    private void UpdateAimMixer()
    {
        if (_aimMixer == null) return;

        Transform cam = Camera.main.transform;
        Transform player = playerModel.transform;
        
        Vector3 camForward = cam.forward;
        Vector3 playerForward = player.forward;
        
        Vector3 flatCamForward = new Vector3(camForward.x, 0f, camForward.z).normalized;
        Vector3 flatPlayerForward = new Vector3(playerForward.x, 0f, playerForward.z).normalized;

        float yawDelta = -Vector3.SignedAngle(flatPlayerForward, flatCamForward, Vector3.up)*2;
        
        Vector3 camForwardFlat = new Vector3(camForward.x, 0, camForward.z).normalized;
        float pitchAngle = -Vector3.SignedAngle(camForwardFlat, camForward, Camera.main.transform.right*3);

        
        Vector2 targetParam = new Vector2(yawDelta > 90f?90f:yawDelta, pitchAngle > 90f?90f:pitchAngle);

        // 平滑混合，避免抖动
        _aimMixer.Parameter = Vector2.Lerp(
            _aimMixer.Parameter,
            targetParam,
            Time.deltaTime * 10f
        );
    }

    // 立即退出瞄准层并清理标志（用于中途取消瞄准或跳转）
    private void ExitAimImmediate()
    {
        // 停掉起步动画
        if (_startAnimState != null && _startAnimState.IsPlaying)
            _startAnimState.Stop();

        // 停掉 move mixer
        _moveMixerPlaying = false;
        _moveMixer = null;

        // 停掉并淡出 aim layer
        if (_aimLayer != null)
        {
            _aimLayer.StartFade(0f, 0.15f);
            _aimLayer.Stop();
            _aimLayer.IsAdditive = false;
        }

        _isStartPlaying = false;
    }

    private ClipTransition GetStartClip(Vector2 input)
    {
        float angle = Mathf.Atan2(input.x, input.y) * Mathf.Rad2Deg;

        if (angle >= -45 && angle <= 45)
            return _FS;
        else if (angle > 45 && angle < 135)
            return _RS;
        else if (angle < -45 && angle > -135)
            return _LS;
        else
            return _BS;
    }

    public override void Exit()
    {
        base.Exit();
        // 走常规清理路径：立即退出 aim 层（平滑淡出）
        ExitAimImmediate();

        // 播回 idle
        if (_Animancer != null && _IdleAnimation != null)
            _Animancer.Play(_IdleAnimation, 0.2f);
    }
}
