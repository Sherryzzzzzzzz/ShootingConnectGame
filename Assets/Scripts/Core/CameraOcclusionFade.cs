using UnityEngine;

/// <summary>
/// 相机遮挡物半透明处理：射线检测玩家与相机之间的遮挡物，设为 50% 透明度。
/// 遮挡消失后恢复原材质。不改变相机位置（不穿模，只是半透明显示）。
/// </summary>
public class CameraOcclusionFade : MonoBehaviour
{
    [SerializeField] private float fadeAlpha = 0.5f;   // 半透明程度（0=全透 1=不透明）
    [SerializeField] private LayerMask occludeLayers = ~0;
    [SerializeField] private float checkRadius = 0.2f;

    private Camera _cam;
    private Transform _target;
    private Renderer _occluder;
    private Material _originalMaterial;
    private Material _fadeMaterial;

    private void Awake()
    {
        // Cinemachine 虚拟相机（FreeLook）的 GameObject 可能没有 Camera 组件，用实际渲染相机
        _cam = Camera.main;
        if (_cam == null)
            _cam = FindFirstObjectByType<Camera>();
    }

    public void SetTarget(Transform target)
    {
        _target = target;
    }

    private void LateUpdate()
    {
        if (_cam == null || _target == null) return;

        Vector3 origin = _cam.transform.position;
        Vector3 toTarget = _target.position - origin;
        float dist = toTarget.magnitude;
        Vector3 dir = toTarget.normalized;

        // 从相机向目标做球形射线，检测遮挡物
        bool blocked = Physics.SphereCast(origin, checkRadius, dir, out var hit, dist, occludeLayers, QueryTriggerInteraction.Ignore);

        if (blocked)
        {
            var renderer = hit.collider.GetComponentInParent<Renderer>();
            if (renderer != null && renderer != _occluder)
            {
                Restore();
                _occluder = renderer;
                _originalMaterial = renderer.material;

                // 克隆材质并设为半透明（不污染原材质）
                _fadeMaterial = new Material(_originalMaterial);
                _fadeMaterial.SetFloat("_Mode", 3);      // Transparent
                _fadeMaterial.SetFloat("_Alpha", fadeAlpha);
                _fadeMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                _fadeMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                _fadeMaterial.SetInt("_ZWrite", 0);
                _fadeMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                _fadeMaterial.EnableKeyword("_ALPHABLEND_ON");
                renderer.material = _fadeMaterial;
            }
        }
        else
        {
            Restore();
        }
    }

    private void Restore()
    {
        if (_occluder != null && _originalMaterial != null)
        {
            _occluder.material = _originalMaterial;
        }
        _occluder = null;
        _originalMaterial = null;
        if (_fadeMaterial != null)
        {
            Destroy(_fadeMaterial);
            _fadeMaterial = null;
        }
    }

    private void OnDisable()
    {
        Restore();
    }
}
