using System.Collections;
using System.Collections.Generic;
using Animancer;
using UnityEngine;
using System;
using InputFrame = ShootingGame.Shared.Simulation.InputFrame;

public interface IStateOwner { }


public class StateMachine//统一的状态机
{
     private StateBase currentState;//当前状态

     private IStateOwner owner;
     
     private Dictionary<Type,StateBase> states = new Dictionary<Type, StateBase>();

     public StateMachine(IStateOwner owner)
     {
          this.owner = owner;
     }
     
     public void EnterState<T>() where T: StateBase,new()//进入继承statebase的状态
     {
          if(currentState != null && currentState.GetType() == typeof(T)) return;
          if (currentState != null)
               currentState.Exit();
          currentState = LoadState<T>();
          // LoadState 在首次创建时已调用 Init(owner)，此处不再重复调用
          currentState.Enter();
     }

     private StateBase LoadState<T>() where T : StateBase, new()//用字典来节省初始化的消耗
     {
          Type stateType = typeof(T);
          if (!states.TryGetValue(stateType, out StateBase state))
          {
               state = new T();
               state.Init(owner);
               states.Add(stateType, state);
          }
          else
          {
               // 每次切换状态时重新初始化，确保引用（如 playerController）是最新的
               state.Init(owner);
          }
          return state;
     }

     /// <summary>
     /// 强制重新初始化当前状态（用于延迟获取组件引用，如 playerController）
     /// </summary>
     public void ReinitCurrentState()
     {
          currentState?.Init(owner);
     }

     public void Stop()
     {
          if(currentState != null)
               currentState.Exit();
          foreach (var state in states.Values)
          {
               state.Destroy();
          }
     }

     public void Tick(InputFrame input, float dt)
     {
          currentState?.Tick(input, dt);
     }
}
