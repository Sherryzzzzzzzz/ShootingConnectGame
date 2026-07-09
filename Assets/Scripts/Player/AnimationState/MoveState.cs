using Animancer;
using UnityEngine;
using InputFrame = ShootingGame.Shared.Simulation.InputFrame;


public class MoveState : PlayerStateBase
{
    private AnimancerComponent animancer;
    private LinearMixerState moveMixer;

    private ClipTransition idle;
    private ClipTransition walk;
    private ClipTransition run;

    private float blend;
    private float targetBlend;

    public override void Init(PlayerModel model)
    {
        base.Init(model);

        animancer = playerModel.animancer;

        idle = playerModel.AnimationSet.GetClip(PlayerAnimType.Rifle_Idle);
        walk = playerModel.AnimationSet.GetClip(PlayerAnimType.Rifle_WalkFwdLoop);
        run  = playerModel.AnimationSet.GetClip(PlayerAnimType.Rifle_RunFwdLoop);

        moveMixer = new LinearMixerState
        {
            { idle, 0f },
            { walk, 1f },
            { run,  2f }
        };
    }

    public override void Enter()
    {
        if (animancer == null || moveMixer == null)
        {
            Debug.LogError($"[MoveState] Cannot Enter: animancer or moveMixer is null");
            return;
        }
        animancer.Play(moveMixer, 0.1f);
        blend = 0f;
    }

    public override void Tick(InputFrame input, float dt)
    {
        if (playerController == null) return;

        float magnitude = input.Movement.Magnitude;

        if (magnitude < 0.05f)
        {
            playerModel.ChangeAnimationState(PlayerAnimationState.idle);
            return;
        }

        if (!playerController.IsGrounded)
        {
            playerModel.ChangeAnimationState(PlayerAnimationState.fall);
            return;
        }

        if (input.Aim)
        {
            playerModel.ChangeAnimationState(PlayerAnimationState.aim);
            return;
        }

        targetBlend = input.Run ? 2f : 1f;

        blend = Mathf.MoveTowards(blend, targetBlend, dt * 5f);
        moveMixer.Parameter = blend;
    }
}