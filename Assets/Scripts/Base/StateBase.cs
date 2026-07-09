using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using InputFrame = ShootingGame.Shared.Simulation.InputFrame;

public abstract class StateBase
{
    public abstract void Enter();
    
    public abstract void Exit();
    
    public abstract void Update();
    
    public abstract void Init(IStateOwner owner);
    
    public abstract void Destroy();

    public abstract void Tick(InputFrame input, float dt);
}
