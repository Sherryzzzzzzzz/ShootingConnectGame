using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using ShootingGame.Shared.Ability;
using ShootingGame.Shared.Hero;

/// <summary>
/// 双端配置导出器：把 Unity SO 配置导出为 JSON 供服务器加载。
/// 编辑入口只有 SO（Assets 右键 Create → ShootingGame/...），
/// 导出后服务器与客户端数值天然一致，消灭硬编码副本。
///
/// 菜单: 游戏/配置/导出双端配置 (JSON)
/// </summary>
public static class GameConfigExporter
{
    private const string AbilitiesResourceDir = "Assets/Resources/Abilities";

    [MenuItem("游戏/配置/导出双端配置 (JSON)", false, 100)]
    public static void ExportAll()
    {
        EnsureDefaultAbilityAssets();

        string projectRoot = Path.GetDirectoryName(Application.dataPath);
        string serverDir = Path.Combine(projectRoot, "Server");

        int gunCount = ExportGuns(serverDir);
        int abilityCount = ExportAbilities(serverDir);
        int heroCount = ExportHeroes(serverDir);

        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("配置导出完成",
            $"已导出到 {serverDir}:\n\n" +
            $"枪械: {gunCount} (guns.json)\n" +
            $"技能: {abilityCount} (abilities.json)\n" +
            $"英雄: {heroCount} (heroes.json)\n\n" +
            $"服务器启动时用 --config-dir 指向该目录（默认当前目录）。",
            "OK");
    }

    // ================= 枪械 =================

    [System.Serializable]
    private class GunListDto { public List<GunDto> Guns; }
    [System.Serializable]
    private class GunDto
    {
        public string Id; public string GunName; public int FireMode; public int Bullet;
        public float FireRate; public byte Damage; public int ClipSize; public float ReloadTime;
        public float Range; public float SpreadAngle; public float RecoilKick; public float BulletSpeed;
        public float FalloffStart; public float FalloffEnd; public float FalloffMinMultiplier;
        public float MoveSpreadAdd; public float BloomPerShot; public float BloomMax; public float BloomRecover;
    }

    private static int ExportGuns(string serverDir)
    {
        var dto = new GunListDto { Guns = new List<GunDto>() };
        foreach (var gun in Resources.LoadAll<GunConfig>("Guns"))
        {
            dto.Guns.Add(new GunDto
            {
                Id = gun.name,
                GunName = gun.GunName,
                FireMode = (int)gun.FireMode,
                Bullet = (int)gun.Bullet,
                FireRate = gun.FireRate,
                Damage = gun.Damage,
                ClipSize = gun.ClipSize,
                ReloadTime = gun.ReloadTime,
                Range = gun.Range,
                SpreadAngle = gun.SpreadAngle,
                RecoilKick = gun.RecoilKick,
                BulletSpeed = gun.BulletSpeed,
                FalloffStart = gun.FalloffStart,
                FalloffEnd = gun.FalloffEnd,
                FalloffMinMultiplier = gun.FalloffMinMultiplier,
                MoveSpreadAdd = gun.MoveSpreadAdd,
                BloomPerShot = gun.BloomPerShot,
                BloomMax = gun.BloomMax,
                BloomRecover = gun.BloomRecover,
            });
        }
        WriteJson(serverDir, "guns.json", dto);
        return dto.Guns.Count;
    }

    // ================= 技能 =================

    [System.Serializable]
    private class AbilityListDto { public List<AbilityDto> Abilities; }
    [System.Serializable]
    private class AbilityDto
    {
        public int AssetId; public string Name; public float Cooldown; public float Duration;
        public long RequiredTags; public long BlockedByTags; public long CancelledByTags;
        public long AppliedTags; public long RemovedTags; public string BehaviorTypeName;
    }

    private static int ExportAbilities(string serverDir)
    {
        var dto = new AbilityListDto { Abilities = new List<AbilityDto>() };
        foreach (var ab in Resources.LoadAll<AbilityConfigSO>("Abilities"))
        {
            dto.Abilities.Add(new AbilityDto
            {
                AssetId = ab.AssetId,
                Name = ab.AbilityName,
                Cooldown = ab.Cooldown,
                Duration = ab.Duration,
                RequiredTags = ab.RequiredTags,
                BlockedByTags = ab.BlockedByTags,
                CancelledByTags = ab.CancelledByTags,
                AppliedTags = ab.AppliedTags,
                RemovedTags = ab.RemovedTags,
                BehaviorTypeName = ab.BehaviorTypeName,
            });
        }
        WriteJson(serverDir, "abilities.json", dto);
        return dto.Abilities.Count;
    }

    // ================= 英雄 =================

    [System.Serializable]
    private class HeroListDto { public List<HeroDto> Heroes; }
    [System.Serializable]
    private class HeroDto
    {
        public int HeroId; public string Name; public byte MaxHP; public float MoveSpeed;
        public float PlayerRadius; public float PlayerHeight; public string StartingGunId;
        public int[] AbilityAssetIds;
    }

    private static int ExportHeroes(string serverDir)
    {
        var dto = new HeroListDto { Heroes = new List<HeroDto>() };
        foreach (var hero in Resources.LoadAll<HeroConfigSO>("Heroes"))
        {
            dto.Heroes.Add(new HeroDto
            {
                HeroId = hero.HeroId,
                Name = hero.HeroName,
                MaxHP = hero.MaxHP,
                MoveSpeed = hero.MoveSpeed,
                PlayerRadius = hero.PlayerRadius,
                PlayerHeight = hero.PlayerHeight,
                StartingGunId = hero.StartingGun != null ? hero.StartingGun.name : null,
                AbilityAssetIds = hero.AbilityAssetIds,
            });
        }
        // 按 HeroId 排序，便于人工检查 diff
        dto.Heroes.Sort((a, b) => a.HeroId.CompareTo(b.HeroId));
        WriteJson(serverDir, "heroes.json", dto);
        return dto.Heroes.Count;
    }

    // ================= 默认技能资产 =================

    /// <summary>
    /// 若 Resources/Abilities/ 下没有任何技能资产，按内置模板创建一份默认的。
    /// </summary>
    [MenuItem("游戏/配置/创建默认技能配置", false, 101)]
    public static void EnsureDefaultAbilityAssets()
    {
        Directory.CreateDirectory(AbilitiesResourceDir);

        // (assetId, name, cooldown, duration, behavior)
        var defaults = new (int id, string name, float cd, float dur, string behavior)[]
        {
            (1,  "Fire",     0.15f, 0f,   "ShootingGame.Shared.Ability.Abilities.FireWeaponAbility"),
            (2,  "Reload",   0f,    2.0f, "ShootingGame.Shared.Ability.Abilities.ReloadWeaponAbility"),
            (3,  "Jump",     0f,    0f,   "ShootingGame.Shared.Ability.Abilities.JumpAbility"),
            (4,  "Sprint",   0f,    0f,   "ShootingGame.Shared.Ability.Abilities.SprintAbility"),
            (11, "Dash",     1.5f,  0f,   "ShootingGame.Shared.Ability.Abilities.DashAbility"),
            (20, "Shield",   8f,    3.0f, "ShootingGame.Shared.Ability.Abilities.ShieldAbility"),
            (21, "Charge",   6f,    0.3f, "ShootingGame.Shared.Ability.Abilities.ChargeAbility"),
            (30, "Stealth",  12f,   5.0f, "ShootingGame.Shared.Ability.Abilities.StealthAbility"),
            (31, "MarkShot", 10f,   0f,   "ShootingGame.Shared.Ability.Abilities.MarkShotAbility"),
        };

        int created = 0;
        foreach (var d in defaults)
        {
            string path = $"{AbilitiesResourceDir}/Ability_{d.id}_{d.name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<AbilityConfigSO>(path);
            if (existing == null)
            {
                // 同名不同路径的资产已存在（按 AssetId 查重）
                bool dup = false;
                foreach (var ab in Resources.LoadAll<AbilityConfigSO>("Abilities"))
                    if (ab.AssetId == d.id) { dup = true; break; }
                if (dup) continue;

                var so = ScriptableObject.CreateInstance<AbilityConfigSO>();
                so.AssetId = d.id;
                so.AbilityName = d.name;
                so.Cooldown = d.cd;
                so.Duration = d.dur;
                so.BehaviorTypeName = d.behavior;
                AssetDatabase.CreateAsset(so, path);
                created++;
            }
        }

        if (created > 0)
        {
            AssetDatabase.SaveAssets();
            Debug.Log($"[GameConfigExporter] 创建了 {created} 个默认技能资产 ({AbilitiesResourceDir})");
        }
    }

    // ================= 工具 =================

    private static void WriteJson(string dir, string fileName, object dto)
    {
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, fileName);
        File.WriteAllText(path, JsonUtility.ToJson(dto, prettyPrint: true));
        Debug.Log($"[GameConfigExporter] 写出 {path}");
    }
}
