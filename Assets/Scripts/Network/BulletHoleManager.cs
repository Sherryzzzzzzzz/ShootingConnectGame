using UnityEngine;

/// <summary>
/// 弹孔管理器：命中点生成贴墙弹孔贴片（程序生成贴图，无需外部资源）。
/// 弹孔 quad 沿命中法线贴合墙面，避免 billboard 造成的悬浮/穿帮。
/// </summary>
public static class BulletHoleManager
{
    private static Material _bulletHoleMat;
    private static Texture2D _bulletHoleTex;
    private static readonly int ColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int MainTexId = Shader.PropertyToID("_BaseMap");

    /// <summary>沿表面法线偏移，避免与墙面 z-fighting。</summary>
    private const float SurfaceOffset = 0.015f;

    /// <summary>在命中点生成一个贴墙弹孔（沿法线贴合，10 秒后消失）。</summary>
    public static void Spawn(Vector3 position, Vector3 normal, float size = 0.06f)
    {
        // 法线无效时退化为朝相机方向的表面法线近似
        if (normal.sqrMagnitude < 0.01f)
        {
            var cam = Camera.main != null ? Camera.main : Object.FindFirstObjectByType<Camera>();
            if (cam != null)
                normal = -(cam.transform.position - position).normalized;
            else
                normal = Vector3.up;
        }
        normal.Normalize();

        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "BulletHole";
        go.transform.position = position + normal * SurfaceOffset;

        // 贴墙：quad 正面（+Z）朝向墙面法线反方向，即贴附在表面上
        go.transform.rotation = Quaternion.LookRotation(-normal);

        go.transform.localScale = new Vector3(size, size, 1f);

        var renderer = go.GetComponent<MeshRenderer>();
        renderer.material = GetMaterial();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        Object.Destroy(go, 10f);
    }

    private static Material GetMaterial()
    {
        if (_bulletHoleMat != null) return _bulletHoleMat;

        _bulletHoleTex = CreateBulletHoleTexture();

        // 用 URP 的 Unlit 透明材质（多名字兜底）
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Unlit/Texture");
        if (shader == null)
        {
            Debug.LogWarning("[BulletHole] 找不到可用 shader，弹孔不显示");
            return null;
        }
        _bulletHoleMat = new Material(shader);
        _bulletHoleMat.SetTexture(MainTexId, _bulletHoleTex);
        _bulletHoleMat.SetColor(ColorId, Color.black);
        // URP 透明设置
        _bulletHoleMat.SetFloat("_Surface", 1f);
        _bulletHoleMat.SetFloat("_Blend", 0f);
        _bulletHoleMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        _bulletHoleMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        _bulletHoleMat.SetInt("_ZWrite", 0);
        _bulletHoleMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        _bulletHoleMat.EnableKeyword("_ALPHABLEND_ON");
        _bulletHoleMat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        _bulletHoleMat.renderQueue = 3000;

        return _bulletHoleMat;
    }

    /// <summary>程序生成弹孔贴图（黑色圆点 + 羽化边缘）。</summary>
    private static Texture2D CreateBulletHoleTexture()
    {
        int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        Vector2 center = new Vector2(size / 2f, size / 2f);
        float radius = size * 0.4f;
        float soft = size * 0.15f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), center);
                float alpha;
                if (dist < radius)
                    alpha = 0.9f;                     // 内部：深黑
                else if (dist < radius + soft)
                    alpha = 0.9f * (1f - (dist - radius) / soft); // 边缘羽化
                else
                    alpha = 0f;                        // 外部：透明

                tex.SetPixel(x, y, new Color(0f, 0f, 0f, alpha));
            }
        }
        tex.Apply();
        return tex;
    }
}
