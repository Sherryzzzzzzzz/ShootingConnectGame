using ShootingGame.Shared.Ability;
using ShootingGame.Shared.ECS;
using ShootingGame.Shared.GameplayTags;
using ShootingGame.Shared.Hero;
using ShootingGame.Shared.Protocol;
using ShootingGame.Shared.Simulation;
using Xunit;

namespace ShootingGame.Tests
{
    public class HeroTests
    {
        const float DT = GameConstants.TickDelta;
        const float E = 0.02f;

        public HeroTests()
        {
            ShootingGame.Shared.GameplayTags.GameplayTagConfig.Initialize();
            HeroRegistry.Initialize();
        }

        #region HeroRegistry

        [Fact]
        public void HeroRegistry_DefaultHeroId_IsSoldier()
        {
            Assert.Equal(1, HeroRegistry.DefaultHeroId);
        }

        [Fact]
        public void HeroRegistry_GetSoldier_CorrectStats()
        {
            var hero = HeroRegistry.GetHero(1);
            Assert.NotNull(hero);
            Assert.Equal("Soldier", hero.Name);
            Assert.Equal(100, hero.MaxHP);
            Assert.InRange(hero.MoveSpeed, 6f - E, 6f + E);
            Assert.Equal(4, hero.Abilities.Length);
        }

        [Fact]
        public void HeroRegistry_GetTank_CorrectStats()
        {
            var hero = HeroRegistry.GetHero(2);
            Assert.NotNull(hero);
            Assert.Equal("Tank", hero.Name);
            Assert.Equal(200, hero.MaxHP);
            Assert.InRange(hero.MoveSpeed, 4.5f - E, 4.5f + E);
            Assert.InRange(hero.PlayerRadius, 0.50f - E, 0.50f + E);
        }

        [Fact]
        public void HeroRegistry_GetSniper_CorrectStats()
        {
            var hero = HeroRegistry.GetHero(3);
            Assert.NotNull(hero);
            Assert.Equal("Sniper", hero.Name);
            Assert.Equal(75, hero.MaxHP);
            Assert.InRange(hero.MoveSpeed, 5.5f - E, 5.5f + E);
            Assert.InRange(hero.PlayerRadius, 0.30f - E, 0.30f + E);
        }

        [Fact]
        public void HeroRegistry_TryGetHero_Valid()
        {
            Assert.True(HeroRegistry.TryGetHero(2, out var hero));
            Assert.Equal("Tank", hero.Name);
        }

        [Fact]
        public void HeroRegistry_TryGetHero_Invalid()
        {
            Assert.False(HeroRegistry.TryGetHero(99, out _));
        }

        [Fact]
        public void HeroRegistry_GetHero_InvalidReturnsNull()
        {
            Assert.Null(HeroRegistry.GetHero(99));
        }

        [Fact]
        public void HeroRegistry_AllHeroesHaveFourAbilities()
        {
            for (int i = 1; i <= 3; i++)
            {
                var hero = HeroRegistry.GetHero(i);
                Assert.NotNull(hero);
                Assert.Equal(4, hero.Abilities.Length);
            }
        }

        [Fact]
        public void HeroRegistry_HeroSpecificAssetIdsAreUnique()
        {
            // AssetIds 1-9 are shared across all heroes; 10+ should be unique per hero
            var heroSpecificIds = new System.Collections.Generic.HashSet<int>();
            for (int i = 1; i <= 3; i++)
            {
                foreach (var ab in HeroRegistry.GetHero(i).Abilities)
                {
                    if (ab.AssetId >= 10)
                    {
                        Assert.True(heroSpecificIds.Add(ab.AssetId),
                            $"Duplicate hero-specific AssetId {ab.AssetId} in hero {i}");
                    }
                }
            }
            Assert.True(heroSpecificIds.Count >= 5, "Expected at least 5 hero-specific abilities");
        }

        [Fact]
        public void HeroRegistry_SoldierHasDash()
        {
            var soldier = HeroRegistry.GetHero(1);
            bool hasDash = false;
            foreach (var ab in soldier.Abilities)
            {
                if (ab.BehaviorTypeName.Contains("DashAbility"))
                    hasDash = true;
            }
            Assert.True(hasDash, "Soldier should have Dash ability");
        }

        [Fact]
        public void HeroRegistry_TankHasShield()
        {
            var tank = HeroRegistry.GetHero(2);
            bool hasShield = false;
            foreach (var ab in tank.Abilities)
            {
                if (ab.BehaviorTypeName.Contains("ShieldAbility"))
                    hasShield = true;
            }
            Assert.True(hasShield, "Tank should have Shield ability");
        }

        [Fact]
        public void HeroRegistry_SniperHasStealth()
        {
            var sniper = HeroRegistry.GetHero(3);
            bool hasStealth = false;
            foreach (var ab in sniper.Abilities)
            {
                if (ab.BehaviorTypeName.Contains("StealthAbility"))
                    hasStealth = true;
            }
            Assert.True(hasStealth, "Sniper should have Stealth ability");
        }

        #endregion

        #region Protocol Round-Trip (HeroId)

        [Fact]
        public void BattlePlayerInfo_RoundTrip_HeroId()
        {
            var pack = new MainPack
            {
                RequestCode = RequestCode.Matching,
                ActionCode = ActionCode.MatchFound,
                BattleInfo = new BattleInfo
                {
                    BattleId = 42,
                    BattlePlayers =
                    {
                        new BattlePlayerInfo
                        {
                            PlayerId = 0, TeamId = 1, UserId = 100,
                            PlayerName = "TestPlayer", HeroId = 2
                        }
                    }
                }
            };

            byte[] data = ProtobufSerializer.SerializeMainPack(pack);
            var result = ProtobufSerializer.DeserializeMainPack(data);

            Assert.NotNull(result.BattleInfo);
            Assert.Single(result.BattleInfo.BattlePlayers);
            var bp = result.BattleInfo.BattlePlayers[0];
            Assert.Equal(0, bp.PlayerId);
            Assert.Equal(1, bp.TeamId);
            Assert.Equal(100, bp.UserId);
            Assert.Equal("TestPlayer", bp.PlayerName);
            Assert.Equal(2, bp.HeroId);
        }

        [Fact]
        public void PlayerStateMsg_RoundTrip_MaxHp()
        {
            var pack = new MainPack
            {
                RequestCode = RequestCode.Battle,
                ActionCode = ActionCode.BattleReady,
                BattleInfo = new BattleInfo
                {
                    BattleId = 100,
                    PlayerStates =
                    {
                        new PlayerStateMsg { PlayerId = 1, Hp = 150, MaxHp = 200 }
                    }
                }
            };

            byte[] data = ProtobufSerializer.SerializeMainPack(pack);
            var result = ProtobufSerializer.DeserializeMainPack(data);

            Assert.NotNull(result.BattleInfo);
            Assert.Single(result.BattleInfo.PlayerStates);
            var ps = result.BattleInfo.PlayerStates[0];
            Assert.Equal(1, ps.PlayerId);
            Assert.Equal(150, ps.Hp);
            Assert.Equal(200, ps.MaxHp);
        }

        [Fact]
        public void BattlePlayerInfo_HeroId_DefaultZero_StillRoundTrips()
        {
            var pack = new MainPack
            {
                RequestCode = RequestCode.Matching,
                ActionCode = ActionCode.MatchFound,
                BattleInfo = new BattleInfo
                {
                    BattleId = 1,
                    BattlePlayers =
                    {
                        new BattlePlayerInfo { PlayerId = 1, PlayerName = "DefaultHero", HeroId = 0 }
                    }
                }
            };

            byte[] data = ProtobufSerializer.SerializeMainPack(pack);
            var result = ProtobufSerializer.DeserializeMainPack(data);

            Assert.Equal(0, result.BattleInfo.BattlePlayers[0].HeroId);
        }

        #endregion

        #region HealthComponent with HeroConfig

        [Fact]
        public void HealthComponent_TankMaxHP_Is200()
        {
            var tank = HeroRegistry.GetHero(2);
            Assert.Equal(200, tank.MaxHP);

            var em = new EntityManager();
            var entity = em.CreateEntity();
            em.AddComponent(entity, new HealthComponent(tank.MaxHP, tank.MaxHP));

            var hc = em.GetComponent<HealthComponent>(entity);
            Assert.Equal(200, hc.Max);
            Assert.Equal(200, hc.Current);
        }

        [Fact]
        public void HealthComponent_SoldierMaxHP_Is100()
        {
            var soldier = HeroRegistry.GetHero(1);
            Assert.Equal(100, soldier.MaxHP);
        }

        [Fact]
        public void HealthComponent_SniperMaxHP_Is75()
        {
            var sniper = HeroRegistry.GetHero(3);
            Assert.Equal(75, sniper.MaxHP);
        }

        #endregion

        #region MovementComponent with HeroConfig

        [Fact]
        public void MovementComponent_TankSettings_Applied()
        {
            var tank = HeroRegistry.GetHero(2);
            var em = new EntityManager();
            var entity = ECSBridge.CreatePlayerEntity(em, PlayerSnapshot.Default(Shared.Math.Vec3.Zero));

            if (em.TryGetComponent<MovementComponent>(entity, out var move))
            {
                move.PlayerRadius = tank.PlayerRadius;
                move.PlayerHeight = tank.PlayerHeight;
                move.MaxMoveSpeed = tank.MoveSpeed;
                em.SetComponent(entity, move);
            }

            em.TryGetComponent<MovementComponent>(entity, out var result);
            Assert.InRange(result.MaxMoveSpeed, 4.5f - E, 4.5f + E);
            Assert.InRange(result.PlayerRadius, 0.50f - E, 0.50f + E);
            Assert.InRange(result.PlayerHeight, 2.0f - E, 2.0f + E);
        }

        [Fact]
        public void MovementComponent_DefaultValues_Preserved()
        {
            // Without hero config, MovementComponent should use GameConstants defaults
            var mv = new MovementComponent(Shared.Math.Vec3.Zero, 0f, true);
            Assert.Equal(GameConstants.PlayerRadius, mv.PlayerRadius);
            Assert.Equal(GameConstants.PlayerHeight, mv.PlayerHeight);
            Assert.Equal(GameConstants.MoveSpeed, mv.MaxMoveSpeed);
        }

        #endregion

        #region DashAbility Behavior

        private static AbilityConfig TestConfig() => new AbilityConfig
        {
            AssetId = 99, Name = "Test", Cooldown = 0, Duration = 0,
            BehaviorTypeName = "Test"
        };

        [Fact]
        public void DashAbility_CanActivate_WhenGrounded()
        {
            var em = new EntityManager();
            var entity = em.CreateEntity();
            em.AddComponent(entity, new MovementComponent(Shared.Math.Vec3.Zero, 0f, true));
            em.AddComponent(entity, new HealthComponent(100, 100));

            var dash = new ShootingGame.Shared.Ability.Abilities.DashAbility();
            Assert.True(dash.CanActivate(em, entity, TestConfig()));
        }

        [Fact]
        public void DashAbility_CannotActivate_WhenAirborne()
        {
            var em = new EntityManager();
            var entity = em.CreateEntity();
            em.AddComponent(entity, new MovementComponent(Shared.Math.Vec3.Zero, 5f, false));
            em.AddComponent(entity, new HealthComponent(100, 100));

            var dash = new ShootingGame.Shared.Ability.Abilities.DashAbility();
            Assert.False(dash.CanActivate(em, entity, TestConfig()));
        }

        [Fact]
        public void DashAbility_OnActivate_AppliesVelocity()
        {
            var em = new EntityManager();
            var entity = em.CreateEntity();
            em.AddComponent(entity, new TransformComponent(Shared.Math.Vec3.Zero, Shared.Math.Quat.Identity));
            em.AddComponent(entity, new MovementComponent(Shared.Math.Vec3.Zero, 0f, true));
            em.AddComponent(entity, new HealthComponent(100, 100));
            em.AddComponent(entity, new TagContainer());

            var dash = new ShootingGame.Shared.Ability.Abilities.DashAbility();
            dash.OnActivate(em, entity, TestConfig());

            em.TryGetComponent<MovementComponent>(entity, out var mv);
            float speed = mv.Velocity.Magnitude;
            Assert.True(speed > 10f, $"Dash speed {speed} should be > 10");
        }

        #endregion

        #region ShieldAbility

        [Fact]
        public void ShieldAbility_OnActivate_NoExceptions()
        {
            var em = new EntityManager();
            var entity = em.CreateEntity();
            em.AddComponent(entity, new MovementComponent(Shared.Math.Vec3.Zero, 0f, true));
            em.AddComponent(entity, new HealthComponent(100, 100));
            em.AddComponent(entity, new TagContainer());

            var shield = new ShootingGame.Shared.Ability.Abilities.ShieldAbility();
            shield.OnActivate(em, entity, TestConfig());
        }

        #endregion

        #region StealthAbility

        [Fact]
        public void StealthAbility_OnActivate_NoExceptions()
        {
            var em = new EntityManager();
            var entity = em.CreateEntity();
            em.AddComponent(entity, new MovementComponent(Shared.Math.Vec3.Zero, 0f, true));
            em.AddComponent(entity, new HealthComponent(100, 100));
            em.AddComponent(entity, new TagContainer());

            var stealth = new ShootingGame.Shared.Ability.Abilities.StealthAbility();
            stealth.OnActivate(em, entity, TestConfig());
        }

        #endregion

        #region ChargeAbility

        [Fact]
        public void ChargeAbility_OnActivate_AppliesVelocity()
        {
            var em = new EntityManager();
            var entity = em.CreateEntity();
            em.AddComponent(entity, new TransformComponent(Shared.Math.Vec3.Zero, Shared.Math.Quat.Identity));
            em.AddComponent(entity, new MovementComponent(Shared.Math.Vec3.Zero, 0f, true));
            em.AddComponent(entity, new HealthComponent(100, 100));
            em.AddComponent(entity, new TagContainer());

            var charge = new ShootingGame.Shared.Ability.Abilities.ChargeAbility();
            charge.OnActivate(em, entity, TestConfig());

            em.TryGetComponent<MovementComponent>(entity, out var mv);
            float speed = mv.Velocity.Magnitude;
            Assert.True(speed > 15f, $"Charge speed {speed} should be > 15");
        }

        #endregion

        #region MarkShotAbility

        [Fact]
        public void MarkShotAbility_OnActivate_NoExceptions()
        {
            var em = new EntityManager();
            var entity = em.CreateEntity();
            em.AddComponent(entity, new MovementComponent(Shared.Math.Vec3.Zero, 0f, true));
            em.AddComponent(entity, new HealthComponent(100, 100));
            em.AddComponent(entity, new TagContainer());

            var markShot = new ShootingGame.Shared.Ability.Abilities.MarkShotAbility();
            markShot.OnActivate(em, entity, TestConfig());
        }

        #endregion

        #region Hero-specific Movement Speed

        [Fact]
        public void Tank_MovesSlower_ThanSoldier()
        {
            var tank = HeroRegistry.GetHero(2);
            var soldier = HeroRegistry.GetHero(1);
            Assert.True(tank.MoveSpeed < soldier.MoveSpeed);
        }

        [Fact]
        public void Sniper_MovesBetween_SoldierAndTank()
        {
            var soldier = HeroRegistry.GetHero(1);
            var tank = HeroRegistry.GetHero(2);
            var sniper = HeroRegistry.GetHero(3);

            Assert.True(sniper.MoveSpeed < soldier.MoveSpeed);
            Assert.True(sniper.MoveSpeed > tank.MoveSpeed);
        }

        #endregion

        #region ECS + HeroConfig Integration

        [Fact]
        public void CreatePlayerEntity_WithTankConfig_HasCorrectStats()
        {
            var tank = HeroRegistry.GetHero(2);
            var em = new EntityManager();
            var snap = PlayerSnapshot.Default(Shared.Math.Vec3.Zero);
            var entity = ECSBridge.CreatePlayerEntity(em, snap);

            // Apply tank overrides (mimicking ServerECSWorld.RegisterPlayer)
            if (em.TryGetComponent<MovementComponent>(entity, out var move))
            {
                move.PlayerRadius = tank.PlayerRadius;
                move.PlayerHeight = tank.PlayerHeight;
                move.MaxMoveSpeed = tank.MoveSpeed;
                em.SetComponent(entity, move);
            }
            if (em.TryGetComponent<HealthComponent>(entity, out var hp))
            {
                hp.Max = tank.MaxHP;
                hp.Current = tank.MaxHP;
                em.SetComponent(entity, hp);
            }

            em.TryGetComponent<HealthComponent>(entity, out var hc);
            Assert.Equal(200, hc.Max);
            Assert.Equal(200, hc.Current);

            em.TryGetComponent<MovementComponent>(entity, out var mv);
            Assert.InRange(mv.MaxMoveSpeed, 4.5f - E, 4.5f + E);
        }

        [Fact]
        public void ECS_TickWithHeroSpeed_MovesAtCorrectRate()
        {
            var em = new EntityManager();
            var snap = PlayerSnapshot.Default(Shared.Math.Vec3.Zero);
            var entity = ECSBridge.CreatePlayerEntity(em, snap);

            // Apply hero-specific speed (Tank: 4.5)
            if (em.TryGetComponent<MovementComponent>(entity, out var move))
            {
                move.MaxMoveSpeed = 4.5f;
                em.SetComponent(entity, move);
            }

            var input = new InputFrame { Movement = new Shared.Math.Vec2(0, 1) };
            PlayerSystemGroup.TickPlayer(em, entity, input, DT);
            var result = ECSBridge.BuildSnapshot(em, entity, 0);

            float expectedZ = 4.5f * DT;
            Assert.InRange(result.Position.z, expectedZ - E, expectedZ + E);
            Assert.InRange(result.Position.x, -E, E);
        }

        #endregion
    }
}
