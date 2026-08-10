using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 程序化技能特效管理器（无美术资源，纯代码生成）。
/// 技能预测确认链：预测施法时生成特效 → 服务器 Confirm 保留 / Reject 回滚。
/// </summary>
public class ProceduralEffectManager : MonoBehaviour
{
    public static ProceduralEffectManager Instance { get; private set; }

    /// <summary>预测中的特效（instanceId → 特效对象），Reject 时回滚</summary>
    private readonly Dictionary<int, GameObject> _predictedEffects = new Dictionary<int, GameObject>();

    // 预测特效 id 分配（本地递增，客户端预测用）
    private int _nextPredictionId = 1;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    /// <summary>
    /// 预测施法：立即播放程序化特效，返回预测 instanceId（供 Confirm/Reject 匹配）。
    /// </summary>
    public int PlayPredictedAbilityEffect(Vector3 position, Vector3 forward, Color tint)
    {
        int predictionId = _nextPredictionId++;
        GameObject fx = SpawnAbilityBurst(position, forward, tint);
        _predictedEffects[predictionId] = fx;
        return predictionId;
    }

    /// <summary>服务器确认：特效保留并自然播放完（标记不再回滚）</summary>
    public void OnAbilityConfirmed(int instanceId)
    {
        if (_predictedEffects.TryGetValue(instanceId, out var fx))
        {
            _predictedEffects.Remove(instanceId); // 不再受回滚管理
            Debug.Log($"[ProceduralFX] Ability {instanceId} confirmed, effect kept");
        }
    }

    /// <summary>服务器拒绝：移除预测特效（回滚）</summary>
    public void OnAbilityRejected(int instanceId)
    {
        if (_predictedEffects.TryGetValue(instanceId, out var fx))
        {
            _predictedEffects.Remove(instanceId);
            if (fx != null) Destroy(fx);
            Debug.Log($"[ProceduralFX] Ability {instanceId} rejected, predicted effect rolled back");
        }
    }

    /// <summary>战斗结束清理</summary>
    public void ClearAll()
    {
        foreach (var fx in _predictedEffects.Values)
            if (fx != null) Destroy(fx);
        _predictedEffects.Clear();
    }

    /// <summary>
    /// 程序化生成"技能施法爆发"特效：粒子喷射 + 冲击波圆环（纯代码，无 Prefab/贴图依赖）。
    /// </summary>
    private GameObject SpawnAbilityBurst(Vector3 position, Vector3 forward, Color tint)
    {
        var root = new GameObject($"ProceduralAbilityFX_{_nextPredictionId}");
        root.transform.position = position;

        // --- 主粒子：向上喷发 + 锥形扩散（代码配置，无资源）---
        var psGo = new GameObject("Burst");
        psGo.transform.SetParent(root.transform, false);
        var ps = psGo.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.startColor = tint;
        main.startLifetime = 0.6f;
        main.startSpeed = 6f;
        main.startSize = 0.25f;
        main.maxParticles = 80;
        main.playOnAwake = false;

        var emit = ps.emission;
        emit.rateOverTime = 0f;
        emit.SetBursts(new ParticleSystem.Burst[]
        {
            new ParticleSystem.Burst(0f, 40)
        });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 20f;
        shape.radius = 0.2f;
        shape.rotation = new Vector3(-90f, 0f, 0f); // 锥口朝前

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;

        // 圆形粒子贴图（内置白点），tint 上色即可
        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.material = BuiltinCircleMaterial();

        ps.Play();

        // --- 冲击波圆环：缩放发光圆环（用 LineRenderer 程序化画圆）---
        var ringGo = new GameObject("Shockwave");
        ringGo.transform.SetParent(root.transform, false);
        var lr = ringGo.AddComponent<LineRenderer>();
        lr.material = BuiltinCircleMaterial();
        lr.startColor = new Color(tint.r, tint.g, tint.b, 0.9f);
        lr.endColor = new Color(tint.r, tint.g, tint.b, 0f);
        lr.startWidth = 0.06f;
        lr.endWidth = 0.02f;
        lr.positionCount = 40;
        lr.useWorldSpace = false;
        ringGo.transform.rotation = Quaternion.LookRotation(forward);

        float ringRadius = 0.4f;
        for (int i = 0; i < 40; i++)
        {
            float angle = i / 39f * Mathf.PI * 2f;
            lr.SetPosition(i, new Vector3(Mathf.Cos(angle) * ringRadius, 0f, Mathf.Sin(angle) * ringRadius));
        }
        var ringAnim = ringGo.AddComponent<ShockwaveAnimator>();
        ringAnim.Init(lr, 0.35f);

        // 2 秒后自动销毁（即使 Confirm 也自然消失）
        Destroy(root, 2f);
        return root;
    }

    private static Material _builtinCircleMat;
    private static Material BuiltinCircleMaterial()
    {
        if (_builtinCircleMat != null) return _builtinCircleMat;
        // 内置圆点贴图（Built-in UI 圆），无外部资源依赖
        var tex = Texture2D.whiteTexture;
        _builtinCircleMat = new Material(Shader.Find("Sprites/Default"));
        _builtinCircleMat.mainTexture = tex;
        return _builtinCircleMat;
    }
}

/// <summary>冲击波圆环动画：从小圆环放大到消散</summary>
public class ShockwaveAnimator : MonoBehaviour
{
    private LineRenderer _lr;
    private float _duration;
    private float _elapsed;

    public void Init(LineRenderer lr, float duration)
    {
        _lr = lr;
        _duration = duration;
    }

    private void Update()
    {
        if (_lr == null) return;
        _elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(_elapsed / _duration);
        float scale = Mathf.Lerp(1f, 6f, t);
        transform.localScale = new Vector3(scale, 1f, scale);
        var c = _lr.startColor;
        c.a = Mathf.Lerp(0.9f, 0f, t);
        _lr.startColor = c;
        _lr.endColor = new Color(c.r, c.g, c.b, 0f);
        if (t >= 1f) Destroy(gameObject);
    }
}
