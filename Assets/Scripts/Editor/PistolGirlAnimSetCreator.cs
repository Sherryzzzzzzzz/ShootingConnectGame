using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using Animancer;

public class PistolGirlAnimSetCreator
{
    [MenuItem("游戏/角色/创建 PistolGirl AnimationSet", false, 402)]
    public static void Create()
    {
        string dstPath = "Assets/Resources/PistolGirl_AnimSet.asset";
        var existing = AssetDatabase.LoadAssetAtPath<PlayerAnimationSet>(dstPath);
        if (existing != null)
        {
            AssetDatabase.DeleteAsset(dstPath);
        }

        string basePath = "Assets/CombatGirlsCharacterPack/Pistol_Girl/Animations";

        // 完整映射：PlayerAnimType 枚举值 → FBX 路径
        var clipMap = new Dictionary<string, string>
        {
            {"Rifle_Idle",           $"{basePath}/Normal/Idle.fbx"},
            {"Rifle_WalkFwdLoop",    $"{basePath}/Normal/Walk.fbx"},
            {"Rifle_WalkBwdLoop",    $"{basePath}/Normal/Walk.fbx"},
            {"Rifle_WalkLtLoop",     $"{basePath}/Aiming/AimWalk_L.fbx"},
            {"Rifle_WalkRtLoop",     $"{basePath}/Aiming/AimWalk_R.fbx"},
            {"Rifle_RunFwdLoop",     $"{basePath}/Normal/Run.fbx"},
            {"Rifle_JumpUp",         $"{basePath}/Normal/Evade.fbx"},
            {"Rifle_FallingLoop",    $"{basePath}/Normal/Idle.fbx"},
            {"Rifle_TurnL90",        $"{basePath}/Aiming/AimTurn_L90.fbx"},
            {"Rifle_TurnR90",        $"{basePath}/Aiming/AimTurn_R90.fbx"},
            {"Rifle_Death",          $"{basePath}/Normal/Die1.fbx"},
            {"Rifle_Hit1",           $"{basePath}/Normal/Hit1.fbx"},
            {"Rifle_Hit2",           $"{basePath}/Normal/Hit2.fbx"},
            {"Rifle_Evade",          $"{basePath}/Normal/Evade.fbx"},
            {"Rifle_Stun",           $"{basePath}/Normal/Stun.fbx"},
            // 瞄准
            {"Rifle_AimIdle",        $"{basePath}/Aiming/AimIdle.fbx"},
            {"Rifle_AimWalkF",       $"{basePath}/Aiming/AimWalk_F.fbx"},
            {"Rifle_AimWalkL",       $"{basePath}/Aiming/AimWalk_L.fbx"},
            {"Rifle_AimWalkR",       $"{basePath}/Aiming/AimWalk_R.fbx"},
            {"Rifle_AimWalkB",       $"{basePath}/Aiming/AimWalk_B.fbx"},
            {"Rifle_AimJog",         $"{basePath}/Aiming/AimJog.fbx"},
            {"Rifle_Shoot",          $"{basePath}/Aiming/AimIdle_Shoot.fbx"},
            // 蹲伏
            {"Rifle_CrouchIdle",     $"{basePath}/Normal/Crouch_Idle.fbx"},
            {"Rifle_CrouchWalk",     $"{basePath}/Normal/Crouch_Walk.fbx"},
            {"Rifle_CrouchJog",      $"{basePath}/Normal/Crouch_Jog.fbx"},
            {"Rifle_CrouchAimIdle",  $"{basePath}/Aiming/Crouch_AimIdle.fbx"},
            {"Rifle_CrouchAimWalk",  $"{basePath}/Aiming/Crouch_AimWalk_F.fbx"},
            {"Rifle_CrouchShoot",    $"{basePath}/Aiming/Crouch_AimIdle_Shoot.fbx"},
            // 拔枪/收枪
            {"Rifle_DrawGun",        $"{basePath}/Normal/TakeGun.fbx"},
            {"Rifle_HolsterGun",     $"{basePath}/Normal/PutGun.fbx"},
        };

        var set = ScriptableObject.CreateInstance<PlayerAnimationSet>();
        foreach (var kv in clipMap)
        {
            var clip = LoadClip(kv.Value);
            if (clip == null) { Debug.LogWarning($"[AnimSet] 未找到: {kv.Key}"); continue; }
            set.animations.Add(new PlayerAnimationSet.AnimationEntry
            {
                name = kv.Key,
                clip = new Animancer.ClipTransition { Clip = clip }
            });
        }
        AssetDatabase.CreateAsset(set, dstPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.SetDirty(set);
        Debug.Log($"[AnimSet] ✅ {dstPath} ({clipMap.Count} clips)");
        EditorUtility.DisplayDialog("完成", $"PistolGirl_AnimSet 已创建 ({clipMap.Count} 个动画)", "OK");
    }

    private static AnimationClip LoadClip(string fbxPath)
    {
        foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
            if (obj is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                return clip;
        return null;
    }
}
