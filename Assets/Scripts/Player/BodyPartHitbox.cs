using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 身体部位碰撞体管理器。基于 Animator 的 Humanoid 骨骼或名称匹配，
/// 在头部、躯干、手臂、腿部创建碰撞体，用于命中判定。
/// </summary>
public class BodyPartHitbox : MonoBehaviour
{
    [Header("碰撞体设置")]
    [SerializeField] private float headRadius = 0.12f;
    [SerializeField] private float chestRadius = 0.25f;
    [SerializeField] private float abdomenRadius = 0.22f;
    [SerializeField] private float armRadius = 0.08f;
    [SerializeField] private float legRadius = 0.12f;
    [SerializeField] private float chestHeight = 0.3f;
    [SerializeField] private float abdomenHeight = 0.25f;

    [Header("手动骨骼引用（适用于非 Humanoid Avatar）")]
    [SerializeField] private Transform headBone;
    [SerializeField] private Transform spineBone;
    [SerializeField] private Transform hipsBone;
    [SerializeField] private Transform leftUpperArmBone;
    [SerializeField] private Transform rightUpperArmBone;
    [SerializeField] private Transform leftUpperLegBone;
    [SerializeField] private Transform rightUpperLegBone;

    [Header("调试")]
    [SerializeField] private bool showHitboxes = true;

    // 身体部位定义
    private readonly Dictionary<BodyPartType, HitboxInfo> _hitboxes = new Dictionary<BodyPartType, HitboxInfo>();
    private Animator _animator;

    public IReadOnlyDictionary<BodyPartType, HitboxInfo> Hitboxes => _hitboxes;

    public enum BodyPartType
    {
        Head,
        Chest,
        Abdomen,
        LeftArm,
        RightArm,
        LeftLeg,
        RightLeg
    }

    [Serializable]
    public class HitboxInfo
    {
        public BodyPartType PartType;
        public Collider Collider;
        public float DamageMultiplier;
        public Transform BoneTransform;
    }

    // 伤害倍率
    public static readonly Dictionary<BodyPartType, float> DamageMultipliers = new Dictionary<BodyPartType, float>
    {
        { BodyPartType.Head, 2.0f },
        { BodyPartType.Chest, 1.0f },
        { BodyPartType.Abdomen, 0.75f },
        { BodyPartType.LeftArm, 0.5f },
        { BodyPartType.RightArm, 0.5f },
        { BodyPartType.LeftLeg, 0.5f },
        { BodyPartType.RightLeg, 0.5f },
    };

    // 用于名称匹配的常见骨骼名（非 Humanoid Avatar 时使用）
    private static readonly Dictionary<BodyPartType, string[]> BoneNamePatterns = new Dictionary<BodyPartType, string[]>
    {
        { BodyPartType.Head,     new[] { "Head", "head", "Bip001 Head", "mixamorig:Head" } },
        { BodyPartType.Chest,    new[] { "Spine", "spine", "Spine1", "Spine2", "Bip001 Spine", "Bip001 Spine1", "Bip001 Spine2", "mixamorig:Spine", "mixamorig:Spine1", "mixamorig:Spine2" } },
        { BodyPartType.Abdomen,  new[] { "Hips", "hips", "Bip001 Pelvis", "mixamorig:Hips" } },
        { BodyPartType.LeftArm,  new[] { "LeftUpperArm", "leftupperarm", "Bip001 L UpperArm", "mixamorig:LeftUpperArm" } },
        { BodyPartType.RightArm, new[] { "RightUpperArm", "rightupperarm", "Bip001 R UpperArm", "mixamorig:RightUpperArm" } },
        { BodyPartType.LeftLeg,  new[] { "LeftUpperLeg", "leftupperleg", "Bip001 L Thigh", "mixamorig:LeftUpperLeg" } },
        { BodyPartType.RightLeg, new[] { "RightUpperLeg", "rightupperleg", "Bip001 R Thigh", "mixamorig:RightUpperLeg" } },
    };

    private void Awake()
    {
        _animator = GetComponent<Animator>();
        if (_animator == null)
            _animator = GetComponentInChildren<Animator>();

        if (_animator == null)
        {
            Debug.LogWarning("[BodyPartHitbox] 未找到 Animator 组件，跳过碰撞体创建");
            return;
        }

        CreateHitboxes();
    }

    private void CreateHitboxes()
    {
        CreateSingleHitbox(BodyPartType.Head,     HumanBodyBones.Head,           headBone,     headRadius,    0.08f);
        CreateSingleHitbox(BodyPartType.Chest,    HumanBodyBones.Spine,          spineBone,    chestRadius,   chestHeight);
        CreateSingleHitbox(BodyPartType.Abdomen,  HumanBodyBones.Hips,           hipsBone,     abdomenRadius, abdomenHeight);
        CreateSingleHitbox(BodyPartType.LeftArm,  HumanBodyBones.LeftUpperArm,   leftUpperArmBone,  armRadius, 0.15f);
        CreateSingleHitbox(BodyPartType.RightArm, HumanBodyBones.RightUpperArm,  rightUpperArmBone, armRadius, 0.15f);
        CreateSingleHitbox(BodyPartType.LeftLeg,  HumanBodyBones.LeftUpperLeg,   leftUpperLegBone,  legRadius, 0.2f);
        CreateSingleHitbox(BodyPartType.RightLeg, HumanBodyBones.RightUpperLeg,  rightUpperLegBone, legRadius, 0.2f);

        Debug.Log($"[BodyPartHitbox] 创建了 {_hitboxes.Count}/7 个身体部位碰撞体 (Humanoid={_animator.isHuman})");
    }

    private void CreateSingleHitbox(BodyPartType partType, HumanBodyBones bone, Transform manualBone, float radius, float height)
    {
        Transform boneTransform = null;

        // 1. 优先使用手动指定的骨骼
        if (manualBone != null)
        {
            boneTransform = manualBone;
        }
        // 2. Humanoid Avatar：通过 GetBoneTransform 获取
        else if (_animator.isHuman)
        {
            boneTransform = _animator.GetBoneTransform(bone);
        }
        // 3. 非 Humanoid Avatar：通过名称模式匹配查找
        else
        {
            boneTransform = FindBoneByName(partType);
        }

        if (boneTransform == null)
        {
            Debug.LogWarning($"[BodyPartHitbox] 骨骼未找到: {partType} (bone={bone}, isHuman={_animator.isHuman})，跳过");
            return;
        }

        var go = new GameObject($"Hitbox_{partType}");
        go.transform.SetParent(boneTransform);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;

        var capsule = go.AddComponent<CapsuleCollider>();
        capsule.radius = radius;
        capsule.height = height;
        capsule.isTrigger = true;

        var info = new HitboxInfo
        {
            PartType = partType,
            Collider = capsule,
            DamageMultiplier = DamageMultipliers.GetValueOrDefault(partType, 1f),
            BoneTransform = boneTransform
        };
        _hitboxes[partType] = info;

#if UNITY_EDITOR
        if (showHitboxes)
        {
            var debugVis = go.AddComponent<HitboxDebugVisual>();
            debugVis.color = GetPartColor(partType);
        }
#endif
    }

    /// <summary>
    /// 在 Animator 层级中按名称模式查找骨骼（用于非 Humanoid Avatar）
    /// </summary>
    private Transform FindBoneByName(BodyPartType partType)
    {
        if (!BoneNamePatterns.TryGetValue(partType, out var patterns))
            return null;

        // 递归搜索所有子 Transform
        foreach (var pattern in patterns)
        {
            var found = SearchRecursive(_animator.transform, pattern);
            if (found != null)
                return found;
        }
        return null;
    }

    private Transform SearchRecursive(Transform parent, string name)
    {
        // 精确匹配或包含匹配
        if (parent.name == name || parent.name.Contains(name, StringComparison.OrdinalIgnoreCase))
            return parent;

        for (int i = 0; i < parent.childCount; i++)
        {
            var found = SearchRecursive(parent.GetChild(i), name);
            if (found != null)
                return found;
        }
        return null;
    }

    /// <summary>
    /// 根据命中位置判断身体部位（用于服务端命中判定）
    /// </summary>
    public static BodyPartType GetBodyPartFromHitPosition(Vector3 hitPoint, Vector3 playerBasePosition, float playerHeight)
    {
        float relativeY = hitPoint.y - playerBasePosition.y;
        float ratio = relativeY / playerHeight;

        if (ratio >= 0.80f) return BodyPartType.Head;
        if (ratio >= 0.55f) return BodyPartType.Chest;
        if (ratio >= 0.30f) return BodyPartType.Abdomen;
        return BodyPartType.LeftLeg; // 腿部（下半身统一）
    }

    /// <summary>
    /// 获取身体部位对应的伤害倍率
    /// </summary>
    public static float GetDamageMultiplier(BodyPartType partType)
    {
        return DamageMultipliers.GetValueOrDefault(partType, 1f);
    }

    private static Color GetPartColor(BodyPartType part)
    {
        return part switch
        {
            BodyPartType.Head => Color.red,
            BodyPartType.Chest => new Color(1f, 0.5f, 0f),
            BodyPartType.Abdomen => Color.yellow,
            BodyPartType.LeftArm => Color.green,
            BodyPartType.RightArm => Color.green,
            BodyPartType.LeftLeg => Color.blue,
            BodyPartType.RightLeg => Color.blue,
            _ => Color.white
        };
    }
}

/// <summary>
/// 编辑器可视化：在 Scene 视图中绘制碰撞体线框
/// </summary>
#if UNITY_EDITOR
public class HitboxDebugVisual : MonoBehaviour
{
    public Color color = Color.green;

    private void OnDrawGizmos()
    {
        var capsule = GetComponent<CapsuleCollider>();
        if (capsule == null) return;

        Gizmos.color = new Color(color.r, color.g, color.b, 0.3f);
        Gizmos.matrix = transform.localToWorldMatrix;

        float radius = capsule.radius;
        float height = capsule.height;

        // 绘制两个半球 + 圆柱体
        Vector3 topCenter = Vector3.up * (height / 2f - radius);
        Vector3 bottomCenter = Vector3.down * (height / 2f - radius);

        Gizmos.DrawSphere(topCenter, radius);
        Gizmos.DrawSphere(bottomCenter, radius);

        // 简化：绘制线框胶囊
        Gizmos.color = new Color(color.r, color.g, color.b, 0.8f);
        DrawWireCapsule(topCenter, bottomCenter, radius);
    }

    private void DrawWireCapsule(Vector3 top, Vector3 bottom, float radius)
    {
        // 顶部圆
        DrawWireCircle(top, Vector3.up, radius);
        // 底部圆
        DrawWireCircle(bottom, Vector3.up, radius);
        // 连接线
        for (int i = 0; i < 4; i++)
        {
            float angle = i * Mathf.PI / 2f;
            Vector3 offset = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius;
            Gizmos.DrawLine(top + offset, bottom + offset);
        }
    }

    private void DrawWireCircle(Vector3 center, Vector3 normal, float radius)
    {
        Vector3 tangent = Vector3.Cross(normal, Vector3.forward).normalized;
        if (tangent == Vector3.zero) tangent = Vector3.right;
        Vector3 binormal = Vector3.Cross(normal, tangent).normalized;

        int segments = 16;
        Vector3 prev = center + tangent * radius;
        for (int i = 1; i <= segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            Vector3 next = center + (tangent * Mathf.Cos(angle) + binormal * Mathf.Sin(angle)) * radius;
            Gizmos.DrawLine(prev, next);
            prev = next;
        }
    }
}
#endif
