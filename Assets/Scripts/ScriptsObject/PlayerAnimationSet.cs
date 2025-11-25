using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Animancer;


[CreateAssetMenu(fileName = "PlayerAnimationSet", menuName = "Configs/PlayerAnimationSet")]
public class PlayerAnimationSet : ScriptableObject
{
    [System.Serializable]
    public class AnimationEntry
    {
        public string name;
        public ClipTransition clip; // ✅ 改成 Animancer 的类型
    }

    public List<AnimationEntry> animations = new List<AnimationEntry>();

    public ClipTransition GetClip(PlayerAnimType type)
    {
        var entry = animations.FirstOrDefault(a => a.name == type.ToString());
        return entry?.clip;
    }
#if UNITY_EDITOR
    // 只在编辑器环境编译
    private void OnValidate()
    {
        // 为了防止编辑器类编译顺序问题，这里用反射调用
        var editorType = System.Type.GetType("PlayerAnimationSetEditor");
        if (editorType != null)
        {
            var method = editorType.GetMethod("GenerateEnumFromAnimations",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (method != null)
                method.Invoke(null, new object[] { this });
        }
    }
#endif
}