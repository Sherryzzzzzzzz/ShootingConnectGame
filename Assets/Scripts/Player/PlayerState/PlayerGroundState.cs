using UnityEngine;
using InputFrame = ShootingGame.Shared.Simulation.InputFrame;


public class PlayerGroundState : PlayerStateBase
{
    private float aimSpeed;

    [Header("倾斜参数")]
    public float maxTiltAngle = 15f;
    public float tiltSmooth = 5f;

    private float currentTilt = 0f;
    private bool _lastJumpInput;

    public override void Tick(InputFrame input, float dt)
    {
        // playerController 可能尚未就绪（场景切换时序问题）
        if (playerController == null) return;

        // === 速度计算 ===
        if (input.Run)
            aimSpeed = playerModel.runSpeed * input.Movement.Magnitude;
        else
            aimSpeed = playerModel.walkSpeed * input.Movement.Magnitude;

        // === 开火（cam 为空时跳过旋转） ===
        if (input.Fire)
        {
            if (playerController.cam != null)
            {
                Vector3 camForward = playerController.cam.transform.forward;
                camForward.y = 0f;
                camForward.Normalize();

                Quaternion targetRotation = Quaternion.LookRotation(camForward, Vector3.up);

                float rotationSpeed = 10f;
                playerModel.transform.rotation = Quaternion.Slerp(
                    playerModel.transform.rotation,
                    targetRotation,
                    rotationSpeed * dt
                );
            }

            playerModel.Fire();
        }

        // === 跳跃（仅在按下瞬间触发，长按不会反复跳跃） ===
        bool jumpJustPressed = input.Jump && !_lastJumpInput;
        _lastJumpInput = input.Jump;

        if (jumpJustPressed)
        {
            playerModel.gravityVector.y =
                Mathf.Sqrt(playerModel.gravity * -2.0f * playerModel.jumpHeight);

            playerModel.ChangePlayerState(PlayerState.sky);
            return;
        }

        // === 进入瞄准 ===
        if (input.Aim)
        {
            playerModel.ChangePlayerState(PlayerState.aim);
        }
    }
}