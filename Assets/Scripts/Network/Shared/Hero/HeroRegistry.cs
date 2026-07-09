using System.Collections.Generic;
using ShootingGame.Shared.Ability;

namespace ShootingGame.Shared.Hero
{
    public static class HeroRegistry
    {
        private static Dictionary<int, HeroConfig> _heroes;
        private static bool _initialized;

        public const int DefaultHeroId = 1;

        /// <summary>
        /// 从 Resources 加载枪械配置（缓存）。
        /// </summary>
        private static GunConfig LoadGun(string name) => UnityEngine.Resources.Load<GunConfig>($"Guns/{name}");

        public static void Initialize()
        {
            if (_initialized) return;

            _heroes = new Dictionary<int, HeroConfig>
            {
                [1] = new HeroConfig
                {
                    HeroId = 1, Name = "突击兵", MaxHP = 100,
                    MoveSpeed = 6f, PlayerRadius = 0.35f, PlayerHeight = 1.8f,
                    StartingGun = LoadGun("Rifle_SemiAuto"),
                    Abilities = new AbilityConfig[]
                    {
                        CreateSharedFire(1),
                        CreateSharedReload(2),
                        CreateSharedJump(3),
                        CreateDash(11),
                    }
                },
                [2] = new HeroConfig
                {
                    HeroId = 2, Name = "重装兵", MaxHP = 200,
                    MoveSpeed = 4.5f, PlayerRadius = 0.50f, PlayerHeight = 2.0f,
                    StartingGun = LoadGun("Shotgun_Pump"),
                    Abilities = new AbilityConfig[]
                    {
                        CreateSharedFire(1),
                        CreateSharedReload(2),
                        CreateShield(20),
                        CreateCharge(21),
                    }
                },
                [3] = new HeroConfig
                {
                    HeroId = 3, Name = "狙击手", MaxHP = 75,
                    MoveSpeed = 5.5f, PlayerRadius = 0.30f, PlayerHeight = 1.7f,
                    StartingGun = LoadGun("Sniper_BoltAction"),
                    Abilities = new AbilityConfig[]
                    {
                        CreateSharedFire(1),
                        CreateSharedReload(2),
                        CreateStealth(30),
                        CreateMarkShot(31),
                    }
                },
            };

            _initialized = true;
        }

        public static HeroConfig GetHero(int heroId)
        {
            if (_heroes != null && _heroes.TryGetValue(heroId, out var config))
                return config;
            return null;
        }

        public static bool TryGetHero(int heroId, out HeroConfig config)
        {
            config = null;
            if (_heroes == null) return false;
            return _heroes.TryGetValue(heroId, out config);
        }

        private static AbilityConfig CreateSharedFire(byte assetId) => new AbilityConfig
        {
            AssetId = assetId, Name = "Fire", Cooldown = 0.15f, Duration = 0f,
            BehaviorTypeName = "ShootingGame.Shared.Ability.Abilities.FireWeaponAbility",
        };

        private static AbilityConfig CreateSharedReload(byte assetId) => new AbilityConfig
        {
            AssetId = assetId, Name = "Reload", Cooldown = 0f, Duration = 2.0f,
            BehaviorTypeName = "ShootingGame.Shared.Ability.Abilities.ReloadWeaponAbility",
        };

        private static AbilityConfig CreateSharedJump(byte assetId) => new AbilityConfig
        {
            AssetId = assetId, Name = "Jump", Cooldown = 0f, Duration = 0f,
            BehaviorTypeName = "ShootingGame.Shared.Ability.Abilities.JumpAbility",
        };

        private static AbilityConfig CreateDash(byte assetId) => new AbilityConfig
        {
            AssetId = assetId, Name = "Dash", Cooldown = 1.5f, Duration = 0f,
            BehaviorTypeName = "ShootingGame.Shared.Ability.Abilities.DashAbility",
        };

        private static AbilityConfig CreateShield(byte assetId) => new AbilityConfig
        {
            AssetId = assetId, Name = "Shield", Cooldown = 8f, Duration = 3.0f,
            BehaviorTypeName = "ShootingGame.Shared.Ability.Abilities.ShieldAbility",
        };

        private static AbilityConfig CreateCharge(byte assetId) => new AbilityConfig
        {
            AssetId = assetId, Name = "Charge", Cooldown = 6f, Duration = 0.3f,
            BehaviorTypeName = "ShootingGame.Shared.Ability.Abilities.ChargeAbility",
        };

        private static AbilityConfig CreateStealth(byte assetId) => new AbilityConfig
        {
            AssetId = assetId, Name = "Stealth", Cooldown = 12f, Duration = 5.0f,
            BehaviorTypeName = "ShootingGame.Shared.Ability.Abilities.StealthAbility",
        };

        private static AbilityConfig CreateMarkShot(byte assetId) => new AbilityConfig
        {
            AssetId = assetId, Name = "MarkShot", Cooldown = 10f, Duration = 0f,
            BehaviorTypeName = "ShootingGame.Shared.Ability.Abilities.MarkShotAbility",
        };
    }
}
