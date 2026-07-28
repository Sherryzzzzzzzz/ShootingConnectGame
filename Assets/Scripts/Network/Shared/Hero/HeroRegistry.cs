using System.Collections.Generic;
using System.Linq;
using ShootingGame.Shared.Ability;

namespace ShootingGame.Shared.Hero
{
    /// <summary>
    /// 英雄注册表。运行时从 Resources/Heroes/ 下的 HeroConfigSO 加载所有英雄配置。
    /// 替代旧版硬编码方案，支持通过 ScriptableObject 自由增删英雄。
    /// </summary>
    public static class HeroRegistry
    {
        private static Dictionary<int, HeroConfig> _heroes;
        private static bool _initialized;

        public const int DefaultHeroId = 1;

        /// <summary>
        /// 从 Resources/Heroes/ 加载所有 HeroConfigSO，转换为 HeroConfig 注册。
        /// </summary>
        public static void Initialize()
        {
            if (_initialized) return;

            _heroes = new Dictionary<int, HeroConfig>();

            var configs = UnityEngine.Resources.LoadAll<HeroConfigSO>("Heroes");
            if (configs == null || configs.Length == 0)
            {
                // 回退：如果没有 SO 资产，使用硬编码默认值
                UnityEngine.Debug.LogWarning("[HeroRegistry] Resources/Heroes/ 下没有找到 HeroConfigSO，使用硬编码默认英雄。请运行 游戏/配置/创建全部英雄配置。");
                InitFallbackHeroes();
                _initialized = true;
                return;
            }

            foreach (var so in configs)
            {
                if (so.HeroId <= 0)
                {
                    UnityEngine.Debug.LogWarning($"[HeroRegistry] 跳过无效 HeroId={so.HeroId} 的配置: {so.name}");
                    continue;
                }

                if (_heroes.ContainsKey(so.HeroId))
                {
                    UnityEngine.Debug.LogWarning($"[HeroRegistry] HeroId={so.HeroId} 重复，跳过: {so.name}");
                    continue;
                }

                var hero = so.ToHeroConfig();
                hero.Abilities = BuildAbilities(so.AbilityAssetIds);
                hero.StartingGunId = so.StartingGun != null ? so.StartingGun.name : null;
                hero.Gun = GunRegistry.GetGun(hero.StartingGunId);
                _heroes[so.HeroId] = hero;
            }

            _initialized = true;
            UnityEngine.Debug.Log($"[HeroRegistry] 从 Resources/Heroes/ 加载了 {_heroes.Count} 个英雄配置");
        }

        /// <summary>
        /// 获取所有已注册的英雄配置（供 UI 动态遍历）。
        /// </summary>
        public static List<HeroConfig> GetAllHeroes()
        {
            if (_heroes == null) Initialize();
            return _heroes.Values.OrderBy(h => h.HeroId).ToList();
        }

        public static HeroConfig GetHero(int heroId)
        {
            if (_heroes == null) Initialize();
            if (_heroes != null && _heroes.TryGetValue(heroId, out var config))
                return config;
            return null;
        }

        public static bool TryGetHero(int heroId, out HeroConfig config)
        {
            config = null;
            if (_heroes == null) Initialize();
            if (_heroes == null) return false;
            return _heroes.TryGetValue(heroId, out config);
        }

        /// <summary>
        /// 根据 AssetId 数组构建 AbilityConfig[]。
        /// 共享技能 (1-4) 和英雄专属技能 (11+) 分别解析。
        /// </summary>
        private static AbilityConfig[] BuildAbilities(int[] assetIds)
        {
            if (assetIds == null || assetIds.Length == 0)
                return System.Array.Empty<AbilityConfig>();

            var list = new List<AbilityConfig>(assetIds.Length);
            foreach (int id in assetIds)
            {
                var cfg = GetAbilityTemplate(id);
                if (cfg != null)
                    list.Add(cfg);
                else
                    UnityEngine.Debug.LogWarning($"[HeroRegistry] 未知的技能 AssetId={id}");
            }
            return list.ToArray();
        }

        /// <summary>
        /// 技能模板查找表。优先从 Resources/Abilities/ 下的 AbilityConfigSO 读取；
        /// 找不到 SO 时回退到内置硬编码模板。
        /// </summary>
        private static AbilityConfig GetAbilityTemplate(int assetId)
        {
            EnsureAbilityTemplatesLoaded();
            if (_abilityTemplates != null && _abilityTemplates.TryGetValue(assetId, out var cfg))
                return cfg;

            switch (assetId)
            {
                // --- 共享技能 ---
                case 1: return new AbilityConfig
                {
                    AssetId = 1, Name = "Fire", Cooldown = 0.15f, Duration = 0f,
                    BehaviorTypeName = "ShootingGame.Shared.Ability.Abilities.FireWeaponAbility",
                };
                case 2: return new AbilityConfig
                {
                    AssetId = 2, Name = "Reload", Cooldown = 0f, Duration = 2.0f,
                    BehaviorTypeName = "ShootingGame.Shared.Ability.Abilities.ReloadWeaponAbility",
                };
                case 3: return new AbilityConfig
                {
                    AssetId = 3, Name = "Jump", Cooldown = 0f, Duration = 0f,
                    BehaviorTypeName = "ShootingGame.Shared.Ability.Abilities.JumpAbility",
                };

                // --- 突击兵专属 ---
                case 11: return new AbilityConfig
                {
                    AssetId = 11, Name = "Dash", Cooldown = 1.5f, Duration = 0f,
                    BehaviorTypeName = "ShootingGame.Shared.Ability.Abilities.DashAbility",
                };

                // --- 重装兵专属 ---
                case 20: return new AbilityConfig
                {
                    AssetId = 20, Name = "Shield", Cooldown = 8f, Duration = 3.0f,
                    BehaviorTypeName = "ShootingGame.Shared.Ability.Abilities.ShieldAbility",
                };
                case 21: return new AbilityConfig
                {
                    AssetId = 21, Name = "Charge", Cooldown = 6f, Duration = 0.3f,
                    BehaviorTypeName = "ShootingGame.Shared.Ability.Abilities.ChargeAbility",
                };

                // --- 狙击手专属 ---
                case 30: return new AbilityConfig
                {
                    AssetId = 30, Name = "Stealth", Cooldown = 12f, Duration = 5.0f,
                    BehaviorTypeName = "ShootingGame.Shared.Ability.Abilities.StealthAbility",
                };
                case 31: return new AbilityConfig
                {
                    AssetId = 31, Name = "MarkShot", Cooldown = 10f, Duration = 0f,
                    BehaviorTypeName = "ShootingGame.Shared.Ability.Abilities.MarkShotAbility",
                };

                default: return null;
            }
        }

        private static Dictionary<int, AbilityConfig> _abilityTemplates;

        /// <summary>从 Resources/Abilities/ 加载技能模板 SO（只加载一次）。</summary>
        private static void EnsureAbilityTemplatesLoaded()
        {
            if (_abilityTemplates != null) return;
            _abilityTemplates = new Dictionary<int, AbilityConfig>();

            var sos = UnityEngine.Resources.LoadAll<ShootingGame.Shared.Ability.AbilityConfigSO>("Abilities");
            foreach (var so in sos)
            {
                if (so.AssetId <= 0 || so.AssetId > 255)
                {
                    UnityEngine.Debug.LogWarning($"[HeroRegistry] 跳过无效 AssetId={so.AssetId} 的技能: {so.name}");
                    continue;
                }
                if (_abilityTemplates.ContainsKey(so.AssetId))
                {
                    UnityEngine.Debug.LogWarning($"[HeroRegistry] 技能 AssetId={so.AssetId} 重复，跳过: {so.name}");
                    continue;
                }
                _abilityTemplates[so.AssetId] = so.ToAbilityConfig();
            }

            // 同步构建客户端 GunRegistry（从 Resources/Guns SO）
            var guns = new Dictionary<string, GunConfigData>();
            foreach (var gunSo in UnityEngine.Resources.LoadAll<GunConfig>("Guns"))
                guns[gunSo.name] = gunSo.ToGunConfigData(gunSo.name);
            GunRegistry.Initialize(guns);
        }

        /// <summary>
        /// 回退：没有 SO 资产时使用硬编码默认值。
        /// </summary>
        private static void InitFallbackHeroes()
        {
            GunConfig LoadGun(string name) => UnityEngine.Resources.Load<GunConfig>($"Guns/{name}");

            _heroes = new Dictionary<int, HeroConfig>
            {
                [1] = new HeroConfig
                {
                    HeroId = 1, Name = "突击兵", MaxHP = 100,
                    MoveSpeed = 6f, PlayerRadius = 0.35f, PlayerHeight = 1.8f,
                    StartingGun = LoadGun("Rifle_SemiAuto"),
                    Abilities = BuildAbilities(new int[] { 1, 2, 3, 11 }),
                },
                [2] = new HeroConfig
                {
                    HeroId = 2, Name = "重装兵", MaxHP = 200,
                    MoveSpeed = 4.5f, PlayerRadius = 0.50f, PlayerHeight = 2.0f,
                    StartingGun = LoadGun("Shotgun_Pump"),
                    Abilities = BuildAbilities(new int[] { 1, 2, 20, 21 }),
                },
                [3] = new HeroConfig
                {
                    HeroId = 3, Name = "狙击手", MaxHP = 75,
                    MoveSpeed = 5.5f, PlayerRadius = 0.30f, PlayerHeight = 1.7f,
                    StartingGun = LoadGun("Sniper_BoltAction"),
                    Abilities = BuildAbilities(new int[] { 1, 2, 30, 31 }),
                },
            };

            foreach (var kv in _heroes)
            {
                kv.Value.StartingGunId = kv.Value.StartingGun != null ? kv.Value.StartingGun.name : null;
                kv.Value.Gun = GunRegistry.GetGun(kv.Value.StartingGunId);
            }
        }
    }
}
