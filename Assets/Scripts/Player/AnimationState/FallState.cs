using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Animancer;

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

    public override void Enter()
    {
        base.Enter();
        _Animancer.Play(_FallAnimation,0.25f,FadeMode.FixedSpeed);
    }

    public override void Update()
    {
        base.Update();

        if (playerController.isGround)
        {
            playerModel.ChangeAnimationState(PlayerAnimationState.idle);
        }

    }
}
