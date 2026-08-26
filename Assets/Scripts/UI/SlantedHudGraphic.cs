using UnityEngine;
using UnityEngine.UI;

public sealed class SlantedHudGraphic : Graphic
{
    [SerializeField] private bool topBand = true;
    [SerializeField] private Color outerColor = new Color(0.02f, 0.02f, 0.025f, 0.96f);
    [SerializeField] private Color lineColor = new Color(0.96f, 0.97f, 0.98f, 1f);
    [SerializeField] private Color panelColor = new Color(0.36f, 0.45f, 0.73f, 0.98f);
    [SerializeField] private Color accentColor = new Color(1f, 0.56f, 0.02f, 1f);

    public bool TopBand { get => topBand; set { topBand = value; SetVerticesDirty(); } }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        var r = rectTransform.rect;
        float slope = Mathf.Clamp(r.height * 0.40f, 24f, 90f);
        var outer = topBand
            ? new[] { new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin + slope), new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax) }
            : new[] { new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax - slope * 0.72f), new Vector2(r.xMin, r.yMax) };
        AddPolygon(vh, outer, outerColor);
        AddPolygon(vh, Inset(outer, 5f), lineColor);
        var panel = Inset(outer, 9f);
        AddPolygon(vh, panel, panelColor);
        float accentHeight = Mathf.Clamp(r.height * 0.07f, 7f, 14f);
        var accent = topBand
            ? new[] { panel[0], panel[1], panel[1] + Vector2.up * accentHeight, panel[0] + Vector2.up * accentHeight }
            : new[] { panel[3] - Vector2.up * accentHeight, panel[2] - Vector2.up * accentHeight, panel[2], panel[3] };
        AddPolygon(vh, accent, accentColor);
    }

    private static Vector2[] Inset(Vector2[] points, float amount)
    {
        var center = (points[0] + points[2]) * 0.5f;
        var result = new Vector2[points.Length];
        for (int i = 0; i < points.Length; i++)
            result[i] = points[i] - (points[i] - center).normalized * amount;
        return result;
    }

    private static void AddPolygon(VertexHelper vh, Vector2[] points, Color color)
    {
        int start = vh.currentVertCount;
        for (int i = 0; i < points.Length; i++) vh.AddVert(points[i], color, Vector2.zero);
        for (int i = 1; i < points.Length - 1; i++) vh.AddTriangle(start, start + i, start + i + 1);
    }
}
