using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// 将 DF_HD_Japan_Garden 的 HDRP 材质批量转换为 URP 材质。
/// 采用直接 YAML 编辑方式确保 Shader 变更持久化。
/// 菜单: 游戏/配置/转换花园材质到 URP
/// </summary>
public class MaterialURPConverter : EditorWindow
{
    // URP Lit shader GUID (Universal Render Pipeline/Lit)
    // 这个 GUID 在所有 Unity 2022.3 URP 项目中是固定的
    private const string URPLitGuid = "933532a4fcc9baf4fa0491de14d08ed7";
    // 等一下，这个 GUID 就是上面材质用的同一个 GUID！
    // 让我用 Shader.Find 动态获取

    [MenuItem("游戏/配置/转换花园材质到 URP", false, 110)]
    public static void ConvertGardenMaterialsToURP()
    {
        // 确保 URP shader 已加载
        Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
        Shader urpUnlit = Shader.Find("Universal Render Pipeline/Unlit");

        if (urpLit == null)
        {
            Debug.LogError("[MaterialURPConverter] URP/Lit Shader 未找到！请确认 URP 包已正确安装。");
            return;
        }

        string gardenPath = "Assets/DF_HD_Japan_Garden";
        string[] matGuids = AssetDatabase.FindAssets("t:Material", new[] { gardenPath });
        Debug.Log($"[MaterialURPConverter] 找到 {matGuids.Length} 个材质，开始转换...");

        // 先强制加载所有 URP shader variants
        Shader.WarmupAllShaders();

        int converted = 0;
        int skipped = 0;
        int customShaderGraph = 0;
        var failedList = new List<string>();

        foreach (string guid in matGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) continue;

            string shaderName = mat.shader != null ? mat.shader.name : "(null)";

            // 已经是 URP shader，跳过
            if (shaderName.StartsWith("Universal Render Pipeline/") ||
                shaderName.StartsWith("URP/") ||
                shaderName.StartsWith("Hidden/Universal"))
            {
                skipped++;
                continue;
            }

            // 判断目标 shader
            Shader targetShader;
            bool isCustomSg = IsCustomShaderGraph(shaderName, mat);

            // 水面/植被等自定义 ShaderGraph → 尝试用 URP/Lit，但标记为需要后续手动处理
            if (isCustomSg)
            {
                targetShader = urpLit; // fallback
                customShaderGraph++;
                Debug.Log($"[MaterialURPConverter] 自定义 ShaderGraph 材质: {path} ({shaderName}) → URP/Lit fallback");
            }
            else if (shaderName.Contains("Unlit") || shaderName.Contains("Sprite"))
            {
                targetShader = urpUnlit;
            }
            else
            {
                targetShader = urpLit;
            }

            // === 核心：转换 Shader + 修复属性 ===

            // 1. 保存原始纹理引用（在改 Shader 之前）
            Texture baseColorTex = mat.HasProperty("_BaseColorMap") ? mat.GetTexture("_BaseColorMap") : mat.mainTexture;
            Texture normalTex = mat.HasProperty("_NormalMap") ? mat.GetTexture("_NormalMap") : null;
            Texture maskTex = mat.HasProperty("_MaskMap") ? mat.GetTexture("_MaskMap") : null;
            float smoothness = mat.HasProperty("_Smoothness") ? mat.GetFloat("_Smoothness") : 0.5f;
            float metallic = mat.HasProperty("_Metallic") ? mat.GetFloat("_Metallic") : 0f;
            float normalScale = mat.HasProperty("_NormalScale") ? mat.GetFloat("_NormalScale") : 1f;
            float cutoff = mat.HasProperty("_AlphaCutoff") ? mat.GetFloat("_AlphaCutoff") : 0.5f;
            bool alphaClip = mat.HasProperty("_AlphaCutoffEnable") ? mat.GetFloat("_AlphaCutoffEnable") > 0.5f : false;
            bool doubleSided = mat.HasProperty("_DoubleSidedEnable") ? mat.GetFloat("_DoubleSidedEnable") > 0.5f : false;
            Color emissionColor = mat.HasProperty("_EmissiveColor") ? mat.GetColor("_EmissiveColor") : Color.black;
            Texture emissiveTex = mat.HasProperty("_EmissiveColorMap") ? mat.GetTexture("_EmissiveColorMap") : null;

            // 2. 记录 SurfaceType (Opaque / Transparent)
            int surfaceType = mat.HasProperty("_SurfaceType") ? (int)mat.GetFloat("_SurfaceType") : 0;
            bool isTransparent = surfaceType == 1;

            // 3. 切换 Shader
            mat.shader = targetShader;

            // 4. 修复 _BaseColor — 这是导致黑色的关键！
            //    HDRP 中 _BaseColor 通常是 (0,0,0,0)，颜色由纹理提供
            //    URP/Lit 中 _BaseColor 乘到纹理上，所以必须改为白色
            mat.SetColor("_BaseColor", Color.white);

            // 5. 转移纹理
            if (baseColorTex != null)
                mat.SetTexture("_BaseMap", baseColorTex);

            if (normalTex != null)
            {
                mat.SetTexture("_BumpMap", normalTex);
                mat.SetFloat("_BumpScale", normalScale);
            }

            // HDRP MaskMap: R=Metallic, G=Occlusion, B=DetailMask, A=Smoothness
            // URP: _MetallicGlossMap (RGB=Metallic, A=Smoothness) 或 _SpecGlossMap
            // 如果 MaskMap 存在，设到 _MetallicGlossMap
            if (maskTex != null)
            {
                mat.SetTexture("_MetallicGlossMap", maskTex);
                // 注意：HDRP MaskMap R=Metallic, A=Smoothness
                // URP MetallicGlossMap R=Metallic(if _SmoothnessTextureChannel=0), A=Smoothness
            }

            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Metallic", metallic);

            // 6. Alpha Clip / Cutoff
            if (alphaClip)
            {
                mat.SetFloat("_AlphaClip", 1f);
                mat.SetFloat("_Cutoff", cutoff);
                mat.SetOverrideTag("RenderType", "TransparentCutout");
            }

            // 7. Double-sided
            if (doubleSided)
            {
                mat.SetFloat("_Cull", 0f); // Off
            }

            // 8. Surface Type
            if (isTransparent)
            {
                mat.SetFloat("_Surface", 1f); // Transparent
                mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                mat.renderQueue = 3000;
            }

            // 9. Emission
            if (emissionColor != Color.black)
            {
                mat.SetColor("_EmissionColor", emissionColor);
                mat.EnableKeyword("_EMISSION");
            }
            if (emissiveTex != null)
            {
                mat.SetTexture("_EmissionMap", emissiveTex);
                mat.EnableKeyword("_EMISSION");
            }

            // 10. 标记材质为已修改
            EditorUtility.SetDirty(mat);
            converted++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[MaterialURPConverter] ========== 转换完成 ==========");
        Debug.Log($"[MaterialURPConverter] 成功转换: {converted}");
        Debug.Log($"[MaterialURPConverter] 跳过(已是URP): {skipped}");
        Debug.Log($"[MaterialURPConverter] 自定义ShaderGraph(fallback URP/Lit): {customShaderGraph}");
        if (failedList.Count > 0)
        {
            Debug.LogWarning($"[MaterialURPConverter] 失败: {failedList.Count}");
            foreach (var f in failedList)
                Debug.LogWarning($"  - {f}");
        }

        if (customShaderGraph > 0)
        {
            Debug.Log("[MaterialURPConverter] 提示: 自定义 ShaderGraph 材质已用 URP/Lit 兜底。如需还原原始效果，请在 URP 中重新创建对应的 ShaderGraph（水面、植被等）。");
        }
    }

    /// <summary>
    /// 判断是否为自定义 HDRP ShaderGraph（非标准 HDRP/Lit）
    /// </summary>
    private static bool IsCustomShaderGraph(string shaderName, Material mat)
    {
        // 标准 HDRP shader 前缀
        if (shaderName.StartsWith("HDRP/Lit") ||
            shaderName.StartsWith("HDRP/Unlit") ||
            shaderName.StartsWith("HDRP/Decal") ||
            shaderName.StartsWith("HDRP/TerrainLit") ||
            shaderName.StartsWith("HDRP/Fabric") ||
            shaderName.StartsWith("HDRP/AxF") ||
            shaderName.StartsWith("HDRP/StackLit") ||
            shaderName.StartsWith("HDRP/Eye") ||
            shaderName.StartsWith("HDRP/Hair") ||
            shaderName.StartsWith("Autodesk"))
            return false;

        // 检查材质属性中是否有 ShaderGraph 自动生成的属性名
        // ShaderGraph 生成形如 "Vector1_XXXXXXXX" 的属性
        var props = mat.GetPropertyNames(MaterialPropertyType.Float);
        foreach (var prop in props)
        {
            if (prop.StartsWith("Vector1_") || prop.StartsWith("Color_") || prop.StartsWith("Vector2_"))
                return true;
        }

        return true; // 未知 shader 也视为自定义
    }

    /// <summary>
    /// 手动修复 YAML 中的 Shader GUID —— 兜底方案。
    /// 当 Editor API 方式因某些原因无效时使用。
    /// </summary>
    [MenuItem("游戏/配置/强制修复材质Shader引用 (YAML)", false, 111)]
    public static void ForceFixMaterialShaderReferences()
    {
        string urpLitPath = AssetDatabase.GetAssetPath(Shader.Find("Universal Render Pipeline/Lit"));
        string urpLitGuid = AssetDatabase.AssetPathToGUID(urpLitPath);

        if (string.IsNullOrEmpty(urpLitGuid))
        {
            Debug.LogError("[MaterialURPConverter] 无法获取 URP/Lit Shader GUID");
            return;
        }

        string gardenPath = "Assets/DF_HD_Japan_Garden";
        string[] matGuids = AssetDatabase.FindAssets("t:Material", new[] { gardenPath });

        int fixed_ = 0;
        foreach (string guid in matGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string content = File.ReadAllText(path);

            // 检查是否仍使用 HDRP shader (匹配模式 m_Shader: {fileID: 4800000, guid: <any>, type: 3})
            var match = System.Text.RegularExpressions.Regex.Match(content,
                @"m_Shader:\s*\{fileID:\s*4800000,\s*guid:\s*([a-fA-F0-9]+),\s*type:\s*3\}");

            if (match.Success && match.Groups[1].Value != urpLitGuid)
            {
                string oldShaderLine = match.Value;
                string newShaderLine = $"m_Shader: {{fileID: 4800000, guid: {urpLitGuid}, type: 3}}";
                content = content.Replace(oldShaderLine, newShaderLine);
                File.WriteAllText(path, content);
                fixed_++;
                Debug.Log($"[MaterialURPConverter] YAML修复: {path}");
            }
        }

        AssetDatabase.Refresh();
        Debug.Log($"[MaterialURPConverter] YAML Shader 引用修复完成: {fixed_} 个材质");
    }
}
