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

        #region 空间索引与暴力扫描一致性

        /// <summary>确定性伪随机（LCG），避免测试依赖 Random 种子</summary>
        private static float NextPseudo(ref uint state, float min, float max)
        {
            state = state * 1664525u + 1013904223u;
            float t = (state & 0xFFFFFF) / 16777216f;
            return min + (max - min) * t;
        }

        [Fact]
        public void CollisionWorld_GridMatchesLinearScan()
        {
            var world = new CollisionWorld();
            uint s = 12345u;
            for (int i = 0; i < 200; i++)
            {
                float cx = NextPseudo(ref s, -100f, 100f);
                float cy = NextPseudo(ref s, -10f, 30f);
                float cz = NextPseudo(ref s, -100f, 100f);
                float sx = NextPseudo(ref s, 0.2f, 6f);
                float sy = NextPseudo(ref s, 0.2f, 6f);
                float sz = NextPseudo(ref s, 0.2f, 6f);
                world.AddBox(new AABB(new Vec3(cx, cy, cz), new Vec3(cx + sx, cy + sy, cz + sz)));
            }

            // 暴力参考：直接遍历 Boxes
            for (int q = 0; q < 20; q++)
            {
                var origin = new Vec3(NextPseudo(ref s, -100f, 100f), NextPseudo(ref s, -5f, 30f), NextPseudo(ref s, -100f, 100f));
                var dir = new Vec3(NextPseudo(ref s, -1f, 1f), NextPseudo(ref s, -1f, 1f), NextPseudo(ref s, -1f, 1f)).Normalized;
                float radius = 0.35f;
                float maxDist = 80f;

                // --- SweepSphere 参考 ---
                HitResult refSweep = HitResult.None;
                float refDist = maxDist;
                for (int i = 0; i < world.Boxes.Count; i++)
                {
                    var hit = Intersection.SweepSphereAABB(origin, radius, dir, world.Boxes[i], refDist);
                    if (hit.Hit && hit.Distance < refDist) { refSweep = hit; refDist = hit.Distance; }
                }

                var gridSweep = world.SweepSphere(origin, radius, dir, maxDist);
                Assert.Equal(refSweep.Hit, gridSweep.Hit);
                if (refSweep.Hit)
                    Assert.InRange(gridSweep.Distance, refSweep.Distance - 0.001f, refSweep.Distance + 0.001f);

                // --- Raycast 参考 ---
                var ray = new Ray(origin, dir);
                HitResult refRay = HitResult.None;
                float refRayDist = maxDist;
                for (int i = 0; i < world.Boxes.Count; i++)
                {
                    var hit = Intersection.RayAABB(ray, world.Boxes[i], refRayDist);
                    if (hit.Hit && hit.Distance < refRayDist) { refRay = hit; refRayDist = hit.Distance; }
                }

                var gridRay = world.Raycast(origin, dir, maxDist);
                Assert.Equal(refRay.Hit, gridRay.Hit);
                if (refRay.Hit)
                    Assert.InRange(gridRay.Distance, refRay.Distance - 0.001f, refRay.Distance + 0.001f);
            }
        }

        #endregion

        #region 解穿透 Depenetrate

        [Fact]
        public void DepenetrateSphere_PushesOutEmbeddedSphere()
        {
            var world = new CollisionWorld();
            world.AddBox(new AABB(new Vec3(1, 0, -1), new Vec3(3, 2, 1)));

            float radius = 0.3f;
            // 球心在盒子扩展体内（expanded min.x = 0.7）
            var center = new Vec3(1.1f, 1f, 0f);
            Assert.True(world.OverlapSphere(center, radius));

            var result = world.DepenetrateSphere(center, radius);

            // 应沿最小穿透轴（-X）推出
            Assert.True(result.x < 0.7f, $"应推出到 expanded min.x=0.7 之外，实际 x={result.x}");
            Assert.False(world.OverlapSphere(result, radius));
        }

        [Fact]
        public void DepenetrateSphere_NoOpWhenNotOverlapping()
        {
            var world = new CollisionWorld();
            world.AddBox(new AABB(new Vec3(-50, -1, -50), new Vec3(50, 0, 50)));

            // 正常站立在地面上方的球心不应被推动
            var center = new Vec3(0f, 0.33f, 0f);
            var result = world.DepenetrateSphere(center, 0.3f);
            Assert.InRange(result.x - center.x, -0.0001f, 0.0001f);
            Assert.InRange(result.y - center.y, -0.0001f, 0.0001f);
            Assert.InRange(result.z - center.z, -0.0001f, 0.0001f);
        }

        #endregion

        #region KinematicMover 重叠盒防上爬回归

        [Fact]
        public void KinematicMover_OverlappingWallSeam_DoesNotClimbUp()
        {
            // 回归测试：两面错位重叠的墙（模拟体素化地形的接缝）。
            // 旧实现：沿墙滑动进入重叠区 → sweep 返回 tmin=0/normal=Zero → 滑动卡死
            // → 误触发跨步 → 角色逐级爬上墙顶。
            var world = new CollisionWorld();
            world.AddBox(new AABB(new Vec3(-50, -1, -50), new Vec3(50, 0, 50)));      // 地面
            world.AddBox(new AABB(new Vec3(3f, 0, -50), new Vec3(4f, 5, 5)));         // 墙 A
            world.AddBox(new AABB(new Vec3(2.9f, 0, -5), new Vec3(3.9f, 5, 50)));     // 墙 B（向玩家侧突出 0.1，与 A 重叠）

            float radius = 0.3f;
            var pos = new Vec3(2f, 0f, -2f);
            var perTick = new Vec3(0.1f, 0f, 0.1f); // 斜着推向墙，沿墙滑动穿 seams

            for (int tick = 0; tick < 80; tick++)
            {
                var result = KinematicMover.Move(pos, perTick, radius, world);
                Assert.True(result.Position.y < 0.05f,
                    $"tick {tick}: 角色不应沿重叠墙缝上爬，实际 y={result.Position.y}");
                pos = result.Position;
            }
        }

        [Fact]
        public void KinematicMover_OverlappingFloorSeam_DoesNotClimbUp()
        {
            // 回归测试：两块错位重叠的地板（体素化地形常见）。走过接缝不应获得高度。
            var world = new CollisionWorld();
            world.AddBox(new AABB(new Vec3(-50, -1, -50), new Vec3(50, 0, 5)));        // 地板 A
            world.AddBox(new AABB(new Vec3(-50, -1.1f, 4), new Vec3(50, -0.1f, 50))); // 地板 B（与 A 重叠，略低 0.1）

            float radius = 0.3f;
            var pos = new Vec3(0f, 0f, 0f);
            var perTick = new Vec3(0f, 0f, 0.1f);

            for (int tick = 0; tick < 80; tick++)
            {
                var result = KinematicMover.Move(pos, perTick, radius, world);
                Assert.True(result.Position.y < 0.05f,
                    $"tick {tick}: 走过重叠地板接缝不应上爬，实际 y={result.Position.y}");
                pos = result.Position;
            }
        }

        [Fact]
        public void KinematicMover_Stairs_StillStepUp()
        {
            // 保证正常的台阶跨步功能没有被新 gating 破坏
            var world = new CollisionWorld();
            world.AddBox(new AABB(new Vec3(-50, -1, -50), new Vec3(50, 0, 50)));       // 地面
            world.AddBox(new AABB(new Vec3(2, 0, -50), new Vec3(4, 0.25f, 50)));       // 0.25m 台阶（< MaxStepHeight 0.3）

            float radius = 0.3f;
            var pos = new Vec3(1f, 0f, 0f);
            var movement = new Vec3(1.0f, 0f, 0f); // 需要 > 0.7 才能触及台阶的扩展体

            var result = KinematicMover.Move(pos, movement, radius, world);

            // 应跨上台阶：y ≈ 0.25，且水平越过了台阶边缘
            Assert.InRange(result.Position.y, 0.15f, 0.35f);
            Assert.True(result.Position.x > 1.5f, $"应跨上台阶前进，实际 x={result.Position.x}");
            Assert.True(result.IsGrounded);
        }

        #endregion

        #region 扩散 SpreadUtility

        [Fact]
        public void Spread_ZeroAngle_KeepsDirection()
        {
            var dir = Vec3.Forward;
            var result = ShootingGame.Shared.Hero.SpreadUtility.ApplyConeSpread(dir, 0f, 12345);
            Assert.Equal(dir.x, result.x);
            Assert.Equal(dir.y, result.y);
            Assert.Equal(dir.z, result.z);
        }

        [Fact]
        public void Spread_SameSeed_SameResult()
        {
            // 双端一致性核心：同种子必须得到逐位相同的弹道
            var dir = Vec3.Forward;
            int seed = ShootingGame.Shared.Hero.SpreadUtility.MakeSeed(42, 3);
            var a = ShootingGame.Shared.Hero.SpreadUtility.ApplyConeSpread(dir, 2.5f, seed);
            var b = ShootingGame.Shared.Hero.SpreadUtility.ApplyConeSpread(dir, 2.5f, seed);
            Assert.Equal(a.x, b.x);
            Assert.Equal(a.y, b.y);
            Assert.Equal(a.z, b.z);
        }

        [Fact]
        public void Spread_WithinMaxAngle()
        {
            var dir = Vec3.Forward;
            float spreadDeg = 3f;
            for (int seed = 1; seed <= 50; seed++)
            {
                var r = ShootingGame.Shared.Hero.SpreadUtility.ApplyConeSpread(dir, spreadDeg, seed * 7919);
                float angle = Vec3.Angle(dir, r);
                Assert.True(angle <= spreadDeg + 0.01f, $"seed={seed}: 偏转角 {angle}° 超过上限 {spreadDeg}°");
            }
        }

        [Fact]
        public void Spread_ComputeTotal_StacksMoveAndBloom()
        {
            var gun = new ShootingGame.Shared.Hero.GunConfigData
            {
                SpreadAngle = 1f,
                MoveSpreadAdd = 2f,
                BloomPerShot = 0.5f
            };
            float still = ShootingGame.Shared.Hero.SpreadUtility.ComputeTotalSpread(gun, false, 0f);
            float moving = ShootingGame.Shared.Hero.SpreadUtility.ComputeTotalSpread(gun, true, 0f);
            float bloomed = ShootingGame.Shared.Hero.SpreadUtility.ComputeTotalSpread(gun, false, 1.5f);
            Assert.Equal(1f, still);
            Assert.Equal(3f, moving);
            Assert.Equal(2.5f, bloomed);
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
