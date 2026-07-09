using Animancer;
using UnityEngine;
using InputFrame = ShootingGame.Shared.Simulation.InputFrame;


public class JumpState : PlayerStateBase
{
    private AnimancerComponent animancer;
    private ClipTransition jumpClip;

    public override void Init(PlayerModel model)
    {
        base.Init(model);
        animancer = playerModel.animancer;
        jumpClip = playerModel.AnimationSet.GetClip(PlayerAnimType.Rifle_FallingLoop);
    }

    public override void Enter()
    {
        if (animancer == null || jumpClip == null)
        {
            Debug.LogError($"[JumpState] Cannot Enter: animancer or jumpClip is null");
            return;
        }
        animancer.Play(jumpClip);
    }

    public override void Tick(InputFrame input, float dt)
    {
        if (playerController == null) return;

        if (playerController.IsGrounded)
        {
            playerModel.ChangeAnimationState(PlayerAnimationState.idle);
        }
    }
}