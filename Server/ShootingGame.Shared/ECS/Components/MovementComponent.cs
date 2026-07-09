using ShootingGame.Shared.Math;
using ShootingGame.Shared.Simulation;

namespace ShootingGame.Shared.ECS
{
    public struct MovementComponent
    {
        public Vec3 Velocity;
        public float VerticalVelocity;
        public bool IsGrounded;
        public float PlayerRadius;
        public float PlayerHeight;
        public float MaxMoveSpeed;

        public MovementComponent(Vec3 velocity, float verticalVelocity, bool isGrounded,
            float playerRadius = GameConstants.PlayerRadius,
            float playerHeight = GameConstants.PlayerHeight,
            float maxMoveSpeed = GameConstants.MoveSpeed)
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
