using Animancer;
using UnityEngine;
using InputFrame = ShootingGame.Shared.Simulation.InputFrame;

public class AimState : PlayerStateBase
{
    private AnimancerComponent animancer;
    private ClipTransition aimIdle;

    public override void Init(PlayerModel model)
    {
        base.Init(model);
        animancer = playerModel.animancer;
        aimIdle = playerModel.AnimationSet.GetClip(PlayerAnimType.Rifle_Idle);
    }

    public override void Enter()
    {
        if (animancer == null || aimIdle == null)
        {
            Debug.LogError($"[AimState] Cannot Enter: animancer or aimIdle is null");
            return;
        }
        animancer.Play(aimIdle);
    }

    public override void Tick(InputFrame input, float dt)
    {
        if (!input.Aim)
        {
            playerModel.ChangeAnimationState(PlayerAnimationState.idle);
            return;
        }

        if (input.Jump)
        {
            playerModel.ChangeAnimationState(PlayerAnimationState.fall);
            return;
        }
    }
}