using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

/// <summary>
/// 给角色所有 SkinnedMeshRenderer 批量加黑色描边：
/// 为每个目标渲染器创建一个同骨骼的"Outline"兄弟渲染器，使用 OutlinePass shader
/// （Cull Front 背面外扩）。多材质槽方案对单 submesh 网格无效，这里采用
/// 复制渲染器的正确方案（与 OutlineTool 单角色版一致）。
/// 菜单: ShootingGame > 批量给角色加描边
/// </summary>
public static class OutlineBulkTool
{
    private const string OutlineName = "Outline";

    [MenuItem("ShootingGame/批量给角色加描边", priority = 50)]
    public static void AddOutlineAll()
    {
        var shader = Shader.Find("Custom/OutlinePass");
        if (shader == null)
        {
            Debug.LogError("[Outline] 找不到 Custom/OutlinePass shader");
            return;
        }

        // 找到所有 Player/LocalPlayer/RemotePlayer 角色的 SkinnedMeshRenderer
        var allSmr = Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None);
        int count = 0;
        foreach (var smr in allSmr)
        {
            // 只处理玩家角色（Pistol_Girl / Humanoid_Bot 等）
            if (!IsPlayerMesh(smr)) continue;

            // 已有 Outline 兄弟/子节点则跳过（避免重复）
            if (FindOutline(smr) != null) continue;

            AddOutlineTo(smr, shader);
            count++;
        }

        Debug.Log($"[Outline] 已给 {count} 个角色部件加黑色描边（OutlinePass 宽度 0.02）");
        AssetDatabase.SaveAssets();
    }

    private static Transform FindOutline(SkinnedMeshRenderer smr)
    {
        var parent = smr.transform.parent;
        if (parent != null)
        {
            var sibling = parent.Find(OutlineName);
            if (sibling != null) return sibling;
        }
        return smr.transform.Find(OutlineName);
    }

    private static void AddOutlineTo(SkinnedMeshRenderer src, Shader shader)
    {
        var outlineGo = new GameObject(OutlineName);
        outlineGo.transform.SetParent(src.transform.parent, false);
        // 与源渲染器完全相同的本地变换
        outlineGo.transform.localPosition = src.transform.localPosition;
        outlineGo.transform.localRotation = src.transform.localRotation;
        outlineGo.transform.localScale = src.transform.localScale;

        var outlineSmr = outlineGo.AddComponent<SkinnedMeshRenderer>();
        outlineSmr.sharedMesh = src.sharedMesh;
        outlineSmr.bones = src.bones;
        outlineSmr.rootBone = src.rootBone;
        outlineSmr.updateWhenOffscreen = src.updateWhenOffscreen;

        var mat = new Material(shader);
        mat.SetColor("_OutlineColor", Color.black);
        mat.SetFloat("_OutlineWidth", 0.02f);
        outlineSmr.sharedMaterial = mat;

        // 描边层不投射阴影（shader 无 ShadowCaster pass，显式关闭更明确）
        outlineSmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        outlineSmr.receiveShadows = false;
    }

    private static bool IsPlayerMesh(SkinnedMeshRenderer smr)
    {
        var name = smr.name.ToLower();
        if (name.Contains("pistol") || name.Contains("girl") || name.Contains("bot")
            || name.Contains("body") || name.Contains("face") || name.Contains("arm"))
            return true;
        // 父物体含 player
        var parent = smr.transform.parent;
        while (parent != null)
        {
            var pn = parent.name.ToLower();
            if (pn.Contains("player") || pn.Contains("pistol") || pn.Contains("hero")) return true;
            parent = parent.parent;
        }
        return false;
    }
}
