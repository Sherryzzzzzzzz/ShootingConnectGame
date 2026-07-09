namespace ShootingGame.Shared.Simulation
{
    /// <summary>
    /// Shared-side player state interface. Logic only — no animation, no camera.
    /// Each state returns a potentially modified snapshot.
    /// </summary>
    public interface IPlayerState
    {
        PlayerStateEnum Id { get; }
        PlayerSnapshot Tick(PlayerSnapshot snap, InputFrame input, float dt);
        PlayerSnapshot OnEnter(PlayerSnapshot snap);
        PlayerSnapshot OnExit(PlayerSnapshot snap);
    }
}
