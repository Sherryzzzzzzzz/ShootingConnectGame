using System.Collections;
using System.Collections.Generic;
using Animancer;
using UnityEngine;

public class IdleState : PlayerStateBase
{
    private AnimancerComponent _Animancer;
    private ClipTransition _IdleAnimation;

    public override void Init(IStateOwner owner)
    {
        base.Init(owner);
        _Animancer = playerModel.animancer;
        _IdleAnimation = playerModel.AnimationSet.GetClip(PlayerAnimType.Rifle_Idle);
    }

    public override void Enter()
    {
        base.Enter();
        _Animancer.Play(_IdleAnimation,0.25f,FadeMode.FromStart);
    }

    public override void Update()
    {
        base.Update();
        if (playerController.movement.magnitude != 0)
        {
            playerModel.ChangeAnimationState(PlayerAnimationState.turn);
        }

        if (playerController.jump)
        {
            playerModel.ChangeAnimationState(PlayerAnimationState.fall);
        }
        
        if (!playerController.isGround)
        {
            playerModel.ChangeAnimationState(PlayerAnimationState.fall);
        }

        if (playerController.aim)
        {
            playerModel.ChangeAnimationState(PlayerAnimationState.aim);
        }
    }
}
