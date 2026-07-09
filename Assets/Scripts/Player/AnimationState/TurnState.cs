using Animancer;
using UnityEngine;
using InputFrame = ShootingGame.Shared.Simulation.InputFrame;


public class TurnState : PlayerStateBase
{
    private AnimancerComponent animancer;
    private ClipTransition turnLeft;
    private ClipTransition turnRight;

    private bool turning;
    private float angleThreshold = 60f;

    public override void Init(PlayerModel model)
    {
        base.Init(model);

        animancer = playerModel.animancer;

        turnLeft  = playerModel.AnimationSet.GetClip(PlayerAnimType.Rifle_TurnL_90);
        turnRight = playerModel.AnimationSet.GetClip(PlayerAnimType.Rifle_TurnR_90);
    }

    public override void Enter()
    {
        turning = false;
    }

    public override void Tick(InputFrame input, float dt)
    {
        if (playerController == null) return;

        if (input.Movement.SqrMagnitude < 0.01f)
        {
            playerModel.ChangeAnimationState(PlayerAnimationState.idle);
            return;
        }

        Vector3 moveDir = new Vector3(input.Movement.x, 0f, input.Movement.y).normalized;

        Vector3 forward = playerController.transform.forward;

        float angle = Vector3.SignedAngle(forward, moveDir, Vector3.up);

        if (!turning)
        {
            if (Mathf.Abs(angle) > angleThreshold)
            {
                turning = true;

                if (angle > 0)
                {
                    if (animancer != null && turnRight != null)
                        animancer.Play(turnRight, 0.1f);
                }
                else
                {
                    if (animancer != null && turnLeft != null)
                        animancer.Play(turnLeft, 0.1f);
                }
            }
            else
            {
                playerModel.ChangeAnimationState(PlayerAnimationState.move);
                return;
            }
        }

        // 旋转已由 PlayerController 控制
        // 这里只检测是否已对齐

        float currentAngle =
            Vector3.Angle(playerController.transform.forward, moveDir);

        if (currentAngle < 5f)
        {
            playerModel.ChangeAnimationState(PlayerAnimationState.move);
        }
    }
}