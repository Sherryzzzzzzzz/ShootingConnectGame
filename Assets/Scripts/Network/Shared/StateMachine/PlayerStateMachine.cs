// 玩家状态机
using System.Collections.Generic;

namespace ShootingGame.Shared.Simulation
{
    /// <summary>
    /// 轻量级状态机，处理进入/退出回调和状态转换
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

            // 处理转换：调用旧状态的退出，新状态的进入
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