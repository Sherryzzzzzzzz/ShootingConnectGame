namespace ShootingGame.Shared.Simulation
{
    public class AimState : IPlayerState
    {
        public PlayerStateEnum Id => PlayerStateEnum.Aim;

        public PlayerSnapshot OnEnter(PlayerSnapshot snap) => snap;
        public PlayerSnapshot OnExit(PlayerSnapshot snap) => snap;

        public PlayerSnapshot Tick(PlayerSnapshot snap, InputFrame input, float dt)
        {
            if (!input.Aim)
            {
                snap.State = PlayerStateEnum.Ground;
            }
            else if (input.Jump)
            {
                snap.State = PlayerStateEnum.Sky;
            }
            return snap;
        }
    }
}
