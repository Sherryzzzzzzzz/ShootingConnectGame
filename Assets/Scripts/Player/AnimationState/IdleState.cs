using Animancer;
using UnityEngine;
using InputFrame = ShootingGame.Shared.Simulation.InputFrame;

public class IdleState : PlayerStateBase
{
    private AnimancerComponent animancer;
    private ClipTransition idleClip;

    public override void Init(PlayerModel model)
    {
        base.Init(model);

        animancer = playerModel.animancer;

        idleClip = playerModel.AnimationSet.GetClip(PlayerAnimType.Rifle_Idle);

        if (idleClip == null)
        {
            Debug.LogError("IdleClip not found: Rifle_Idle");
        }
    }
    public override void Enter()
    {
        if (animancer == null || idleClip == null)
        {
            Debug.LogError($"[IdleState] Cannot Enter: animancer or idleClip is null");
            return;
        }
        animancer.Play(idleClip, 0.2f);
    }

    public override void Tick(InputFrame input, float dt)
    {
        if (playerController == null) return;

        if (input.Jump)
        {
            playerModel.ChangeAnimationState(PlayerAnimationState.jump);
            return;
        }

        if (!playerController.IsGrounded)
        {
            playerModel.ChangeAnimationState(PlayerAnimationState.fall);
            return;
        }

        if (input.Movement.SqrMagnitude > 0.01f)
        {
            playerModel.ChangeAnimationState(PlayerAnimationState.move);
            return;
        }

        if (input.Aim)
        {
            playerModel.ChangeAnimationState(PlayerAnimationState.aim);
        }
    }
}