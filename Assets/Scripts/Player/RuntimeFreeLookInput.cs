using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Cinemachine;

/// <summary>
/// Runtime mouse input provider for CinemachineFreeLook.
/// Reads mouse delta from the Input System and directly drives the FreeLook's axis values.
///
/// This is necessary because the old CinemachineFreeLook cannot auto-discover the new Input System.
/// This component bypasses AxisState.Update() by setting m_XAxis.Value and m_YAxis.Value directly.
/// Since FreeLook's default axis names ("Mouse X", "Mouse Y") reference the legacy Input Manager
/// (which returns 0 when only the Input System package is active), AxisState.Update() is a no-op
/// and our manually-set values persist through the camera pipeline unchanged.
/// </summary>
public class RuntimeFreeLookInput : MonoBehaviour
{
    [Header("Mouse Sensitivity")]
    [SerializeField] [Tooltip("水平旋转速度（度/像素）")]
    private float lookSpeedX = 0.05f;

    [SerializeField] [Tooltip("垂直旋转速度（FreeLook Y 轴范围 0~1，每像素变化量）")]
    private float lookSpeedY = 0.001f;

    [SerializeField] [Tooltip("Invert the vertical mouse look direction.")]
    private bool invertY = false;

    private CinemachineFreeLook _freeLook;
    private PlayerInputAction _input;
    private float _xAxisValue;
    private float _yAxisValue;

    private void Awake()
    {
        _freeLook = GetComponent<CinemachineFreeLook>();
        _input = new PlayerInputAction();
    }

    private void Start()
    {
        // Seed accumulated values from whatever the FreeLook already has
        // (preserved across camera swaps via PlayerAimState.SetNormalCamera/SetAimCamera)
        if (_freeLook != null)
        {
            _xAxisValue = _freeLook.m_XAxis.Value;
            _yAxisValue = _freeLook.m_YAxis.Value;
        }
    }

    private void OnEnable() => _input.Enable();
    private void OnDisable() => _input.Disable();

    private void Update()
    {
        if (_freeLook == null) return;

        Vector2 mouseDelta = _input.Simple.MousesXY.ReadValue<Vector2>();

        // 当鼠标不动时（delta 接近零），不累积旋转
        if (Mathf.Abs(mouseDelta.x) < 0.001f && Mathf.Abs(mouseDelta.y) < 0.001f)
            return;

        _xAxisValue += mouseDelta.x * lookSpeedX;
        _yAxisValue += mouseDelta.y * lookSpeedY * (invertY ? 1f : -1f);

        // Clamp vertical axis to the FreeLook's expected 0..1 range.
        _yAxisValue = Mathf.Clamp01(_yAxisValue);

        // Drive the FreeLook axes directly.
        _freeLook.m_XAxis.Value = _xAxisValue;
        _freeLook.m_YAxis.Value = _yAxisValue;
    }
}
