// 地面状态
namespace ShootingGame.Shared.Simulation
{
    public class GroundState : IPlayerState
    {
        public PlayerStateEnum Id => PlayerStateEnum.Ground;

        public PlayerSnapshot OnEnter(PlayerSnapshot snap) => snap;
        public PlayerSnapshot OnExit(PlayerSnapshot snap) => snap;

        public PlayerSnapshot Tick(PlayerSnapshot snap, InputFrame input, float dt)
        {
            if (input.Jump)
            {
                snap.State = PlayerStateEnum.Sky;
            }
            else if (input.Aim)
            {
                snap.State = PlayerStateEnum.Aim;
            }
            return snap;
        }
    }
}