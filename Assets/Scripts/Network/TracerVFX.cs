using UnityEngine;

/// <summary>
/// Cosmetic-only bullet tracer VFX. Draws a line from fire point in the aim direction.
/// No physics, no collision, no damage — that's all server-authoritative.
/// Uses a LineRenderer that fades out over a short duration.
/// </summary>
public class TracerVFX : MonoBehaviour
{
    [Header("Tracer Settings")]
    [SerializeField] private float tracerLength = 100f;
    [SerializeField] private float tracerDuration = 0.1f;
    [SerializeField] private float tracerWidth = 0.02f;
    [SerializeField] private Color tracerColor = new Color(1f, 0.8f, 0.2f, 1f);
    [SerializeField] private Material tracerMaterial;

    private static TracerVFX _instance;
    public static TracerVFX Instance => _instance;

    private void Awake()
    {
        _instance = this;
    }

    /// <summary>
    /// Spawn a cosmetic tracer line from origin in direction.
    /// </summary>
    public void SpawnTracer(Vector3 origin, Vector3 direction)
    {
        var go = new GameObject("Tracer");
        var lr = go.AddComponent<LineRenderer>();
        lr.positionCount = 2;
        lr.SetPosition(0, origin);
        lr.SetPosition(1, origin + direction.normalized * tracerLength);
        lr.startWidth = tracerWidth;
        lr.endWidth = tracerWidth * 0.5f;

        if (tracerMaterial != null)
        {
            lr.material = tracerMaterial;
        }
        else
        {
            // Use default unlit material
            lr.material = new Material(Shader.Find("Sprites/Default"));
        }

        lr.startColor = tracerColor;
        lr.endColor = new Color(tracerColor.r, tracerColor.g, tracerColor.b, 0f);

        Destroy(go, tracerDuration);
    }
}
