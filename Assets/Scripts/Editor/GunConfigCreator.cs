using UnityEditor;
using UnityEngine;
using ShootingGame.Shared.Hero;

/// <summary>
/// 编辑器工具：一键创建所有枪械配置到 Resources/Guns/。
/// </summary>
public class GunConfigCreator : EditorWindow
{
    [MenuItem("游戏/配置/创建全部枪械配置")]
    public static void CreateAllGuns()
    {
        EnsureFolder("Assets/Resources/Guns");

        CreateGun("Rifle_SemiAuto",     "半自动步枪", FireMode.Single, 0.15f, 25, 30, 2f,   200f, 0f,   BulletType.Hitscan);
        CreateGun("Pistols_Dual",       "双手枪",     FireMode.Auto,   0.08f, 15, 24, 1.5f,  150f, 2f,   BulletType.Hitscan);
        CreateGun("Sniper_BoltAction",  "狙击枪",     FireMode.Single, 1.5f,  100, 5,  3f,    300f, 0f,   BulletType.Hitscan);
        CreateGun("Shotgun_Pump",       "霰弹枪",     FireMode.Shotgun,0.8f,  8,   8,  3.5f,  80f,  5f,   BulletType.Hitscan);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[GunConfigCreator] 4 把枪械配置已创建到 Resources/Guns/");
    }

    private static void CreateGun(string assetName, string displayName, FireMode mode,
        float fireRate, byte damage, int clip, float reload, float range, float spread, BulletType bullet)
    {
        var path = $"Assets/Resources/Guns/{assetName}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<GunConfig>(path);
        if (existing != null)
        {
            existing.FireRate = fireRate;
            existing.Damage = damage;
            existing.ClipSize = clip;
            existing.ReloadTime = reload;
            existing.Range = range;
            existing.SpreadAngle = spread;
            EditorUtility.SetDirty(existing);
            Debug.Log($"[GunConfigCreator] 更新: {displayName}");
            return;
        }

        var gun = ScriptableObject.CreateInstance<GunConfig>();
        gun.GunName = displayName;
        gun.FireMode = mode;
        gun.FireRate = fireRate;
        gun.Damage = damage;
        gun.ClipSize = clip;
        gun.ReloadTime = reload;
        gun.Range = range;
        gun.SpreadAngle = spread;
        gun.Bullet = bullet;
        AssetDatabase.CreateAsset(gun, path);
        Debug.Log($"[GunConfigCreator] 创建: {displayName} → {path}");
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
