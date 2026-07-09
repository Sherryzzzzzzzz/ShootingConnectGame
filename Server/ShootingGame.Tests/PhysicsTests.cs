using ShootingGame.Shared.Math;
using ShootingGame.Shared.Physics;
using Xunit;

namespace ShootingGame.Tests
{
    public class PhysicsTests
    {
        const float E = 0.02f;

        #region Ray vs AABB

        [Fact]
        public void RayAABB_HitsBox()
        {
            var box = new AABB(new Vec3(2, 0, -1), new Vec3(4, 2, 1));
            var ray = new Ray(Vec3.Zero, Vec3.Right); // shooting along +X

            var hit = Intersection.RayAABB(ray, box, 100f);
            Assert.True(hit.Hit);
            Assert.InRange(hit.Distance, 2f - E, 2f + E); // hits at x=2
            Assert.InRange(hit.Normal.x, -1f - E, -1f + E); // normal points -X
        }

        [Fact]
        public void RayAABB_MissesBox()
        {
            var box = new AABB(new Vec3(2, 0, -1), new Vec3(4, 2, 1));
            var ray = new Ray(Vec3.Zero, Vec3.Up); // shooting up, misses

            var hit = Intersection.RayAABB(ray, box, 100f);
            Assert.False(hit.Hit);
        }

        [Fact]
        public void RayAABB_OriginInsideBox()
        {
            var box = new AABB(new Vec3(-1, -1, -1), new Vec3(1, 1, 1));
            var ray = new Ray(Vec3.Zero, Vec3.Right);

            var hit = Intersection.RayAABB(ray, box, 100f);
            Assert.True(hit.Hit);
            Assert.InRange(hit.Distance, -E, E); // distance = 0 (already inside)
        }

        [Fact]
        public void RayAABB_BeyondMaxDistance()
        {
            var box = new AABB(new Vec3(10, 0, -1), new Vec3(12, 2, 1));
            var ray = new Ray(Vec3.Zero, Vec3.Right);

            var hit = Intersection.RayAABB(ray, box, 5f); // max 5, box at 10
            Assert.False(hit.Hit);
        }

        #endregion

        #region Sweep Sphere vs AABB

        [Fact]
        public void SweepSphere_HitsWall()
        {
            var box = new AABB(new Vec3(5, 0, -2), new Vec3(6, 3, 2));
            float radius = 0.3f;

            var hit = Intersection.SweepSphereAABB(Vec3.Zero, radius, Vec3.Right, box, 100f);
            Assert.True(hit.Hit);
            // Sphere of radius 0.3 should hit at distance ~4.7 (5 - 0.3)
            Assert.InRange(hit.Distance, 4.5f, 4.9f);
        }

        [Fact]
        public void SweepSphere_MissesWall()
        {
            var box = new AABB(new Vec3(5, 0, 5), new Vec3(6, 3, 7)); // off to the side
            float radius = 0.3f;

            var hit = Intersection.SweepSphereAABB(Vec3.Zero, radius, Vec3.Right, box, 100f);
            Assert.False(hit.Hit);
        }

        #endregion

        #region CollisionWorld

        [Fact]
        public void CollisionWorld_Raycast()
        {
            var world = new CollisionWorld();
            world.AddBox(new AABB(new Vec3(3, 0, -1), new Vec3(4, 2, 1)));
            world.AddBox(new AABB(new Vec3(7, 0, -1), new Vec3(8, 2, 1)));

            var hit = world.Raycast(Vec3.Zero, Vec3.Right, 100f);
            Assert.True(hit.Hit);
            Assert.InRange(hit.Distance, 3f - E, 3f + E); // closest box
        }

        [Fact]
        public void CollisionWorld_SaveLoad()
        {
            var world = new CollisionWorld();
            world.AddBox(new AABB(new Vec3(1, 2, 3), new Vec3(4, 5, 6)));
            world.AddBox(new AABB(new Vec3(-1, -2, -3), new Vec3(0, 0, 0)));

            string tmpPath = System.IO.Path.GetTempFileName();
            try
            {
                world.Save(tmpPath);
                var loaded = CollisionWorld.Load(tmpPath);

                Assert.Equal(2, loaded.Count);

                // Verify by raycast — should hit the second box from below
                var hit = loaded.Raycast(new Vec3(-0.5f, -10, -1.5f), Vec3.Up, 100f);
                Assert.True(hit.Hit);
            }
            finally
            {
                System.IO.File.Delete(tmpPath);
            }
        }

        #endregion

        #region KinematicMover

        [Fact]
        public void KinematicMover_MoveOnFlat()
        {
            var world = new CollisionWorld();
            // Floor
            world.AddBox(new AABB(new Vec3(-50, -1, -50), new Vec3(50, 0, 50)));

            float radius = 0.3f;
            var pos = new Vec3(0, 0, 0); // feet on floor
            var movement = new Vec3(1, 0, 0); // move right

            var result = KinematicMover.Move(pos, movement, radius, world);

            // Should have moved right
            Assert.InRange(result.Position.x, 0.9f, 1.1f);
            Assert.True(result.IsGrounded);
        }

        [Fact]
        public void KinematicMover_SlideAlongWall()
        {
            var world = new CollisionWorld();
            // Floor
            world.AddBox(new AABB(new Vec3(-50, -1, -50), new Vec3(50, 0, 50)));
            // Wall at x=3
            world.AddBox(new AABB(new Vec3(3, 0, -50), new Vec3(4, 5, 50)));

            float radius = 0.3f;
            var pos = new Vec3(2, 0, 0);
            // Move diagonally into wall
            var movement = new Vec3(3, 0, 3);

            var result = KinematicMover.Move(pos, movement, radius, world);

            // Should be stopped near x=3-radius, but should have slid in Z
            Assert.True(result.Position.x < 3f);
            Assert.True(result.Position.z > 0.5f); // should have moved in Z
        }

        [Fact]
        public void KinematicMover_GroundDetection()
        {
            var world = new CollisionWorld();
            // Floor
            world.AddBox(new AABB(new Vec3(-50, -1, -50), new Vec3(50, 0, 50)));

            float radius = 0.3f;
            var pos = new Vec3(0, 0, 0);
            var movement = Vec3.Zero;

            var result = KinematicMover.Move(pos, movement, radius, world);
            Assert.True(result.IsGrounded);
        }

        [Fact]
        public void KinematicMover_NoGround_WhenInAir()
        {
            var world = new CollisionWorld();
            // Floor far below
            world.AddBox(new AABB(new Vec3(-50, -100, -50), new Vec3(50, -99, 50)));

            float radius = 0.3f;
            var pos = new Vec3(0, 10, 0); // high up
            var movement = Vec3.Zero;

            var result = KinematicMover.Move(pos, movement, radius, world);
            Assert.False(result.IsGrounded);
        }

        [Fact]
        public void KinematicMover_FallDown()
        {
            var world = new CollisionWorld();
            // Floor at y=0
            world.AddBox(new AABB(new Vec3(-50, -1, -50), new Vec3(50, 0, 50)));

            float radius = 0.3f;
            var pos = new Vec3(0, 5, 0);
            // Falling movement
            var movement = new Vec3(0, -10, 0);

            var result = KinematicMover.Move(pos, movement, radius, world);

            // Should land on or near the floor (small tolerance for ground snap offset)
            Assert.InRange(result.Position.y, -0.05f, 0.1f);
            Assert.True(result.IsGrounded);
        }

        [Fact]
        public void KinematicMover_CornerSlide()
        {
            var world = new CollisionWorld();
            // Floor
            world.AddBox(new AABB(new Vec3(-50, -1, -50), new Vec3(50, 0, 50)));
            // Wall at x=3
            world.AddBox(new AABB(new Vec3(3, 0, -50), new Vec3(4, 5, 50)));
            // Wall at z=3
            world.AddBox(new AABB(new Vec3(-50, 0, 3), new Vec3(50, 5, 4)));

            float radius = 0.3f;
            var pos = new Vec3(2, 0, 2);
            // Move into corner
            var movement = new Vec3(3, 0, 3);

            var result = KinematicMover.Move(pos, movement, radius, world);

            // Should be blocked in both X and Z near the corner
            Assert.True(result.Position.x < 3f);
            Assert.True(result.Position.z < 3f);
        }

        #endregion
    }
}
