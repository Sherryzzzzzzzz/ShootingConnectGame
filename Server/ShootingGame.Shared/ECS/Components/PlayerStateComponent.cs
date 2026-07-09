using ShootingGame.Shared.Simulation;

namespace ShootingGame.Shared.ECS
{
    /// <summary>
    /// 玩家状态组件：当前玩家状态枚举。
    /// </summary>
    public struct PlayerStateComponent
    {
        public PlayerStateEnum State;

        public PlayerStateComponent(PlayerStateEnum state)
        {
            State = state;
        }
    }
}
