using UnityEngine;
using ShootingGame.Shared.Simulation;

/// <summary>
/// Drives animation states from PlayerSnapshot data using the existing Animancer-based
/// PlayerModel state machine. Reads snapshot from NetPlayerController each frame and
/// calls PlayerModel.ChangeAnimationState() to trigger the correct Animancer animation.
/// Attach to the local player GameObject alongside NetPlayerController and PlayerModel.
/// </summary>
public class SnapshotAnimationBridge : MonoBehaviour
{
    private PlayerModel _playerModel;
    private NetPlayerController _localController;

    private PlayerAnimationState _lastAnimState = PlayerAnimationState.idle;

    private void Awake()
    {
        _playerModel = GetComponent<PlayerModel>();
        _localController = GetComponent<NetPlayerController>();
    }

    private void Update()
    {
        if (_playerModel == null || _localController == null) return;

        var snap = _localController.CurrentSnapshot;
        var targetState = SnapshotToAnimationState(snap);

        if (targetState != _lastAnimState)
        {
            _playerModel.ChangeAnimationState(targetState);
            _lastAnimState = targetState;
        }
    }

    /// <summary>
    /// Maps a PlayerSnapshot to the appropriate PlayerAnimationState
    /// that the existing Animancer state machine understands.
    /// </summary>
    public static PlayerAnimationState SnapshotToAnimationState(PlayerSnapshot snap)
    {
        switch (snap.State)
        {
            case PlayerStateEnum.Aim:
                return PlayerAnimationState.aim;

            case PlayerStateEnum.Sky:
                if (snap.VerticalVelocity > 0f)
                    return PlayerAnimationState.jump;
                else
                    return PlayerAnimationState.fall;

            case PlayerStateEnum.Ground:
            default:
                float speed = new ShootingGame.Shared.Math.Vec3(
                    snap.Velocity.x, 0f, snap.Velocity.z).Magnitude;
                if (speed > 0.1f)
                    return PlayerAnimationState.move;
                else
                    return PlayerAnimationState.idle;
        }
    }
}
