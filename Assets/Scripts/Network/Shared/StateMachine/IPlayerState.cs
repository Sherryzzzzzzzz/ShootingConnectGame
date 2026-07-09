// 玩家状态接口
namespace ShootingGame.Shared.Simulation
{
    /// <summary>
    /// 共享端玩家状态接口，仅逻辑——无动画、无摄像机
    /// </summary>
    public interface IPlayerState
    {
        PlayerStateEnum Id { get; }
        PlayerSnapshot Tick(PlayerSnapshot snap, InputFrame input, float dt);
        PlayerSnapshot OnEnter(PlayerSnapshot snap);
        PlayerSnapshot OnExit(PlayerSnapshot snap);
    }
}