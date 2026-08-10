using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEditor;

/// <summary>
/// 后处理描边：把 OutlinePost 全屏 pass 安全地加到所有 URP Renderer。
/// 只添加新 feature（不删除/修改现有 feature），用 SerializedObject 操作避免破坏 asset。
/// 菜单: ShootingGame > 开启后处理描边 / 关闭后处理描边
/// </summary>
public static class OutlinePostSetup
{
    private const string FeatureName = "OutlinePost_PostFX";

    [MenuItem("ShootingGame/开启后处理描边", priority = 40)]
    public static void Enable()
    {
        var shader = Shader.Find("Hidden/OutlinePost");
        if (shader == null)
        {
            Debug.LogError("[Outline] 找不到 Hidden/OutlinePost shader");
            return;
        }

        // 创建/更新 Material
        EnsureFolder("Assets/Resources/Materials");
        string matPath = "Assets/Resources/Materials/OutlinePost.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, matPath);
        }
        mat.shader = shader;
        mat.SetFloat("_OutlineWidth", 1f);
        mat.SetFloat("_DepthThreshold", 0.05f);   // 线性深度差（米）
        mat.SetFloat("_NormalThreshold", 0.15f);  // 法线夹角差 1-dot
        mat.SetColor("_OutlineColor", Color.black);
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

        // 确保所有 URP asset 开启深度纹理（描边需要 _CameraDepthTexture）
        var pipelineGuids = AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset");
        foreach (var guid in pipelineGuids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
            if (pipeline == null) continue;
            var pso = new SerializedObject(pipeline);
            var depthProp = pso.FindProperty("m_RequireDepthTexture");
            if (depthProp != null && !depthProp.boolValue)
            {
                depthProp.boolValue = true;
                pso.ApplyModifiedProperties();
                EditorUtility.SetDirty(pipeline);
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[Outline] 后处理描边已开启（{count} 个 Renderer）");
    }

    [MenuItem("ShootingGame/关闭后处理描边", priority = 41)]
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
        Debug.Log("[Outline] 后处理描边已关闭");
    }

    private static bool AddFeatureToRenderer(UniversalRendererData renderer, Material mat)
    {
        var so = new SerializedObject(renderer);
        var list = so.FindProperty("m_RendererFeatures");
        if (list == null) return false;

        // 已存在则修复其配置（URP 17 迁移逻辑可能把 fetchColorBuffer 重置为 false）并跳过新增
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
        fso.FindProperty("injectionPoint").intValue = 1; // BeforeRenderingPostProcessing
        fso.FindProperty("fetchColorBuffer").boolValue = true;
        fso.FindProperty("requirements").intValue = (int)(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal); // 需要深度+法线
        fso.FindProperty("m_Version").intValue = 1; // AddFetchColorBufferCheckbox：防止反序列化迁移覆盖 fetchColorBuffer
        fso.ApplyModifiedProperties();

        AssetDatabase.AddObjectToAsset(newFeature, renderer);
        list.arraySize++;
        list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = newFeature;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(renderer);
        return true;
    }

    /// <summary>
    /// 修复已存在的 OutlinePost feature：确保 fetchColorBuffer=true（shader 依赖 _BlitTexture），
    /// 并写入 m_Version 防止 URP 迁移逻辑重置它。
    /// </summary>
    private static void FixFeature(FullScreenPassRendererFeature feature, Material mat)
    {
        var fso = new SerializedObject(feature);
        var fetchProp = fso.FindProperty("fetchColorBuffer");
        if (fetchProp != null && !fetchProp.boolValue)
            fetchProp.boolValue = true;
        var reqProp = fso.FindProperty("requirements");
        if (reqProp != null)
            reqProp.intValue = (int)(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
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
