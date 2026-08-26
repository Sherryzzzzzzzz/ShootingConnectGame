using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ShootingGame.Shared.ECS;

/// <summary>
/// Runtime-only arcade HUD skin matching the reference game's slanted frame layout.
/// Existing BattleUI controls remain active underneath this presentation layer.
/// </summary>
public sealed class ArcadeHudVisuals : MonoBehaviour
{
    private const string RootName = "ArcadeHudVisuals";
    private const string PortraitPath = "UIReference/AkaneIida_Glass";
    private const string AmmoSpritePath = "UIReference/ammo4";

    private TMP_Text _score;
    private TMP_Text _timer;
    private TMP_Text _ammo;
    private Image[] _shells;
    private float _displayedScore;
    private float _remainingSeconds = 13f;
    private bool _built;

    public static ArcadeHudVisuals Ensure(Transform canvas)
    {
        if (canvas == null)
            return null;

        var visuals = canvas.GetComponent<ArcadeHudVisuals>();
        if (visuals == null)
            visuals = canvas.gameObject.AddComponent<ArcadeHudVisuals>();
        visuals.Build();
        return visuals;
    }

    private void Build()
    {
        if (_built)
            return;
        _built = true;

        var root = new GameObject(RootName, typeof(RectTransform));
        root.transform.SetParent(transform, false);
        var rootRect = root.GetComponent<RectTransform>();
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;

        var top = CreateBand(root.transform, "ArcadeTopBand", true, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 148f));
        var bottom = CreateBand(root.transform, "ArcadeBottomBand", false, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 132f));
        top.SetSiblingIndex(0);
        bottom.SetSiblingIndex(0);

        _score = CreateText(root.transform, "ScoreNum", "000000", 82, Color.white, TextAlignmentOptions.Left,
            new Vector2(0.012f, 0.88f), new Vector2(0.32f, 0.995f));
        CreateText(root.transform, "ScoreTitle", "SCORE", 27, Color.white, TextAlignmentOptions.Left,
            new Vector2(0.012f, 0.955f), new Vector2(0.18f, 1f));
        CreateText(root.transform, "StageInfo", "STAGE 1", 25, Color.white, TextAlignmentOptions.Center,
            new Vector2(0.36f, 0.94f), new Vector2(0.58f, 1f));
        CreateText(root.transform, "RoomInfo", "ROOM 1", 47, Color.white, TextAlignmentOptions.Center,
            new Vector2(0.36f, 0.875f), new Vector2(0.60f, 0.97f));
        _timer = CreateText(root.transform, "TimeNum", "13.00", 88, new Color(1f, 0.94f, 0.2f), TextAlignmentOptions.Right,
            new Vector2(0.71f, 0.86f), new Vector2(0.90f, 0.995f));
        CreateText(root.transform, "TimeTitle", "TIME\nREMAINING", 21, Color.white, TextAlignmentOptions.Left,
            new Vector2(0.885f, 0.90f), new Vector2(0.995f, 1f));

        var portrait = CreateImage(root.transform, "AkanePortrait", Resources.Load<Sprite>(PortraitPath),
            new Vector2(0.01f, 0.01f), new Vector2(0.245f, 0.40f));
        if (portrait != null)
            portrait.preserveAspect = true;

        var stats = CreateText(root.transform, "PlayerStats", "DMG  8\nFWK 38\nSPD 39\nHDL 40\nSHK 39", 21,
            Color.white, TextAlignmentOptions.Left, new Vector2(0.012f, 0.145f), new Vector2(0.22f, 0.36f));
        stats.lineSpacing = -8f;
        CreateText(root.transform, "PlayerName", "Akane Iida", 29, Color.white, TextAlignmentOptions.Left,
            new Vector2(0.11f, 0.012f), new Vector2(0.36f, 0.11f));
        CreateText(root.transform, "AmmoTitle", "AMMO REMAINING", 19, Color.white, TextAlignmentOptions.Right,
            new Vector2(0.75f, 0.25f), new Vector2(0.985f, 0.33f));
        _ammo = CreateText(root.transform, "AmmoNum", "4", 86, new Color(1f, 0.92f, 0.55f), TextAlignmentOptions.Right,
            new Vector2(0.91f, 0.015f), new Vector2(0.995f, 0.23f));

        _shells = new Image[4];
        var shellSprite = Resources.Load<Sprite>(AmmoSpritePath);
        for (int i = 0; i < _shells.Length; i++)
        {
            float x = 0.82f + i * 0.034f;
            var shell = CreateImage(root.transform, $"AmmoShell{i + 1}", shellSprite,
                new Vector2(x, 0.035f + i * 0.012f), new Vector2(x + 0.033f, 0.24f + i * 0.012f));
            if (shell != null)
            {
                shell.preserveAspect = true;
                shell.color = new Color(1f, 0.86f, 0.42f, 0.98f);
            }
            _shells[i] = shell;
        }

        StyleExistingText();
    }

    private void Update()
    {
        if (!_built)
            return;

        UpdateScore();
        UpdateTimer();
        UpdateAmmo();
    }

    private void UpdateScore()
    {
        int kills = 0;
        var client = BattleClient.Instance;
        var frame = client != null ? client.GetLatestFrame() : null;
        if (frame?.PlayerStates != null && client != null)
        {
            foreach (var state in frame.PlayerStates)
            {
                if (state.PlayerId == client.BattlePlayerId)
                {
                    kills = state.Kills;
                    break;
                }
            }
        }

        float target = Mathf.Max(0, kills) * 100f;
        _displayedScore = Mathf.MoveTowards(_displayedScore, target, Mathf.Max(1f, Mathf.Abs(target - _displayedScore) * Time.unscaledDeltaTime * 10f));
        _score?.SetText("{0:000000}", Mathf.RoundToInt(_displayedScore));
    }

    private void UpdateTimer()
    {
        if (BattleManager.Instance != null && BattleManager.Instance.State == BattleManager.BattleState.Playing)
            _remainingSeconds = Mathf.Max(0f, _remainingSeconds - Time.deltaTime);
        _timer?.SetText("{0:00}.{1:00}", Mathf.FloorToInt(_remainingSeconds), Mathf.FloorToInt((_remainingSeconds % 1f) * 100f));
        if (_timer != null && _remainingSeconds <= 3f)
            _timer.color = Color.Lerp(new Color(1f, 0.94f, 0.2f), Color.red, Mathf.PingPong(Time.unscaledTime * 3f, 1f));
    }

    private void UpdateAmmo()
    {
        int current = 4;
        var world = ClientECSWorld.Instance;
        if (world != null)
        {
            var entity = world.GetLocalPlayerEntity();
            if (world.EntityManager != null && world.EntityManager.IsValid(entity) && world.EntityManager.TryGetComponent<AmmoComponent>(entity, out var ammo))
            {
                current = Mathf.Max(0, ammo.Current);
            }
        }

        _ammo?.SetText("{0}", current);
        if (_shells == null)
            return;
        for (int i = 0; i < _shells.Length; i++)
            if (_shells[i] != null)
                _shells[i].gameObject.SetActive(i < Mathf.Min(current, _shells.Length));
    }

    private static RectTransform CreateBand(Transform parent, string name, bool topBand, Vector2 min, Vector2 max, Vector2 pivot, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.pivot = pivot;
        rect.sizeDelta = size;
        var graphic = go.AddComponent<SlantedHudGraphic>();
        graphic.TopBand = topBand;
        graphic.raycastTarget = false;
        return rect;
    }

    private static TMP_Text CreateText(Transform parent, string name, string value, float size, Color color,
        TextAlignmentOptions alignment, Vector2 anchorMin, Vector2 anchorMax)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        var text = go.AddComponent<TextMeshProUGUI>();
        text.text = value;
        text.font = TMP_Settings.defaultFontAsset;
        text.fontSize = size;
        text.color = color;
        text.alignment = alignment;
        text.fontStyle = FontStyles.Bold;
        text.enableWordWrapping = false;
        text.outlineWidth = 0.28f;
        text.outlineColor = Color.black;
        text.raycastTarget = false;
        return text;
    }

    private static Image CreateImage(Transform parent, string name, Sprite sprite, Vector2 anchorMin, Vector2 anchorMax)
    {
        if (sprite == null)
            return null;
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        var image = go.AddComponent<Image>();
        image.sprite = sprite;
        image.raycastTarget = false;
        return image;
    }

    private void StyleExistingText()
    {
        foreach (var text in GetComponentsInChildren<TMP_Text>(true))
        {
            text.fontStyle |= FontStyles.Bold;
            text.outlineWidth = Mathf.Max(text.outlineWidth, 0.18f);
            text.outlineColor = Color.black;
            text.raycastTarget = false;
        }

        var health = transform.Find("UILowerBase/HealthPanel") ?? transform.Find("HealthPanel");
        if (health == null)
            return;
        var rect = health.GetComponent<RectTransform>();
        if (rect == null)
            return;
        rect.anchorMin = new Vector2(0.23f, 0.12f);
        rect.anchorMax = new Vector2(0.40f, 0.25f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
