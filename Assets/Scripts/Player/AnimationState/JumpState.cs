using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Animancer;

public class JumpState : PlayerStateBase
{
    private AnimancerComponent _Animancer;
    [SerializeField]
    private ClipTransition _JumpAnimation;

    public override void Init(IStateOwner owner)
    {
        base.Init(owner);
        _Animancer = playerModel.animancer;
    }

    public override void Enter()
    {
        base.Enter();
        _Animancer.Play(_JumpAnimation);
    }

    public override void Update()
    {
        if (playerModel.gravityVector.y <= 0)
        {
            playerModel.ChangeAnimationState(PlayerAnimationState.fall);
        }
    }
    
}
