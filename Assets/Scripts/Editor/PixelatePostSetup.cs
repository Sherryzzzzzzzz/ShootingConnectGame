using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEditor;

/// <summary>
/// 像素化后处理：把 PixelatePost 全屏 pass 安全地加到所有 URP Renderer。
/// 注入点 AfterRenderingPostProcessing（卡通后处理之后），让 Bloom 辉光也一并像素化。
/// 菜单: ShootingGame > 开启像素化后处理 / 关闭像素化后处理 / 设置像素化强度
/// </summary>
public static class PixelatePostSetup
{
    private const string FeatureName = "PixelatePost_PostFX";

    [MenuItem("ShootingGame/开启像素化后处理", priority = 42)]
    public static void Enable()
    {
        var shader = Shader.Find("Hidden/PixelatePost");
        if (shader == null)
        {
            Debug.LogError("[Pixelate] 找不到 Hidden/PixelatePost shader");
            return;
        }

        // 创建/更新 Material
        EnsureFolder("Assets/Resources/Materials");
        string matPath = "Assets/Resources/Materials/PixelatePost.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, matPath);
        }
        mat.shader = shader;
        mat.SetFloat("_PixelSize", 4f);
        mat.SetFloat("_PixelStrength", 1f);
        EditorUtility.SetDirty(mat);

        // 找所有 URP Renderer，添加 feature（只添加，不动现有）
        var guids = AssetDatabase.FindAssets("t:UniversalRendererData");
        int count = 0;
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
            if (renderer == null) continue;

            if (AddFeatureToRenderer(renderer, mat))
                count++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[Pixelate] 像素化后处理已开启（{count} 个 Renderer）");
    }

    [MenuItem("ShootingGame/关闭像素化后处理", priority = 43)]
    public static void Disable()
    {
        var guids = AssetDatabase.FindAssets("t:UniversalRendererData");
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
            if (renderer == null) continue;

            var so = new SerializedObject(renderer);
            var list = so.FindProperty("m_RendererFeatures");
            if (list == null) continue;

            for (int i = list.arraySize - 1; i >= 0; i--)
            {
                var elem = list.GetArrayElementAtIndex(i);
                var feature = elem.objectReferenceValue as FullScreenPassRendererFeature;
                if (feature != null && feature.name == FeatureName)
                {
                    list.DeleteArrayElementAtIndex(i);
                    Object.DestroyImmediate(feature, true);
                }
            }
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(renderer);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[Pixelate] 像素化后处理已关闭");
    }

    [MenuItem("ShootingGame/设置像素化强度(小/中/大)", priority = 44)]
    public static void SetStrengthCycle()
    {
        string matPath = "Assets/Resources/Materials/PixelatePost.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            Debug.LogError("[Pixelate] 请先开启像素化后处理");
            return;
        }
        float cur = mat.GetFloat("_PixelSize");
        float next = cur < 2f ? 2f : cur < 4f ? 4f : cur < 8f ? 8f : 1f;
        mat.SetFloat("_PixelSize", next);
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Pixelate] 像素大小: {next}px（越小越精细，越大越像素）");
    }

    private static bool AddFeatureToRenderer(UniversalRendererData renderer, Material mat)
    {
        var so = new SerializedObject(renderer);
        var list = so.FindProperty("m_RendererFeatures");
        if (list == null) return false;

        // 已存在则修复配置（URP 17 迁移逻辑可能把 fetchColorBuffer 重置）并跳过新增
        for (int i = 0; i < list.arraySize; i++)
        {
            var feature = list.GetArrayElementAtIndex(i).objectReferenceValue as FullScreenPassRendererFeature;
            if (feature != null && feature.name == FeatureName)
            {
                FixFeature(feature, mat);
                return false;
            }
        }

        // 添加新 feature
        var newFeature = ScriptableObject.CreateInstance<FullScreenPassRendererFeature>();
        newFeature.name = FeatureName;
        var fso = new SerializedObject(newFeature);
        fso.FindProperty("passMaterial").objectReferenceValue = mat;
        fso.FindProperty("injectionPoint").intValue = 2; // AfterRenderingPostProcessing
        fso.FindProperty("fetchColorBuffer").boolValue = true;
        fso.FindProperty("requirements").intValue = 0;   // 像素化不需要深度/法线
        fso.FindProperty("m_Version").intValue = 1;      // AddFetchColorBufferCheckbox：防止反序列化迁移覆盖 fetchColorBuffer
        fso.ApplyModifiedProperties();

        AssetDatabase.AddObjectToAsset(newFeature, renderer);
        list.arraySize++;
        list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = newFeature;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(renderer);
        return true;
    }

    /// <summary>修复已存在的 Pixelate feature：确保 fetchColorBuffer=true 且写入 m_Version。</summary>
    private static void FixFeature(FullScreenPassRendererFeature feature, Material mat)
    {
        var fso = new SerializedObject(feature);
        var fetchProp = fso.FindProperty("fetchColorBuffer");
        if (fetchProp != null && !fetchProp.boolValue)
            fetchProp.boolValue = true;
        var versionProp = fso.FindProperty("m_Version");
        if (versionProp != null && versionProp.intValue < 1)
            versionProp.intValue = 1;
        var matProp = fso.FindProperty("passMaterial");
        if (matProp != null)
            matProp.objectReferenceValue = mat;
        fso.ApplyModifiedProperties();
        EditorUtility.SetDirty(feature);
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        string folder = System.IO.Path.GetFileName(path);
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, folder);
    }
}
