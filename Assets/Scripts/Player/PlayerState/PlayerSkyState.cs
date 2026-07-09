using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using InputFrame = ShootingGame.Shared.Simulation.InputFrame;

public class PlayerSkyState : PlayerStateBase
{
    public override void Tick(InputFrame input, float dt)
    {
        if (playerModel == null) return;

        // 应用重力
        playerModel.gravityVector.y += playerModel.gravity * dt;

        // 检测落地（从模拟快照获取）
        if (playerController != null && playerController.IsGrounded)
        {
            playerModel.ChangePlayerState(PlayerState.ground);
        }
    }
}
