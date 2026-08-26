using UnityEngine;

/// <summary>
/// Runtime-safe HUD anchors inspired by the reference Game00 hierarchy.
/// The anchors are created once and keep existing data-bound widgets reusable.
/// </summary>
public sealed class ReferenceHudLayout : MonoBehaviour
{
    public Transform Upper { get; private set; }
    public Transform Lower { get; private set; }
    public Transform Center { get; private set; }
    public Transform Overlay { get; private set; }

    public static ReferenceHudLayout Ensure(Transform canvas)
    {
        if (canvas == null) return null;

        var layout = canvas.GetComponent<ReferenceHudLayout>();
        if (layout == null) layout = canvas.gameObject.AddComponent<ReferenceHudLayout>();
        layout.Build();
        return layout;
    }

    private void Awake() => Build();

    private void Build()
    {
        Upper = EnsureAnchor("UIUpperBase");
        Lower = EnsureAnchor("UILowerBase");
        Center = EnsureAnchor("CenterBase");
        Overlay = EnsureAnchor("CanvasOverFade");
    }

    private Transform EnsureAnchor(string name)
    {
        var existing = transform.Find(name);
        if (existing != null) return existing;

        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(transform, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.pivot = new Vector2(0.5f, 0.5f);
        return go.transform;
    }
}
