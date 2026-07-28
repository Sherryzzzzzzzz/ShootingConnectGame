using UnityEditor;
using UnityEngine;
using ShootingGame.Shared.Hero;

/// <summary>
/// 编辑器工具：一键创建全部英雄配置到 Resources/Heroes/。
/// 菜单: 游戏 → 配置 → 创建全部英雄配置
/// </summary>
public class HeroConfigCreator : EditorWindow
{
    [MenuItem("游戏/配置/创建全部英雄配置", false, 100)]
    public static void CreateAllHeroes()
    {
        EnsureFolder("Assets/Resources/Heroes");

        CreateHero(heroId: 1, heroName: "突击兵", maxHP: 100, moveSpeed: 6f, playerRadius: 0.35f, playerHeight: 1.8f,
            gunName: "Rifle_SemiAuto", abilityIds: new int[] { 1, 2, 3, 11 });

        CreateHero(heroId: 2, heroName: "重装兵", maxHP: 200, moveSpeed: 4.5f, playerRadius: 0.50f, playerHeight: 2.0f,
            gunName: "Shotgun_Pump", abilityIds: new int[] { 1, 2, 20, 21 });

        CreateHero(heroId: 3, heroName: "狙击手", maxHP: 75, moveSpeed: 5.5f, playerRadius: 0.30f, playerHeight: 1.7f,
            gunName: "Sniper_BoltAction", abilityIds: new int[] { 1, 2, 30, 31 });

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[HeroConfigCreator] 3 个英雄配置已创建/更新到 Resources/Heroes/");
    }

    private static void CreateHero(int heroId, string heroName, byte maxHP, float moveSpeed,
        float playerRadius, float playerHeight, string gunName, int[] abilityIds)
    {
        var path = $"Assets/Resources/Heroes/Hero_{heroId}_{heroName}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<HeroConfigSO>(path);

        HeroConfigSO hero;
        if (existing != null)
        {
            hero = existing;
            Debug.Log($"[HeroConfigCreator] 更新: {heroName} (HeroId={heroId})");
        }
        else
        {
            hero = ScriptableObject.CreateInstance<HeroConfigSO>();
            AssetDatabase.CreateAsset(hero, path);
            Debug.Log($"[HeroConfigCreator] 创建: {heroName} → {path}");
        }

        hero.HeroId = heroId;
        hero.HeroName = heroName;
        hero.MaxHP = maxHP;
        hero.MoveSpeed = moveSpeed;
        hero.PlayerRadius = playerRadius;
        hero.PlayerHeight = playerHeight;
        hero.StartingGun = LoadGunConfig(gunName);
        hero.AbilityAssetIds = abilityIds;

        EditorUtility.SetDirty(hero);
    }

    private static GunConfig LoadGunConfig(string gunName)
    {
        var gun = Resources.Load<GunConfig>($"Guns/{gunName}");
        if (gun == null)
            Debug.LogWarning($"[HeroConfigCreator] 枪械配置未找到: Resources/Guns/{gunName}");
        return gun;
    }

    private static void EnsureFolder(string folder)
    {
        if (!AssetDatabase.IsValidFolder(folder))
        {
            var parts = folder.Split('/');
            var parent = "Assets";
            for (int i = 1; i < parts.Length; i++)
            {
                var sub = $"{parent}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(sub))
                    AssetDatabase.CreateFolder(parent, parts[i]);
                parent = sub;
            }
        }
    }
}
