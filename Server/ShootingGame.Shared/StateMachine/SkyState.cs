namespace ShootingGame.Shared.Simulation
{
    public class SkyState : IPlayerState
    {
        public PlayerStateEnum Id => PlayerStateEnum.Sky;

        public PlayerSnapshot OnEnter(PlayerSnapshot snap) => snap;
        public PlayerSnapshot OnExit(PlayerSnapshot snap) => snap;

        public PlayerSnapshot Tick(PlayerSnapshot snap, InputFrame input, float dt)
        {
            if (snap.IsGrounded && snap.VerticalVelocity <= 0f)
            {
                snap.State = input.Aim ? PlayerStateEnum.Aim : PlayerStateEnum.Ground;
            }
            return snap;
        }
    }
}
