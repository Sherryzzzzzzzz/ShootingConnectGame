using ShootingGame.Shared.ECS;
using ShootingGame.Shared.Math;
using ShootingGame.Shared.Simulation;
using Xunit;

namespace ShootingGame.Tests
{
    public class ECSIntegrationTests
    {
        const float DT = GameConstants.TickDelta;
        const float E = 0.02f;

        private static Entity CreatePlayerEntity(EntityManager em, PlayerSnapshot snap)
        {
            var entity = em.CreateEntity();

            em.AddComponent(entity, new TransformComponent(snap.Position, snap.Rotation));
            em.AddComponent(entity, new MovementComponent(snap.Velocity, snap.VerticalVelocity, snap.IsGrounded));
            em.AddComponent(entity, new PlayerStateComponent(snap.State));
            em.AddComponent(entity, new FireCooldownComponent(snap.FireCooldown));
            em.AddComponent(entity, new HealthComponent(snap.Health, GameConstants.MaxHealth));
            em.AddComponent(entity, new AmmoComponent(snap.CurrentAmmo, GameConstants.MaxAmmoPerClip));
            em.AddComponent(entity, new ReloadComponent(snap.IsReloading, snap.ReloadTimer));
            em.AddComponent(entity, new InputComponent());

            return entity;
        }

        private static PlayerSnapshot BuildSnapshot(EntityManager em, Entity entity, int tick)
        {
            var snap = new PlayerSnapshot { Tick = tick };

            if (em.TryGetComponent<TransformComponent>(entity, out var tx))
            {
                snap.Position = tx.Position;
                snap.Rotation = tx.Rotation;
            }
            if (em.TryGetComponent<MovementComponent>(entity, out var mv))
            {
                snap.Velocity = mv.Velocity;
                snap.VerticalVelocity = mv.VerticalVelocity;
                snap.IsGrounded = mv.IsGrounded;
            }
            if (em.TryGetComponent<PlayerStateComponent>(entity, out var ps))
                snap.State = ps.State;
            if (em.TryGetComponent<FireCooldownComponent>(entity, out var fc))
                snap.FireCooldown = fc.Cooldown;
            if (em.TryGetComponent<HealthComponent>(entity, out var hp))
                snap.Health = hp.Current;
            if (em.TryGetComponent<AmmoComponent>(entity, out var ammo))
                snap.CurrentAmmo = ammo.Current;
            if (em.TryGetComponent<ReloadComponent>(entity, out var rel))
            {
                snap.IsReloading = rel.IsReloading;
                snap.ReloadTimer = rel.Timer;
            }

            return snap;
        }

        [Fact]
        public void NoInput_NoMovement()
        {
            var em = new EntityManager();
            var snap = PlayerSnapshot.Default(Vec3.Zero);
            var entity = CreatePlayerEntity(em, snap);
            var input = new InputFrame { Tick = 0 };

            PlayerSystemGroup.TickPlayer(em, entity, input, DT);
            var result = BuildSnapshot(em, entity, 0);

            Assert.InRange(result.Position.x, -E, E);
            Assert.InRange(result.Position.z, -E, E);
        }

        [Fact]
        public void MoveForward()
        {
            var em = new EntityManager();
            var snap = PlayerSnapshot.Default(Vec3.Zero);
            var entity = CreatePlayerEntity(em, snap);
            var input = new InputFrame { Tick = 0, Movement = new Vec2(0, 1) };

            PlayerSystemGroup.TickPlayer(em, entity, input, DT);
            var result = BuildSnapshot(em, entity, 0);

            float expectedZ = GameConstants.MoveSpeed * DT;
            Assert.InRange(result.Position.z, expectedZ - E, expectedZ + E);
            Assert.InRange(result.Position.x, -E, E);
        }

        [Fact]
        public void MoveForward_Running()
        {
            var em = new EntityManager();
            var snap = PlayerSnapshot.Default(Vec3.Zero);
            var entity = CreatePlayerEntity(em, snap);
            var input = new InputFrame { Tick = 0, Movement = new Vec2(0, 1), Run = true };

            PlayerSystemGroup.TickPlayer(em, entity, input, DT);
            var result = BuildSnapshot(em, entity, 0);

            float expectedZ = GameConstants.MoveSpeed * GameConstants.RunMultiplier * DT;
            Assert.InRange(result.Position.z, expectedZ - E, expectedZ + E);
        }

        [Fact]
        public void Gravity_WhenNotGrounded()
        {
            var em = new EntityManager();
            var snap = PlayerSnapshot.Default(new Vec3(0, 10, 0)); // 远离地面
            snap.IsGrounded = false;
            snap.VerticalVelocity = 0f;
            var entity = CreatePlayerEntity(em, snap);
            var input = new InputFrame();

            PlayerSystemGroup.TickPlayer(em, entity, input, DT);
            var result = BuildSnapshot(em, entity, 0);

            Assert.True(result.VerticalVelocity < 0f);
            Assert.True(result.Position.y < 10f);
        }

        [Fact]
        public void Jump_SetsVerticalVelocity()
        {
            var em = new EntityManager();
            var snap = PlayerSnapshot.Default(Vec3.Zero);
            snap.IsGrounded = true;
            var entity = CreatePlayerEntity(em, snap);
            var input = new InputFrame { Jump = true };

            PlayerSystemGroup.TickPlayer(em, entity, input, DT);
            var result = BuildSnapshot(em, entity, 0);

            float expectedVv = GameConstants.JumpForce + GameConstants.Gravity * DT;
            Assert.InRange(result.VerticalVelocity, expectedVv - E, expectedVv + E);
            Assert.False(result.IsGrounded);
        }

        [Fact]
        public void Jump_OnlyWhenGrounded()
        {
            var em = new EntityManager();
            var snap = PlayerSnapshot.Default(new Vec3(0, 10, 0)); // 远离地面
            snap.IsGrounded = false;
            snap.VerticalVelocity = -5f;
            var entity = CreatePlayerEntity(em, snap);
            var input = new InputFrame { Jump = true };

            PlayerSystemGroup.TickPlayer(em, entity, input, DT);
            var result = BuildSnapshot(em, entity, 0);

            Assert.True(result.VerticalVelocity < 0f);
        }

        [Fact]
        public void GroundSnap_WhenGroundedAndFalling()
        {
            var em = new EntityManager();
            var snap = PlayerSnapshot.Default(Vec3.Zero);
            snap.IsGrounded = true;
            snap.VerticalVelocity = -10f;
            var entity = CreatePlayerEntity(em, snap);
            var input = new InputFrame();

            PlayerSystemGroup.TickPlayer(em, entity, input, DT);
            var result = BuildSnapshot(em, entity, 0);

            Assert.InRange(result.VerticalVelocity, 0 - E, 0 + E);
            Assert.True(result.IsGrounded);
        }

        [Fact]
        public void FireCooldown_Decreases()
        {
            var em = new EntityManager();
            var snap = PlayerSnapshot.Default(Vec3.Zero);
            snap.FireCooldown = 0.1f;
            var entity = CreatePlayerEntity(em, snap);
            var input = new InputFrame();

            PlayerSystemGroup.TickPlayer(em, entity, input, DT);
            var result = BuildSnapshot(em, entity, 0);

            Assert.True(result.FireCooldown < 0.1f);
        }

        [Fact]
        public void Fire_SetsCooldown()
        {
            var em = new EntityManager();
            var snap = PlayerSnapshot.Default(Vec3.Zero);
            snap.FireCooldown = 0f;
            var entity = CreatePlayerEntity(em, snap);
            var input = new InputFrame { Fire = true };

            PlayerSystemGroup.TickPlayer(em, entity, input, DT);
            var result = BuildSnapshot(em, entity, 0);

            Assert.InRange(result.FireCooldown, GameConstants.FireRate - E, GameConstants.FireRate + E);
        }

        [Fact]
        public void Fire_RespectsCooldown()
        {
            var em = new EntityManager();
            var snap = PlayerSnapshot.Default(Vec3.Zero);
            snap.FireCooldown = 0.5f;
            var entity = CreatePlayerEntity(em, snap);
            var input = new InputFrame { Fire = true };

            PlayerSystemGroup.TickPlayer(em, entity, input, DT);
            var result = BuildSnapshot(em, entity, 0);

            Assert.True(result.FireCooldown < 0.5f);
            Assert.True(result.FireCooldown > GameConstants.FireRate);
        }

        [Fact]
        public void MultiTick_JumpArc()
        {
            var em = new EntityManager();
            var snap = PlayerSnapshot.Default(Vec3.Zero);
            snap.IsGrounded = true;
            var entity = CreatePlayerEntity(em, snap);

            // Jump
            var input = new InputFrame { Tick = 0, Jump = true };
            PlayerSystemGroup.TickPlayer(em, entity, input, DT);
            var result = BuildSnapshot(em, entity, 0);

            Assert.True(result.VerticalVelocity > 0f);
            float prevY = result.Position.y;

            bool wentUp = false;
            bool cameDown = false;

            // Write result back to entity and continue simulation
            for (int i = 1; i <= 120; i++)
            {
                // Apply snapshot back to entity
                ECSBridge.ApplyServerCorrection(em, entity, result);
                var tickInput = new InputFrame { Tick = i };
                PlayerSystemGroup.TickPlayer(em, entity, tickInput, DT);
                result = BuildSnapshot(em, entity, i);

                if (result.Position.y > prevY) wentUp = true;
                if (result.Position.y < prevY && wentUp) cameDown = true;
                prevY = result.Position.y;
            }

            Assert.True(wentUp, "Player should rise during jump");
            Assert.True(cameDown, "Player should fall after peak");
        }

        [Fact]
        public void TickNumber_Updated()
        {
            var em = new EntityManager();
            var snap = PlayerSnapshot.Default(Vec3.Zero);
            var entity = CreatePlayerEntity(em, snap);
            var input = new InputFrame { Tick = 42 };

            PlayerSystemGroup.TickPlayer(em, entity, input, DT);
            var result = BuildSnapshot(em, entity, 42);

            Assert.Equal(42, result.Tick);
        }

        [Fact]
        public void ECS_Matches_PlayerSimulation_Output()
        {
            // Run same input through both old PlayerSimulation and new ECS, compare results
            var em = new EntityManager();
            var oldSnap = PlayerSnapshot.Default(Vec3.Zero);
            var entity = CreatePlayerEntity(em, oldSnap);

            var input = new InputFrame
            {
                Tick = 1,
                Movement = new Vec2(0.7f, 0.3f),
                Jump = true,
                Run = true,
                AimYaw = 45f
            };

            // Old path
            var oldResult = PlayerSimulation.Simulate(oldSnap, input, DT);

            // ECS path
            PlayerSystemGroup.TickPlayer(em, entity, input, DT);
            var ecsResult = BuildSnapshot(em, entity, 1);

            Assert.Equal(oldResult.Tick, ecsResult.Tick);
            Assert.InRange(ecsResult.Position.x, oldResult.Position.x - E, oldResult.Position.x + E);
            Assert.InRange(ecsResult.Position.y, oldResult.Position.y - E, oldResult.Position.y + E);
            Assert.InRange(ecsResult.Position.z, oldResult.Position.z - E, oldResult.Position.z + E);
            Assert.InRange(ecsResult.VerticalVelocity, oldResult.VerticalVelocity - E, oldResult.VerticalVelocity + E);
            Assert.Equal(oldResult.IsGrounded, ecsResult.IsGrounded);
        }

        [Fact]
        public void ECSBridge_CreateAndBuild_Roundtrip()
        {
            var em = new EntityManager();
            var snap = PlayerSnapshot.Default(new Vec3(10, 5, 20));
            snap.CurrentAmmo = 15;
            snap.IsReloading = true;
            snap.ReloadTimer = 1.5f;
            snap.FireCooldown = 0.3f;
            snap.Health = 75;
            snap.State = PlayerStateEnum.Sky;
            snap.IsGrounded = false;
            snap.VerticalVelocity = 3f;

            var entity = ECSBridge.CreatePlayerEntity(em, snap);
            var rebuilt = ECSBridge.BuildSnapshot(em, entity, 42);

            Assert.Equal(42, rebuilt.Tick);
            Assert.InRange(rebuilt.Position.x, 10 - E, 10 + E);
            Assert.InRange(rebuilt.Position.y, 5 - E, 5 + E);
            Assert.InRange(rebuilt.Position.z, 20 - E, 20 + E);
            Assert.Equal(15, rebuilt.CurrentAmmo);
            Assert.True(rebuilt.IsReloading);
            Assert.InRange(rebuilt.ReloadTimer, 1.5f - E, 1.5f + E);
            Assert.InRange(rebuilt.FireCooldown, 0.3f - E, 0.3f + E);
            Assert.Equal(75, rebuilt.Health);
            Assert.Equal(PlayerStateEnum.Sky, rebuilt.State);
            Assert.False(rebuilt.IsGrounded);
            Assert.InRange(rebuilt.VerticalVelocity, 3f - E, 3f + E);
        }

        [Fact]
        public void ECSBridge_ApplyServerCorrection_OverridesCorrectly()
        {
            var em = new EntityManager();
            var snap = PlayerSnapshot.Default(Vec3.Zero);
            var entity = ECSBridge.CreatePlayerEntity(em, snap);

            var serverSnap = new PlayerSnapshot
            {
                Tick = 5,
                Position = new Vec3(100, 50, 200),
                Rotation = Quat.Euler(0, 90, 0),
                Velocity = new Vec3(10, 0, 0),
                VerticalVelocity = -2f,
                IsGrounded = false,
                State = PlayerStateEnum.Sky,
                FireCooldown = 0.1f,
                Health = 50,
                CurrentAmmo = 10,
                IsReloading = true,
                ReloadTimer = 1.0f
            };

            ECSBridge.ApplyServerCorrection(em, entity, serverSnap);
            var result = ECSBridge.BuildSnapshot(em, entity, 5);

            Assert.InRange(result.Position.x, 100 - E, 100 + E);
            Assert.InRange(result.Position.y, 50 - E, 50 + E);
            Assert.Equal(50, result.Health);
            Assert.Equal(10, result.CurrentAmmo);
            Assert.True(result.IsReloading);
            Assert.Equal(PlayerStateEnum.Sky, result.State);
        }

        [Fact]
        public void ECSBridge_WriteInput_TransfersAllFields()
        {
            var em = new EntityManager();
            var entity = em.CreateEntity();
            em.AddComponent<InputComponent>(entity);

            var input = new InputFrame
            {
                Tick = 7,
                Movement = new Vec2(0.5f, -0.8f),
                Jump = true,
                Run = true,
                Aim = true,
                Fire = true,
                Reload = false,
                AimYaw = 120f,
                AimPitch = -15f
            };

            ECSBridge.WriteInput(em, entity, input);
            var ic = em.GetComponent<InputComponent>(entity);

            Assert.Equal(7, ic.Tick);
            Assert.InRange(ic.Movement.x, 0.5f - E, 0.5f + E);
            Assert.InRange(ic.Movement.y, -0.8f - E, -0.8f + E);
            Assert.True(ic.Jump);
            Assert.True(ic.Run);
            Assert.True(ic.Aim);
            Assert.True(ic.Fire);
            Assert.False(ic.Reload);
            Assert.InRange(ic.AimYaw, 120f - E, 120f + E);
        }

        [Fact]
        public void AmmoConsumption_DecreasesAmmo_OnFire()
        {
            var em = new EntityManager();
            var snap = PlayerSnapshot.Default(Vec3.Zero);
            snap.CurrentAmmo = 30;
            var entity = CreatePlayerEntity(em, snap);
            var input = new InputFrame { Fire = true };

            PlayerSystemGroup.TickPlayer(em, entity, input, DT);
            var result = BuildSnapshot(em, entity, 0);

            Assert.Equal(29, result.CurrentAmmo);
        }

        [Fact]
        public void AmmoConsumption_BlockedWhenReloading()
        {
            var em = new EntityManager();
            var snap = PlayerSnapshot.Default(Vec3.Zero);
            snap.CurrentAmmo = 10;
            snap.IsReloading = true;
            snap.ReloadTimer = 1.0f;
            var entity = CreatePlayerEntity(em, snap);
            var input = new InputFrame { Fire = true };

            PlayerSystemGroup.TickPlayer(em, entity, input, DT);
            var result = BuildSnapshot(em, entity, 0);

            Assert.Equal(10, result.CurrentAmmo); // unchanged
        }

        [Fact]
        public void Reload_RefillsAmmo_WhenTimerExpires()
        {
            var em = new EntityManager();
            var snap = PlayerSnapshot.Default(Vec3.Zero);
            snap.CurrentAmmo = 5;
            snap.IsReloading = true;
            snap.ReloadTimer = 0.01f; // almost done
            var entity = CreatePlayerEntity(em, snap);
            var input = new InputFrame();

            PlayerSystemGroup.TickPlayer(em, entity, input, DT);
            var result = BuildSnapshot(em, entity, 0);

            Assert.Equal(GameConstants.MaxAmmoPerClip, result.CurrentAmmo);
            Assert.False(result.IsReloading);
        }

        [Fact]
        public void Reload_Initiated_WhenEmptyAndRKey()
        {
            var em = new EntityManager();
            var snap = PlayerSnapshot.Default(Vec3.Zero);
            snap.CurrentAmmo = 10;
            var entity = CreatePlayerEntity(em, snap);
            var input = new InputFrame { Reload = true };

            PlayerSystemGroup.TickPlayer(em, entity, input, DT);
            var result = BuildSnapshot(em, entity, 0);

            Assert.True(result.IsReloading);
            Assert.InRange(result.ReloadTimer, GameConstants.ReloadTime - E, GameConstants.ReloadTime + E);
        }

        [Fact]
        public void DamageApplication_ReducesHealth()
        {
            var em = new EntityManager();
            var entity = em.CreateEntity();
            em.AddComponent(entity, new HealthComponent(100, 100));

            bool isDead = DamageApplicationSystem.ApplyDamage(em, entity, 25);

            Assert.False(isDead);
            var hc = em.GetComponent<HealthComponent>(entity);
            Assert.Equal(75, hc.Current);
        }

        [Fact]
        public void DamageApplication_KillAtZero()
        {
            var em = new EntityManager();
            var entity = em.CreateEntity();
            em.AddComponent(entity, new HealthComponent(20, 100));

            bool isDead = DamageApplicationSystem.ApplyDamage(em, entity, 25);

            Assert.True(isDead);
            var hc = em.GetComponent<HealthComponent>(entity);
            Assert.Equal(0, hc.Current);
        }

        [Fact]
        public void DamageApplication_NoDoubleKill()
        {
            var em = new EntityManager();
            var entity = em.CreateEntity();
            em.AddComponent(entity, new HealthComponent(0, 100)); // already dead

            bool isDead = DamageApplicationSystem.ApplyDamage(em, entity, 25);

            Assert.False(isDead); // should not report kill on already-dead entity
        }
    }
}
