using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ShootingGame.Shared.ECS;
using ShootingGame.Shared.Simulation;

/// <summary>
/// Scene-persisted arcade HUD skin matching the reference game's slanted frame layout.
/// The hierarchy is generated in the editor; runtime only updates its data.
/// </summary>
public sealed class ArcadeHudVisuals : MonoBehaviour
{
    private const string RootName = "ArcadeHudVisuals";
    private const string PortraitPath = "UIReference/AkaneIida_Glass";
    private const string AmmoSpritePath = "UIReference/ammo4";

    private TMP_Text _score;
    private TMP_Text _timer;
    private TMP_Text _ammo;
    private Image[] _shells = new Image[4];
    private float _displayedScore;
    private float _remainingSeconds = GameConstants.MatchDurationSeconds;
    private bool _built;
    private int _lastAmmo = -1;
    private readonly Vector2[] _shellBasePositions = new Vector2[4];
    private readonly RectTransform[] _shellRects = new RectTransform[4];
    private readonly Vector2[] _shellStartPositions = new Vector2[4];
    private readonly Vector2[] _shellTargets = new Vector2[4];
    private readonly int[] _activeShellIndices = new int[4];
    private float _shellShotElapsed;
    private float _shellShotDuration;
    private int _ejectIndex = -1;
    private int _shotVisibleCount;
    private readonly int[] _queuedShotAmmo = new int[GameConstants.MaxAmmoPerClip];
    private int _queuedShotRead;
    private int _queuedShotWrite;
    private readonly float[] _shellShiftDelays = new float[4];
    private bool _shellShotAnimating;

    /// <summary>Called by the Unity editor generator; never called from runtime startup.</summary>
    public void GenerateInEditor()
    {
        Build();
    }

    /// <summary>Replaces the saved visual root while the Fight scene is open in the editor.</summary>
    public void RebuildInEditor()
    {
        var existingRoot = transform.Find(RootName);
        if (existingRoot != null)
        {
            // The feed belongs to BattleUI; preserve it while replacing only the skin root.
            var feed = existingRoot.Find("KillFeedPanel");
            if (feed != null)
                feed.SetParent(transform.Find("UIUpperBase") ?? transform, false);
            DestroyImmediate(existingRoot.gameObject);
        }
        _built = false;
        _score = null;
        _timer = null;
        _ammo = null;
        _shells = new Image[4];
        Build();
    }

    private void OnEnable()
    {
        _remainingSeconds = GameConstants.MatchDurationSeconds;
        _lastAmmo = -1;
        _queuedShotRead = 0;
        _queuedShotWrite = 0;
        var existingRoot = transform.Find(RootName);
        if (existingRoot != null)
        {
            CacheReferences(existingRoot);
            _built = _score != null;
        }
    }

    private void Build()
    {
        if (_built)
            return;
        _built = true;

        var existingRoot = transform.Find(RootName);
        if (existingRoot != null)
        {
            CacheReferences(existingRoot);
            if (_score != null)
            {
                StyleExistingText();
                return;
            }
        }

        var root = existingRoot != null ? existingRoot.gameObject : new GameObject(RootName, typeof(RectTransform));
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
            if (shell != null)
            {
                _shellRects[i] = shell.rectTransform;
                _shellBasePositions[i] = shell.rectTransform.anchoredPosition;
            }
        }

        StyleExistingText();
    }

    private void CacheReferences(Transform root)
    {
        _score = FindText(root, "ScoreNum");
        _timer = FindText(root, "TimeNum");
        _ammo = FindText(root, "AmmoNum");
        for (int i = 0; i < _shells.Length; i++)
        {
            var shell = root.Find($"AmmoShell{i + 1}");
            _shells[i] = shell != null ? shell.GetComponent<Image>() : null;
            _shellRects[i] = shell != null ? shell.GetComponent<RectTransform>() : null;
            _shellBasePositions[i] = _shellRects[i] != null ? _shellRects[i].anchoredPosition : Vector2.zero;
        }
    }

    private static TMP_Text FindText(Transform root, string name)
    {
        var child = root.Find(name);
        return child != null ? child.GetComponent<TMP_Text>() : null;
    }

    private void Update()
    {
        if (!_built)
            return;

        UpdateScore();
        UpdateTimer();
        UpdateAmmo();
        UpdateShellAnimations();
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
        var client = BattleClient.Instance;
        if (client != null && client.ServerMatchRemainingTicks >= 0)
            _remainingSeconds = client.ServerMatchRemainingTicks * GameConstants.TickDelta;
        _timer?.SetText("{0:000}.{1:00}", Mathf.FloorToInt(_remainingSeconds), Mathf.FloorToInt((_remainingSeconds % 1f) * 100f));
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

        if (_lastAmmo < 0)
            ResetShellLayout(current);
        else if (current < _lastAmmo)
            QueueShellShots(_lastAmmo, current);
        else if (current > _lastAmmo)
            ResetShellLayout(current);
        _lastAmmo = current;
        _ammo?.SetText("{0}", current);
    }

    private void UpdateShellAnimations()
    {
        if (!_shellShotAnimating)
            return;

        _shellShotElapsed += Time.deltaTime;
        for (int i = 0; i < _shells.Length; i++)
        {
            if (_shells[i] == null || !_shells[i].gameObject.activeSelf)
                continue;

            if (_shellRects[i] != null)
            {
                if (i == _ejectIndex)
                {
                    float ejectT = Mathf.Clamp01(_shellShotElapsed / 0.24f);
                    _shellRects[i].anchoredPosition = _shellStartPositions[i] + new Vector2(72f * ejectT, 66f * ejectT);
                    _shellRects[i].localRotation = Quaternion.Euler(0f, 0f, -70f * ejectT);
                }
                else
                {
                    float shiftT = Mathf.Clamp01((_shellShotElapsed - _shellShiftDelays[i]) / 0.18f);
                    _shellRects[i].anchoredPosition = Vector2.Lerp(_shellStartPositions[i], _shellTargets[i], shiftT);
                }
            }
            if (i == _ejectIndex)
            {
                var color = _shells[i].color;
                color.a = 1f - Mathf.Clamp01(_shellShotElapsed / 0.24f);
                _shells[i].color = color;
            }
        }

        if (_shellShotElapsed < _shellShotDuration)
            return;

        for (int i = 0; i < _shells.Length; i++)
        {
            if (_shells[i] == null || i == _ejectIndex)
                continue;
            if (_shellRects[i] != null)
            {
                _shellRects[i].anchoredPosition = _shellTargets[i];
                _shellRects[i].localRotation = Quaternion.identity;
            }
        }

        if (_shells[_ejectIndex] != null)
        {
            var color = _shells[_ejectIndex].color;
            color.a = 0.98f;
            _shells[_ejectIndex].color = color;
            if (_shotVisibleCount >= _shells.Length)
            {
                _shells[_ejectIndex].gameObject.SetActive(true);
                if (_shellRects[_ejectIndex] != null)
                    _shellRects[_ejectIndex].anchoredPosition = _shellBasePositions[0];
            }
            else
            {
                _shells[_ejectIndex].gameObject.SetActive(false);
            }
        }
        _shellShotAnimating = false;
        _ejectIndex = -1;
        TryStartQueuedShellShot();
    }

    private void ResetShellLayout(int currentAmmo)
    {
        int visible = Mathf.Clamp(Mathf.Min(currentAmmo, _shells.Length), 0, _shells.Length);
        int start = _shells.Length - visible;
        _shellShotAnimating = false;
        _ejectIndex = -1;
        _queuedShotRead = 0;
        _queuedShotWrite = 0;
        for (int i = 0; i < _shells.Length; i++)
        {
            if (_shells[i] == null)
                continue;
            if (_shellRects[i] != null)
            {
                _shellRects[i].anchoredPosition = _shellBasePositions[i];
                _shellRects[i].localRotation = Quaternion.identity;
            }
            var color = _shells[i].color;
            color.a = 0.98f;
            _shells[i].color = color;
            _shells[i].gameObject.SetActive(i >= start);
        }
    }

    private void StartShellShot(int currentAmmo)
    {
        if (_shellShotAnimating)
            return;

        int activeCount = 0;
        for (int i = 0; i < _shells.Length; i++)
            if (_shells[i] != null && _shells[i].gameObject.activeSelf)
                _activeShellIndices[activeCount++] = i;
        if (activeCount == 0)
            return;

        for (int i = 1; i < activeCount; i++)
        {
            int value = _activeShellIndices[i];
            int j = i - 1;
            while (j >= 0 && _shellRects[_activeShellIndices[j]].anchoredPosition.x > _shellRects[value].anchoredPosition.x)
            {
                _activeShellIndices[j + 1] = _activeShellIndices[j--];
            }
            _activeShellIndices[j + 1] = value;
        }

        _ejectIndex = _activeShellIndices[activeCount - 1];
        _shotVisibleCount = Mathf.Clamp(Mathf.Min(currentAmmo, _shells.Length), 0, _shells.Length);
        int survivorCount = activeCount - 1;
        int targetStart = _shells.Length - survivorCount;
        int survivor = 0;
        for (int i = 0; i < activeCount; i++)
        {
            int shellIndex = _activeShellIndices[i];
            if (_shellRects[shellIndex] != null)
                _shellStartPositions[shellIndex] = _shellRects[shellIndex].anchoredPosition;
            if (shellIndex == _ejectIndex)
                continue;
            int targetSlot = Mathf.Clamp(targetStart + survivor++, 0, _shells.Length - 1);
            _shellTargets[shellIndex] = _shellBasePositions[targetSlot];
            // The neighbour of the ejected shell fills the rightmost slot first.
            _shellShiftDelays[shellIndex] = 0.24f + Mathf.Max(0, survivorCount - survivor) * 0.07f;
        }
        _shellShotElapsed = 0f;
        _shellShotDuration = 0.24f + survivorCount * 0.07f + 0.18f;
        _shellShotAnimating = true;
    }

    private void QueueShellShots(int previousAmmo, int currentAmmo)
    {
        for (int ammoAfterShot = previousAmmo - 1; ammoAfterShot >= currentAmmo; ammoAfterShot--)
        {
            if (!_shellShotAnimating && _queuedShotRead == _queuedShotWrite)
                StartShellShot(ammoAfterShot);
            else if (_queuedShotWrite - _queuedShotRead < _queuedShotAmmo.Length)
                _queuedShotAmmo[_queuedShotWrite++ % _queuedShotAmmo.Length] = ammoAfterShot;
        }
    }

    private void TryStartQueuedShellShot()
    {
        if (_queuedShotRead >= _queuedShotWrite)
            return;
        StartShellShot(_queuedShotAmmo[_queuedShotRead++ % _queuedShotAmmo.Length]);
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

        // Keep the kill feed above the opaque slanted top band.
        var feed = transform.Find("UIUpperBase/KillFeedPanel") ?? transform.Find("KillFeedPanel");
        var root = transform.Find(RootName);
        if (feed != null && root != null)
        {
            feed.SetParent(root, false);
            var feedRect = feed.GetComponent<RectTransform>();
            if (feedRect != null)
            {
                feedRect.anchorMin = new Vector2(0.012f, 0.57f);
                feedRect.anchorMax = new Vector2(0.36f, 0.84f);
                feedRect.pivot = new Vector2(0f, 0f);
                feedRect.offsetMin = Vector2.zero;
                feedRect.offsetMax = Vector2.zero;
            }
        }

        SetLegacyTopPanelVisible("ScorePanel", false);
        SetLegacyTopPanelVisible("NetworkStatusPanel", false);

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

    private void SetLegacyTopPanelVisible(string name, bool visible)
    {
        var panel = transform.Find($"UIUpperBase/{name}") ?? transform.Find(name);
        if (panel != null)
            panel.gameObject.SetActive(visible);
    }
}
