using ShootingGame.Shared.Math;
using ShootingGame.Shared.Simulation;
using Xunit;

namespace ShootingGame.Tests
{
    public class PlayerSimulationTests
    {
        const float DT = GameConstants.TickDelta;
        const float E = 0.01f;

        private static PlayerSnapshot MakeSnapshot()
        {
            return PlayerSnapshot.Default(Vec3.Zero);
        }

        private static InputFrame MakeInput(int tick = 0)
        {
            return new InputFrame { Tick = tick };
        }

        [Fact]
        public void NoInput_NoMovement()
        {
            var snap = MakeSnapshot();
            var input = MakeInput();
            var result = PlayerSimulation.Simulate(snap, input, DT);

            // Only vertical velocity (ground snap -2) causes tiny Y change
            Assert.InRange(result.Position.x, -E, E);
            Assert.InRange(result.Position.z, -E, E);
        }

        [Fact]
        public void MoveForward()
        {
            var snap = MakeSnapshot();
            var input = MakeInput();
            input.Movement = new Vec2(0, 1); // forward (Z+)

            var result = PlayerSimulation.Simulate(snap, input, DT);

            // Should move in Z direction at MoveSpeed
            float expectedZ = GameConstants.MoveSpeed * DT;
            Assert.InRange(result.Position.z, expectedZ - E, expectedZ + E);
            Assert.InRange(result.Position.x, -E, E);
        }

        [Fact]
        public void MoveForward_Running()
        {
            var snap = MakeSnapshot();
            var input = MakeInput();
            input.Movement = new Vec2(0, 1);
            input.Run = true;

            var result = PlayerSimulation.Simulate(snap, input, DT);

            float expectedZ = GameConstants.MoveSpeed * GameConstants.RunMultiplier * DT;
            Assert.InRange(result.Position.z, expectedZ - E, expectedZ + E);
        }

        [Fact]
        public void Gravity_WhenNotGrounded()
        {
            var snap = MakeSnapshot();
            snap.Position = new Vec3(0, 10, 0); // 远离地面，避免地面吸附
            snap.IsGrounded = false;
            snap.VerticalVelocity = 0f;

            var input = MakeInput();
            var result = PlayerSimulation.Simulate(snap, input, DT);

            Assert.True(result.VerticalVelocity < 0f);
            Assert.True(result.Position.y < 10f);
        }

        [Fact]
        public void Jump_SetsVerticalVelocity()
        {
            var snap = MakeSnapshot();
            snap.IsGrounded = true;

            var input = MakeInput();
            input.Jump = true;

            var result = PlayerSimulation.Simulate(snap, input, DT);

            float expectedVv = GameConstants.JumpForce + GameConstants.Gravity * DT;
            Assert.InRange(result.VerticalVelocity, expectedVv - E, expectedVv + E);
            Assert.False(result.IsGrounded);
            Assert.Equal(PlayerStateEnum.Sky, result.State);
        }

        [Fact]
        public void Jump_OnlyWhenGrounded()
        {
            var snap = MakeSnapshot();
            snap.Position = new Vec3(0, 10, 0); // 远离地面
            snap.IsGrounded = false;
            snap.VerticalVelocity = -5f;

            var input = MakeInput();
            input.Jump = true;

            var result = PlayerSimulation.Simulate(snap, input, DT);

            Assert.True(result.VerticalVelocity < 0f);
        }

        [Fact]
        public void GroundSnap_WhenGroundedAndFalling()
        {
            var snap = MakeSnapshot();
            snap.IsGrounded = true;
            snap.VerticalVelocity = -10f; // falling fast but grounded

            var input = MakeInput();
            var result = PlayerSimulation.Simulate(snap, input, DT);

            // 地面吸附后垂直速度归零
            Assert.InRange(result.VerticalVelocity, 0 - E, 0 + E);
            Assert.True(result.IsGrounded);
        }

        [Fact]
        public void StateTransition_GroundToSky()
        {
            var snap = MakeSnapshot();
            snap.State = PlayerStateEnum.Ground;
            snap.IsGrounded = true;

            var input = MakeInput();
            input.Jump = true;

            var result = PlayerSimulation.Simulate(snap, input, DT);
            Assert.Equal(PlayerStateEnum.Sky, result.State);
        }

        [Fact]
        public void StateTransition_SkyToGround_WhenLanded()
        {
            var snap = MakeSnapshot();
            snap.State = PlayerStateEnum.Sky;
            snap.IsGrounded = true;
            snap.VerticalVelocity = -1f; // falling and grounded

            var input = MakeInput();

            var result = PlayerSimulation.Simulate(snap, input, DT);
            Assert.Equal(PlayerStateEnum.Ground, result.State);
        }

        [Fact]
        public void FireCooldown_Decreases()
        {
            var snap = MakeSnapshot();
            snap.FireCooldown = 0.1f;

            var input = MakeInput();
            var result = PlayerSimulation.Simulate(snap, input, DT);

            Assert.True(result.FireCooldown < 0.1f);
        }

        [Fact]
        public void Fire_SetsCooldown()
        {
            var snap = MakeSnapshot();
            snap.FireCooldown = 0f;

            var input = MakeInput();
            input.Fire = true;

            var result = PlayerSimulation.Simulate(snap, input, DT);
            Assert.InRange(result.FireCooldown, GameConstants.FireRate - E, GameConstants.FireRate + E);
        }

        [Fact]
        public void Fire_RespectsCooldown()
        {
            var snap = MakeSnapshot();
            snap.FireCooldown = 0.5f; // still on cooldown

            var input = MakeInput();
            input.Fire = true;

            var result = PlayerSimulation.Simulate(snap, input, DT);
            // Cooldown should have decreased but not been reset to FireRate
            Assert.True(result.FireCooldown < 0.5f);
            Assert.True(result.FireCooldown > GameConstants.FireRate); // still higher than fresh fire rate
        }

        [Fact]
        public void MultiTick_JumpArc()
        {
            var snap = MakeSnapshot();
            snap.IsGrounded = true;

            // Jump
            var input = MakeInput(0);
            input.Jump = true;
            snap = PlayerSimulation.Simulate(snap, input, DT);

            Assert.True(snap.VerticalVelocity > 0f);
            float prevY = snap.Position.y;

            // Simulate several ticks — should rise then fall
            bool wentUp = false;
            bool cameDown = false;

            for (int i = 1; i <= 120; i++)
            {
                var tickInput = MakeInput(i);
                snap = PlayerSimulation.Simulate(snap, tickInput, DT);

                if (snap.Position.y > prevY) wentUp = true;
                if (snap.Position.y < prevY && wentUp) cameDown = true;
                prevY = snap.Position.y;
            }

            Assert.True(wentUp, "Player should rise during jump");
            Assert.True(cameDown, "Player should fall after peak");
        }

        [Fact]
        public void TickNumber_Updated()
        {
            var snap = MakeSnapshot();
            var input = MakeInput(42);
            var result = PlayerSimulation.Simulate(snap, input, DT);
            Assert.Equal(42, result.Tick);
        }

        [Fact]
        public void DiagonalMovement_ClampedToOne()
        {
            var snap = MakeSnapshot();
            var input = MakeInput();
            input.Movement = new Vec2(1, 1); // diagonal

            var result = PlayerSimulation.Simulate(snap, input, DT);

            // Speed should not exceed MoveSpeed
            float speed = result.Velocity.Magnitude;
            Assert.True(speed <= GameConstants.MoveSpeed + E);
        }
    }
}
