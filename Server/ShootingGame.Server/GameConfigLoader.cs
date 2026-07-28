using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using ShootingGame.Shared.Ability;
using ShootingGame.Shared.Hero;

namespace ShootingGame.Server
{
    /// <summary>
    /// 从 JSON 文件加载英雄/枪械/技能配置（由 Unity 编辑器 GameConfigExporter 导出）。
    /// 这是服务器侧的唯一配置入口，保证服务器数值与 Unity SO 编辑结果一致。
    /// </summary>
    public static class GameConfigLoader
    {
        // ---------- JSON DTO ----------

        public class GunListDto { public List<GunDto> Guns { get; set; } = new List<GunDto>(); }
        public class GunDto
        {
            public string Id { get; set; }
            public string GunName { get; set; }
            public int FireMode { get; set; }
            public int Bullet { get; set; }
            public float FireRate { get; set; }
            public byte Damage { get; set; }
            public int ClipSize { get; set; }
            public float ReloadTime { get; set; }
            public float Range { get; set; }
            public float SpreadAngle { get; set; }
            public float RecoilKick { get; set; }
            public float BulletSpeed { get; set; }
            public float FalloffStart { get; set; }
            public float FalloffEnd { get; set; }
            public float FalloffMinMultiplier { get; set; }
            public float MoveSpreadAdd { get; set; }
            public float BloomPerShot { get; set; }
            public float BloomMax { get; set; }
            public float BloomRecover { get; set; }
        }

        public class AbilityListDto { public List<AbilityDto> Abilities { get; set; } = new List<AbilityDto>(); }
        public class AbilityDto
        {
            public int AssetId { get; set; }
            public string Name { get; set; }
            public float Cooldown { get; set; }
            public float Duration { get; set; }
            public long RequiredTags { get; set; }
            public long BlockedByTags { get; set; }
            public long CancelledByTags { get; set; }
            public long AppliedTags { get; set; }
            public long RemovedTags { get; set; }
            public string BehaviorTypeName { get; set; }
        }

        public class HeroListDto { public List<HeroDto> Heroes { get; set; } = new List<HeroDto>(); }
        public class HeroDto
        {
            public int HeroId { get; set; }
            public string Name { get; set; }
            public byte MaxHP { get; set; }
            public float MoveSpeed { get; set; }
            public float PlayerRadius { get; set; }
            public float PlayerHeight { get; set; }
            public string StartingGunId { get; set; }
            public int[] AbilityAssetIds { get; set; }
        }

        // ---------- 加载 ----------

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        /// <summary>
        /// 从配置目录加载全部配置并初始化 GunRegistry / 返回英雄表。
        /// 任何文件缺失或解析失败都回退到该类的安全默认（不抛异常）。
        /// </summary>
        /// <returns>英雄配置表；为空表示应使用 HeroRegistry 硬编码回退</returns>
        public static Dictionary<int, HeroConfig> LoadAll(string configDir)
        {
            var guns = LoadGuns(configDir);
            GunRegistry.Initialize(guns);

            var abilityTemplates = LoadAbilities(configDir);
            return LoadHeroes(configDir, abilityTemplates);
        }

        private static Dictionary<string, GunConfigData> LoadGuns(string configDir)
        {
            var result = new Dictionary<string, GunConfigData>();
            var dto = LoadDto<GunListDto>(configDir, "guns.json");
            if (dto == null) return result;

            foreach (var g in dto.Guns)
            {
                if (string.IsNullOrEmpty(g.Id)) continue;
                result[g.Id] = new GunConfigData
                {
                    Id = g.Id,
                    GunName = g.GunName ?? g.Id,
                    FireMode = (FireMode)g.FireMode,
                    Bullet = (BulletType)g.Bullet,
                    FireRate = g.FireRate > 0 ? g.FireRate : 0.15f,
                    Damage = g.Damage,
                    ClipSize = g.ClipSize > 0 ? g.ClipSize : 30,
                    ReloadTime = g.ReloadTime > 0 ? g.ReloadTime : 2.0f,
                    Range = g.Range > 0 ? g.Range : 200f,
                    SpreadAngle = g.SpreadAngle,
                    RecoilKick = g.RecoilKick,
                    BulletSpeed = g.BulletSpeed > 0 ? g.BulletSpeed : 100f,
                    FalloffStart = g.FalloffStart > 0 ? g.FalloffStart : 1e9f,
                    FalloffEnd = g.FalloffEnd > 0 ? g.FalloffEnd : 1e9f,
                    FalloffMinMultiplier = g.FalloffMinMultiplier > 0 ? g.FalloffMinMultiplier : 1f,
                    MoveSpreadAdd = g.MoveSpreadAdd,
                    BloomPerShot = g.BloomPerShot,
                    BloomMax = g.BloomMax,
                    BloomRecover = g.BloomRecover,
                };
            }
            Console.WriteLine($"[GameConfig] 加载枪械 {result.Count} 把 (guns.json)");
            return result;
        }

        private static Dictionary<int, AbilityConfig> LoadAbilities(string configDir)
        {
            var result = new Dictionary<int, AbilityConfig>();
            var dto = LoadDto<AbilityListDto>(configDir, "abilities.json");
            if (dto == null) return result;

            foreach (var a in dto.Abilities)
            {
                if (a.AssetId <= 0 || a.AssetId > 255) continue;
                result[a.AssetId] = new AbilityConfig
                {
                    AssetId = (byte)a.AssetId,
                    Name = a.Name ?? $"Ability_{a.AssetId}",
                    Cooldown = a.Cooldown,
                    Duration = a.Duration,
                    RequiredTags = a.RequiredTags,
                    BlockedByTags = a.BlockedByTags,
                    CancelledByTags = a.CancelledByTags,
                    AppliedTags = a.AppliedTags,
                    RemovedTags = a.RemovedTags,
                    BehaviorTypeName = a.BehaviorTypeName,
                };
            }
            Console.WriteLine($"[GameConfig] 加载技能模板 {result.Count} 个 (abilities.json)");
            return result;
        }

        private static Dictionary<int, HeroConfig> LoadHeroes(string configDir, Dictionary<int, AbilityConfig> abilityTemplates)
        {
            var result = new Dictionary<int, HeroConfig>();
            var dto = LoadDto<HeroListDto>(configDir, "heroes.json");
            if (dto == null) return result;

            foreach (var h in dto.Heroes)
            {
                if (h.HeroId <= 0) continue;

                var abilities = new List<AbilityConfig>();
                if (h.AbilityAssetIds != null)
                {
                    foreach (int id in h.AbilityAssetIds)
                    {
                        if (abilityTemplates.TryGetValue(id, out var tpl))
                            abilities.Add(tpl);
                        else
                            Console.WriteLine($"[GameConfig] 警告: 英雄 {h.HeroId} 引用未知技能 AssetId={id}");
                    }
                }

                result[h.HeroId] = new HeroConfig
                {
                    HeroId = h.HeroId,
                    Name = h.Name ?? $"Hero_{h.HeroId}",
                    MaxHP = h.MaxHP > 0 ? h.MaxHP : (byte)100,
                    MoveSpeed = h.MoveSpeed > 0 ? h.MoveSpeed : 6f,
                    PlayerRadius = h.PlayerRadius > 0 ? h.PlayerRadius : 0.35f,
                    PlayerHeight = h.PlayerHeight > 0 ? h.PlayerHeight : 1.8f,
                    StartingGunId = h.StartingGunId,
                    Gun = GunRegistry.GetGun(h.StartingGunId),
                    Abilities = abilities.ToArray(),
                };
            }
            Console.WriteLine($"[GameConfig] 加载英雄 {result.Count} 个 (heroes.json)");
            return result;
        }

        private static T LoadDto<T>(string configDir, string fileName) where T : class
        {
            string path = Path.Combine(configDir, fileName);
            if (!File.Exists(path))
            {
                Console.WriteLine($"[GameConfig] {fileName} 不存在 ({configDir})，使用内置默认");
                return null;
            }
            try
            {
                string json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<T>(json, JsonOptions);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GameConfig] 解析 {fileName} 失败: {ex.Message}，使用内置默认");
                return null;
            }
        }
    }
}
