using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerStateBase: StateBase
{
    protected PlayerModel playerModel;
    protected PlayerController playerController;
    
    public override void Enter()
    {
        MonoManager.Instance.AddUpdateAction(Update);
    }

    public override void Exit()
    {
        MonoManager.Instance.RemoveUpdateAction(Update);
    }

    public override void Update()
    {
        
    }

    public override void Init(IStateOwner owner)
    {
        playerModel = (PlayerModel)owner;
        playerController = PlayerController.Instance;
    }

    public override void Destroy()
    {
        
    }
}
