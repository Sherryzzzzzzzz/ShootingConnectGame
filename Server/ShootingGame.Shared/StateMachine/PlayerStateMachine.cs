using System.Collections.Generic;

namespace ShootingGame.Shared.Simulation
{
    /// <summary>
    /// Lightweight state machine operating on PlayerSnapshot.
    /// Handles enter/exit callbacks and state transitions.
    /// </summary>
    public class PlayerStateMachine
    {
        private readonly Dictionary<PlayerStateEnum, IPlayerState> _states;

        public PlayerStateMachine()
        {
            _states = new Dictionary<PlayerStateEnum, IPlayerState>
            {
                { PlayerStateEnum.Ground, new GroundState() },
                { PlayerStateEnum.Sky, new SkyState() },
                { PlayerStateEnum.Aim, new AimState() }
            };
        }

        public PlayerSnapshot Tick(PlayerSnapshot snap, InputFrame input, float dt)
        {
            var previousState = snap.State;

            if (_states.TryGetValue(snap.State, out var state))
            {
                snap = state.Tick(snap, input, dt);
            }

            // Handle transition: call exit on old, enter on new
            if (snap.State != previousState)
            {
                if (_states.TryGetValue(previousState, out var oldState))
                    snap = oldState.OnExit(snap);
                if (_states.TryGetValue(snap.State, out var newState))
                    snap = newState.OnEnter(snap);
            }

            return snap;
        }
    }
}
