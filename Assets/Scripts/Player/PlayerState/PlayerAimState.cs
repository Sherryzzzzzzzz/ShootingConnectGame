using System.Collections;
using System.Collections.Generic;
using UnityEditor.Rendering;
using UnityEngine;

public class PlayerAimState : PlayerStateBase
{
    private float aimSpeed;
    float speed = 0;

    private float currentTilt = 0f;

    public override void Enter()
    {
        base.Enter();
        SetAimCamera();
        playerModel.transform.rotation = Quaternion.Euler(0,Camera.main.transform.eulerAngles.y,0);
    }

    public override void Update()
    {
        base.Update();
        Vector3 camForward = Camera.main.transform.forward;
        camForward.y = 0f; // 忽略俯仰角
        camForward.Normalize();

        Quaternion targetRotation = Quaternion.LookRotation(camForward, Vector3.up);
        playerModel.transform.rotation = Quaternion.Lerp(
            playerModel.transform.rotation,
            targetRotation,
            Time.deltaTime * 10f // 可调旋转平滑度
        );

        // === 正常移动逻辑 ===
        if (playerController.running)
        {
            aimSpeed = playerModel.runSpeed * playerController.movement.magnitude;
        }
        else
        {
            aimSpeed = playerModel.walkSpeed * playerController.movement.magnitude;
        }

        if (playerController.fire)
        {
            playerModel.Fire();
        }
        

        float accel = (playerController.movement.magnitude > 0.1f) ? 8f : 4f;
        speed = Mathf.Lerp(speed, aimSpeed, Time.deltaTime * accel);
        playerController.speed = speed;
        
        if (playerController.jump)
        {
            playerModel.gravityVector.y = Mathf.Sqrt(playerModel.gravity * -2.0f * playerModel.jumpHeight);
            playerModel.ChangePlayerState(PlayerState.sky);
        }

        if (!playerController.aim)
        {
            playerModel.ChangePlayerState(PlayerState.ground);
        }
        
    }

    public override void Exit()
    {
        base.Exit();
        SetNormalCamera();
        playerModel.aimImage.color = Color.white;
    }

    private void SetNormalCamera()
    {
        playerModel.normal.m_XAxis.Value = playerModel.aim.m_XAxis.Value;
        playerModel.normal.m_YAxis.Value = playerModel.aim.m_YAxis.Value;
        playerModel.normal.Priority = 100;
        playerModel.aim.Priority = 0;
    }

    private void SetAimCamera()
    {
        playerModel.aim.m_XAxis.Value = playerModel.normal.m_XAxis.Value;
        playerModel.aim.m_YAxis.Value = playerModel.normal.m_YAxis.Value;
        playerModel.aim.Priority = 100;
        playerModel.normal.Priority = 0;
    }
    
}
