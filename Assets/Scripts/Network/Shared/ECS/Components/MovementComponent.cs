using ShootingGame.Network;
using ShootingGame.Shared.ECS.Components;
using ShootingGame.Shared.Math;
using ShootingGame.Shared.Simulation;

namespace ShootingGame.Shared.ECS
{
    [SyncComponent]
    public partial struct MovementComponent
    {
        [SyncVar] public Vec3 Velocity;
        [SyncVar] public float VerticalVelocity;
        [SyncVar] public bool IsGrounded;
        public float PlayerRadius;
        public float PlayerHeight;
        public float MaxMoveSpeed;

        /// <summary>Dirty tracker for network incremental sync.</summary>
        public DirtyTracker Dirty;

        public MovementComponent(Vec3 velocity, float verticalVelocity, bool isGrounded,
            float playerRadius = GameConstants.PlayerRadius,
            float playerHeight = GameConstants.PlayerHeight,
            float maxMoveSpeed = GameConstants.MoveSpeed) : this()
        {
            Velocity = velocity;
            VerticalVelocity = verticalVelocity;
            IsGrounded = isGrounded;
            PlayerRadius = playerRadius;
            PlayerHeight = playerHeight;
            MaxMoveSpeed = maxMoveSpeed;
        }
    }
}
