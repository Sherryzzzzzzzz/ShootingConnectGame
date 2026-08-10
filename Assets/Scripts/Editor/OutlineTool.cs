using UnityEngine;
using UnityEditor;

/// <summary>
/// 给角色加黑色描边：复制 SkinnedMeshRenderer（共享骨骼），用 OutlinePass shader 渲染背面外扩黑边。
/// 菜单: ShootingGame > 给选中角色加描边
/// 需要先选中场景中的角色（带 SkinnedMeshRenderer）。
/// </summary>
public static class OutlineTool
{
    [MenuItem("ShootingGame/给选中角色加描边", priority = 50)]
    public static void AddOutline()
    {
        var selected = Selection.activeGameObject;
        if (selected == null)
        {
            Debug.LogError("[Outline] 请先在 Hierarchy 选中角色");
            return;
        }

        var smr = selected.GetComponentInChildren<SkinnedMeshRenderer>();
        if (smr == null)
        {
            Debug.LogError("[Outline] 角色没有 SkinnedMeshRenderer");
            return;
        }

        // 已有描边层则跳过
        if (selected.transform.Find("Outline") != null)
        {
            Debug.Log("[Outline] 已有描边层");
            return;
        }

        // 创建描边层
        var outlineGo = new GameObject("Outline");
        outlineGo.transform.SetParent(smr.transform.parent, false);
        outlineGo.transform.localPosition = Vector3.zero;
        outlineGo.transform.localRotation = Quaternion.identity;
        outlineGo.transform.localScale = Vector3.one;

        var outlineSmr = outlineGo.AddComponent<SkinnedMeshRenderer>();
        outlineSmr.sharedMesh = smr.sharedMesh;
        outlineSmr.bones = smr.bones;
        outlineSmr.rootBone = smr.rootBone;

        // 描边材质
        var shader = Shader.Find("Custom/OutlinePass");
        if (shader == null)
        {
            Debug.LogError("[Outline] 找不到 Custom/OutlinePass shader");
            return;
        }
        var mat = new Material(shader);
        mat.SetColor("_OutlineColor", Color.black);
        mat.SetFloat("_OutlineWidth", 0.03f);
        outlineSmr.sharedMaterial = mat;

        Debug.Log($"[Outline] 已给 {selected.name} 加黑色描边（宽度 0.03，可调 _OutlineWidth）");
        EditorUtility.SetDirty(selected);
    }
}
