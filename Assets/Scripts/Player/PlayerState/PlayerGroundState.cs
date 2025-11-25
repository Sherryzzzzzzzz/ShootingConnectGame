using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.PlayerLoop;

public class PlayerGroundState : PlayerStateBase
{
    private float aimSpeed;
    float speed = 0;

    [Header("倾斜参数")]
    public float maxTiltAngle = 15f; // 最大左右倾斜角度
    public float tiltSmooth = 5f;    // 倾斜平滑度

    private float currentTilt = 0f;

    public override void Update()
    {
        base.Update();
        

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
            Vector3 camForward = Camera.main.transform.forward;
            camForward.y = 0f; // 忽略俯仰角，只取水平面方向
            camForward.Normalize();
            
            Quaternion targetRotation = Quaternion.LookRotation(camForward, Vector3.up);
            
            float rotationSpeed = 10f;
            playerModel.transform.rotation = Quaternion.Slerp(
                playerModel.transform.rotation,
                targetRotation,
                rotationSpeed * Time.deltaTime
            );
            
            playerModel.Fire();
        }

        
        float rad = Mathf.Atan2(playerController.localMovement.x, playerController.localMovement.z);
        playerModel.transform.Rotate(0,rad*playerController.rotationSpeed*Time.deltaTime,0);
        
        if (playerController.jump)
        {
            playerModel.gravityVector.y = Mathf.Sqrt(playerModel.gravity * -2.0f * playerModel.jumpHeight);
            playerModel.ChangePlayerState(PlayerState.sky);
        }

        if (playerController.aim)
        {
            playerModel.ChangePlayerState(PlayerState.aim);
        }
    }
    
}

