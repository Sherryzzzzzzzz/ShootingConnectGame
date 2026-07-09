using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Animancer;
using InputFrame = ShootingGame.Shared.Simulation.InputFrame;


public class FallState : PlayerStateBase
{
    private AnimancerComponent _Animancer;
    private ClipTransition _FallAnimation;
    private float rayDistance = 1f;

    public override void Init(IStateOwner owner)
    {
        base.Init(owner);
        _Animancer = playerModel.animancer;
        _FallAnimation = playerModel.AnimationSet.GetClip(PlayerAnimType.Rifle_FallingLoop);
    }

    public override void Tick(InputFrame input, float dt)
    {
        if (playerController != null && playerController.IsGrounded)
        {
            playerModel.ChangeAnimationState(PlayerAnimationState.idle);
        }
    }

    public override void Enter()
    {
        if (_Animancer == null || _FallAnimation == null)
        {
            Debug.LogError($"[FallState] Cannot Enter: _Animancer or _FallAnimation is null");
            return;
        }
        base.Enter();
        _Animancer.Play(_FallAnimation, 0.25f, FadeMode.FixedSpeed);
    }

    public override void Update()
    {
        base.Update();
    }
}
