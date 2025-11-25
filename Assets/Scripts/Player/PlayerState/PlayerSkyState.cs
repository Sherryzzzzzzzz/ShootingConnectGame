using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerSkyState : PlayerStateBase
{
    public override void Update()
    {
        if (playerController.isGround)
        {
            playerModel.ChangePlayerState(PlayerState.ground);
        }
    }
}
