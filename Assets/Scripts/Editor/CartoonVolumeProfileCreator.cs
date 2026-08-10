using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;

/// <summary>
/// 卡通/风格化后处理生成器。
/// 菜单: ShootingGame > 创建卡通后处理
/// 覆盖 Assets/DefaultVolumeProfile.asset，添加：
///   - ColorAdjustments 高饱和
///   - Vignette 柔和暗角
///   - Bloom 轻量辉光
///   - Tonemapping ACES
///   - FilmGrain 轻度胶片颗粒
///   - LiftGammaGain 微暖色调
/// </summary>
public static class CartoonVolumeProfileCreator
{
    [MenuItem("ShootingGame/创建卡通后处理", priority = 30)]
    public static void Create()
    {
        // 输出到 Resources，运行时可用 Resources.Load 加载
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");

        string path = "Assets/Resources/DefaultVolumeProfile.asset";
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
        }

        // 清空旧组件（Remove 需要 Type，不是组件实例）
        var oldTypes = new System.Collections.Generic.List<System.Type>();
        foreach (var comp in profile.components)
        {
            oldTypes.Add(comp.GetType());
            // 从 asset 移除旧组件子对象
            Object.DestroyImmediate(comp, true);
        }
        foreach (var t in oldTypes)
            profile.Remove(t);

        // ===== ColorAdjustments: 高饱和 + 提亮 + 微对比 =====
        var color = profile.Add<ColorAdjustments>();
        color.saturation.overrideState = true;
        color.saturation.value = 25f;          // +25% 饱和度（卡通感）
        color.contrast.overrideState = true;
        color.contrast.value = 8f;             // +8 对比
        color.postExposure.overrideState = true;
        color.postExposure.value = 0.1f;       // +0.1 EV 提亮
        AssetDatabase.AddObjectToAsset(color, profile);

        // ===== Vignette: 柔和暗角（聚焦视线） =====
        var vig = profile.Add<Vignette>();
        vig.intensity.overrideState = true;
        vig.intensity.value = 0.35f;
        vig.smoothness.overrideState = true;
        vig.smoothness.value = 0.4f;
        vig.rounded.overrideState = true;
        vig.rounded.value = true;
        AssetDatabase.AddObjectToAsset(vig, profile);

        // ===== Bloom: 轻量辉光（枪口火焰/特效发光） =====
        var bloom = profile.Add<Bloom>();
        bloom.threshold.overrideState = true;
        bloom.threshold.value = 0.85f;
        bloom.intensity.overrideState = true;
        bloom.intensity.value = 0.4f;
        bloom.scatter.overrideState = true;
        bloom.scatter.value = 0.5f;
        bloom.highQualityFiltering.overrideState = true;
        bloom.highQualityFiltering.value = true;
        AssetDatabase.AddObjectToAsset(bloom, profile);

        // ===== Tonemapping: ACES（更饱和的电影色调） =====
        var tonemap = profile.Add<Tonemapping>();
        tonemap.mode.overrideState = true;
        tonemap.mode.value = TonemappingMode.ACES;
        AssetDatabase.AddObjectToAsset(tonemap, profile);

        // ===== LiftGammaGain: 微暖色调（提亮暗部） =====
        var lgg = profile.Add<LiftGammaGain>();
        lgg.lift.overrideState = true;
        lgg.lift.value = new Vector4(0.01f, 0.005f, -0.005f, 0f);   // 暗部微暖
        lgg.gamma.overrideState = true;
        lgg.gamma.value = new Vector4(0.99f, 0.99f, 1.01f, 0f);     // 中间调
        lgg.gain.overrideState = true;
        lgg.gain.value = new Vector4(1.02f, 1.0f, 0.98f, 0f);       // 高光微暖
        AssetDatabase.AddObjectToAsset(lgg, profile);

        // ===== FilmGrain: 轻度胶片颗粒 =====
        var grain = profile.Add<FilmGrain>();
        grain.type.overrideState = true;
        grain.type.value = FilmGrainLookup.Medium1;
        grain.intensity.overrideState = true;
        grain.intensity.value = 0.08f;
        grain.response.overrideState = true;
        grain.response.value = 0.8f;
        AssetDatabase.AddObjectToAsset(grain, profile);

        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("<color=green>[CartoonPostFX] 已生成卡通风格后处理: 高饱和 + 暗角 + 辉光 + ACES + 暖调 + 颗粒</color>");
    }
}
