using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;

/// <summary>
/// 一键修复 DF_HD_Japan_Garden 场景从 HDRP 迁移到 URP 的所有问题。
/// 菜单: 游戏/配置/完整修复花园场景 (URP)
/// </summary>
public class GardenURPFixer : EditorWindow
{
    [MenuItem("游戏/配置/完整修复花园场景 (URP)", false, 100)]
    public static void FixAll()
    {
        Debug.Log("========== [GardenURPFixer] 开始完整修复 ==========");

        FixMaterials();
        FixSceneLighting();
        FixURPSettings();

        AssetDatabase.SaveAssets();
        Debug.Log("========== [GardenURPFixer] 修复完成！ ==========");
        Debug.Log("如果仍有问题，请检查: 1) Window→Rendering→Lighting→Environment Lighting  2) URP Asset 中的 Main Light 设置");
    }

    // ===== 1. 材质修复 =====
    private static void FixMaterials()
    {
        Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
        if (urpLit == null) { Debug.LogError("URP/Lit not found!"); return; }

        string[] guids = AssetDatabase.FindAssets("t:Material", new[] { "Assets/DF_HD_Japan_Garden" });
        int fixed_ = 0;

        foreach (string g in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(g);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null || mat.shader.name.StartsWith("Universal Render Pipeline/")) continue;

            // Read HDRP properties BEFORE shader change
            Texture baseTex = mat.HasProperty("_BaseColorMap") ? mat.GetTexture("_BaseColorMap") : mat.mainTexture;
            Texture normalTex = mat.HasProperty("_NormalMap") ? mat.GetTexture("_NormalMap") : null;
            Texture maskTex = mat.HasProperty("_MaskMap") ? mat.GetTexture("_MaskMap") : null;
            Texture emissiveTex = mat.HasProperty("_EmissiveColorMap") ? mat.GetTexture("_EmissiveColorMap") : null;

            float smoothness = 0.5f;
            if (mat.HasProperty("_SmoothnessRemapMin"))
                smoothness = mat.GetFloat("_SmoothnessRemapMin") + 0.1f;
            else if (mat.HasProperty("_Smoothness"))
                smoothness = mat.GetFloat("_Smoothness");

            float metallic = mat.HasProperty("_Metallic") ? mat.GetFloat("_Metallic") : 0f;
            float normalScale = mat.HasProperty("_NormalScale") ? mat.GetFloat("_NormalScale") : 1f;
            bool alphaClip = mat.HasProperty("_AlphaCutoffEnable") && mat.GetFloat("_AlphaCutoffEnable") > 0.5f;
            float cutoff = mat.HasProperty("_AlphaCutoff") ? mat.GetFloat("_AlphaCutoff") : 0.5f;
            bool doubleSided = mat.HasProperty("_DoubleSidedEnable") && mat.GetFloat("_DoubleSidedEnable") > 0.5f;
            int surfaceType = mat.HasProperty("_SurfaceType") ? (int)mat.GetFloat("_SurfaceType") : 0;
            Color emission = Color.black;
            if (mat.HasProperty("_EmissiveColor")) emission = mat.GetColor("_EmissiveColor");
            if (mat.HasProperty("_EmissiveIntensity") && mat.GetFloat("_EmissiveIntensity") > 0)
                emission *= mat.GetFloat("_EmissiveIntensity");

            // SWITCH SHADER
            mat.shader = urpLit;

            // FIX PROPERTIES
            mat.SetColor("_BaseColor", Color.white);
            if (baseTex != null) mat.SetTexture("_BaseMap", baseTex);
            if (normalTex != null)
            {
                mat.SetTexture("_BumpMap", normalTex);
                mat.SetFloat("_BumpScale", normalScale);
            }
            if (maskTex != null)
            {
                mat.SetTexture("_MetallicGlossMap", maskTex);
                mat.SetFloat("_SmoothnessTextureChannel", 0f); // R=Metallic, A=Smoothness (HDRP MaskMap format)
            }
            mat.SetFloat("_Smoothness", Mathf.Clamp(smoothness, 0f, 1f));
            mat.SetFloat("_Metallic", Mathf.Clamp(metallic, 0f, 1f));
            mat.SetFloat("_WorkflowMode", 1f); // Metallic workflow

            // Alpha
            if (alphaClip)
            {
                mat.SetFloat("_AlphaClip", 1f);
                mat.SetFloat("_Cutoff", cutoff);
                mat.renderQueue = 2450; // AlphaTest
            }

            // Double-sided
            if (doubleSided)
                mat.SetFloat("_Cull", 0f);

            // Surface type
            if (surfaceType == 1) // Transparent
            {
                mat.SetFloat("_Surface", 1f);
                mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_ZWrite", 0f);
                mat.renderQueue = 3000;
            }

            // Emission
            if (emission.maxColorComponent > 0.01f)
            {
                mat.SetColor("_EmissionColor", emission);
                mat.EnableKeyword("_EMISSION");
                if (emissiveTex != null) mat.SetTexture("_EmissionMap", emissiveTex);
            }

            // === REMOVE HDRP-specific texture references to avoid conflicts ===
            ClearHDRPProperty(mat, "_AnisotropyMap");
            ClearHDRPProperty(mat, "_CoatMaskMap");
            ClearHDRPProperty(mat, "_SpecGlossMap");
            ClearHDRPProperty(mat, "_DetailMap");
            ClearHDRPProperty(mat, "_ParallaxMap");
            ClearHDRPProperty(mat, "_SubsurfaceMaskMap");
            ClearHDRPProperty(mat, "_ThicknessMap");
            ClearHDRPProperty(mat, "_IridescenceMaskMap");
            ClearHDRPProperty(mat, "_SpecularColorMap");
            ClearHDRPProperty(mat, "_TransmittanceColorMap");
            ClearHDRPProperty(mat, "_BentNormalMap");
            ClearHDRPProperty(mat, "_TangentMap");

            EditorUtility.SetDirty(mat);
            fixed_++;
        }

        Debug.Log($"[GardenURPFixer] 材质修复: {fixed_} 个");
    }

    private static void ClearHDRPProperty(Material mat, string prop)
    {
        if (mat.HasProperty(prop))
            mat.SetTexture(prop, null);
    }

    // ===== 2. 场景光照修复 =====
    private static void FixSceneLighting()
    {
        // --- Directional Light (Sun) ---
        var sunLights = Object.FindObjectsOfType<Light>(true);
        foreach (var light in sunLights)
        {
            if (light.type == LightType.Directional)
            {
                // HDRP uses physical units (lux). URP uses arbitrary multiplier.
                // Convert: typical HDRP sun = 130000 lux → URP intensity ~1.3
                if (light.intensity > 100)
                {
                    light.intensity = Mathf.Clamp(light.intensity / 100000f, 0.5f, 3f);
                    Debug.Log($"[GardenURPFixer] 调整 Directional Light '{light.name}' 强度: {light.intensity:F2}");
                }
                // Ensure shadows
                if (light.shadows == LightShadows.None)
                    light.shadows = LightShadows.Soft;
            }
            else if (light.type == LightType.Point || light.type == LightType.Spot)
            {
                // HDRP point lights in lumens → URP arbitrary
                if (light.intensity > 50)
                {
                    light.intensity = Mathf.Clamp(light.intensity / 500f, 0.5f, 10f);
                    Debug.Log($"[GardenURPFixer] 调整 {light.type} '{light.name}' 强度: {light.intensity:F2}");
                }
            }
        }

        // --- Environment Lighting ---
        RenderSettings.ambientMode = AmbientMode.Skybox;
        RenderSettings.ambientIntensity = 1.0f;
        // If skybox material is HDRP-specific, switch to default
        if (RenderSettings.skybox != null && RenderSettings.skybox.shader.name.Contains("HDRP"))
        {
            RenderSettings.skybox = null; // Use URP default procedural sky
            Debug.Log("[GardenURPFixer] 移除 HDRP Skybox，使用默认天空");
        }

        // --- Fog ---
        RenderSettings.fog = true;
        // HDRP fog is often extreme, tone it down
        if (RenderSettings.fogDensity > 0.01f)
            RenderSettings.fogDensity = 0.003f;

        Debug.Log("[GardenURPFixer] 场景光照已修复");
    }

    // ===== 3. URP 设置 =====
    private static void FixURPSettings()
    {
        var urpAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (urpAsset == null)
        {
            Debug.LogWarning("[GardenURPFixer] 未找到 URP Asset！请在 Project Settings → Graphics 中设置。");
            return;
        }

        // Enable main light shadows
        var serializedUrp = new SerializedObject(urpAsset);

        var mainLightShadows = serializedUrp.FindProperty("m_MainLightShadowsSupported");
        if (mainLightShadows != null && !mainLightShadows.boolValue)
        {
            mainLightShadows.boolValue = true;
            Debug.Log("[GardenURPFixer] 开启主光源阴影");
        }

        var additionalLights = serializedUrp.FindProperty("m_AdditionalLightsSupported");
        if (additionalLights != null && !additionalLights.boolValue)
        {
            additionalLights.boolValue = true;
            Debug.Log("[GardenURPFixer] 开启额外光源支持");
        }

        var hdr = serializedUrp.FindProperty("m_SupportsHDR");
        if (hdr != null && !hdr.boolValue)
        {
            hdr.boolValue = true;
            Debug.Log("[GardenURPFixer] 开启 HDR 渲染");
        }

        var depthTexture = serializedUrp.FindProperty("m_SupportsDepthTexture");
        if (depthTexture != null && !depthTexture.boolValue)
        {
            depthTexture.boolValue = true;
            Debug.Log("[GardenURPFixer] 开启 Depth Texture");
        }

        serializedUrp.ApplyModifiedProperties();
        Debug.Log("[GardenURPFixer] URP 设置已优化");
    }
}
