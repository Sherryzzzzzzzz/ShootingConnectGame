using UnityEngine;
using InputFrame = ShootingGame.Shared.Simulation.InputFrame;


public class PlayerAimState : PlayerStateBase
{
    private float aimSpeed;

    public override void Enter()
    {
        base.Enter();
        SetAimCamera();

        if (playerController != null && playerController.cam != null && playerModel != null)
        {
            playerModel.transform.rotation =
                Quaternion.Euler(0, playerController.cam.transform.eulerAngles.y, 0);
        }
    }

    public override void Tick(InputFrame input, float dt)
    {
        if (playerController == null) return;

        // === 始终朝摄像机方向对齐 ===
        if (playerController.cam != null)
        {
            Vector3 camForward = playerController.cam.transform.forward;
            camForward.y = 0f;
            camForward.Normalize();

            Quaternion targetRotation = Quaternion.LookRotation(camForward, Vector3.up);

            playerModel.transform.rotation = Quaternion.Lerp(
                playerModel.transform.rotation,
                targetRotation,
                dt * 10f
            );
        }

        // === 速度计算 ===
        if (input.Run)
            aimSpeed = playerModel.runSpeed * input.Movement.Magnitude;
        else
            aimSpeed = playerModel.walkSpeed * input.Movement.Magnitude;

        // === 开火 ===
        if (input.Fire)
        {
            playerModel.Fire();
        }

        // === 跳跃 ===
        if (input.Jump)
        {
            playerModel.gravityVector.y =
                Mathf.Sqrt(playerModel.gravity * -2.0f * playerModel.jumpHeight);

            playerModel.ChangePlayerState(PlayerState.sky);
            return;
        }

        // === 退出瞄准 ===
        if (!input.Aim)
        {
            playerModel.ChangePlayerState(PlayerState.ground);
        }
    }

    public override void Exit()
    {
        base.Exit();
        SetNormalCamera();
        if (playerModel != null && playerModel.aimImage != null)
            playerModel.aimImage.color = Color.white;
    }

    private void SetNormalCamera()
    {
        if (playerModel.normal == null || playerModel.aim == null) return;
        playerModel.normal.m_XAxis.Value = playerModel.aim.m_XAxis.Value;
        playerModel.normal.m_YAxis.Value = playerModel.aim.m_YAxis.Value;
        playerModel.normal.Priority = 100;
        playerModel.aim.Priority = 0;
    }

    private void SetAimCamera()
    {
        if (playerModel.normal == null || playerModel.aim == null) return;
        playerModel.aim.m_XAxis.Value = playerModel.normal.m_XAxis.Value;
        playerModel.aim.m_YAxis.Value = playerModel.normal.m_YAxis.Value;
        playerModel.aim.Priority = 100;
        playerModel.normal.Priority = 0;
    }
}