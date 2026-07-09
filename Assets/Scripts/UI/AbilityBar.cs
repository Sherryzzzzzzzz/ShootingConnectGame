using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ShootingGame.Shared.Ability;
using ShootingGame.Shared.ECS;

/// <summary>
/// 技能栏 UI：显示 Q/E 两个英雄技能槽的冷却、持续时间和激活状态。
/// 从 ClientECSWorld 本地玩家实体读取 AbilityInstanceComponent 数据。
/// </summary>
public class AbilityBar : MonoBehaviour
{
    [Header("布局")]
    [SerializeField] private float slotSize = 80f;
    [SerializeField] private float slotSpacing = 12f;
    [SerializeField] private Vector2 barAnchor = new Vector2(0.5f, 0.05f);

    [Header("颜色")]
    [SerializeField] private Color cooldownOverlayColor = new Color(0, 0, 0, 0.6f);
    [SerializeField] private Color predictingColor = new Color(1f, 0.8f, 0f, 0.4f);
    [SerializeField] private Color activeColor = new Color(0.2f, 1f, 0.2f, 0.4f);
    [SerializeField] private Color normalSlotColor = new Color(0.15f, 0.15f, 0.15f, 0.75f);

    private struct SlotUI
    {
        public GameObject Root;
        public Image Background;
        public Image IconPlaceholder;
        public Image CooldownOverlay;
        public TMP_Text CooldownText;
        public TMP_Text KeyText;
        public TMP_Text NameText;
        public Image ActiveOverlay;
        public Image DurationBar;
    }

    private SlotUI _slotQ;
    private SlotUI _slotE;
    private TMP_FontAsset _font;
    private float _lastUpdateTime;

    public static AbilityBar Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        _font = TMP_Settings.defaultFontAsset;
        CreateSlotUI(ref _slotQ, "Q");
        CreateSlotUI(ref _slotE, "E");
    }

    private void Update()
    {
        _lastUpdateTime += Time.deltaTime;
        if (_lastUpdateTime < 0.1f) return;
        _lastUpdateTime = 0f;

        RefreshSlot(_slotQ, 2); // Q → Abilities[2]
        RefreshSlot(_slotE, 3); // E → Abilities[3]
    }

    private void CreateSlotUI(ref SlotUI slot, string keyName)
    {
        var canvas = GetComponentInChildren<Canvas>();
        if (canvas == null)
        {
            var canvasGo = new GameObject("AbilityBarCanvas");
            canvasGo.transform.SetParent(transform, false);
            canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
        }

        float xOffset = keyName == "Q" ? -(slotSize + slotSpacing) * 0.5f : (slotSize + slotSpacing) * 0.5f;

        slot.Root = new GameObject($"AbilitySlot_{keyName}");
        slot.Root.transform.SetParent(canvas.transform, false);
        var rootRect = slot.Root.AddComponent<RectTransform>();
        rootRect.anchorMin = barAnchor;
        rootRect.anchorMax = barAnchor;
        rootRect.pivot = new Vector2(0.5f, 0.5f);
        rootRect.sizeDelta = new Vector2(slotSize, slotSize);
        rootRect.anchoredPosition = new Vector2(xOffset, 0);

        // 背景
        var bgGo = new GameObject("Background");
        bgGo.transform.SetParent(slot.Root.transform, false);
        var bgRect = bgGo.AddComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;
        slot.Background = bgGo.AddComponent<Image>();
        slot.Background.color = normalSlotColor;

        // 图标占位
        var iconGo = new GameObject("Icon");
        iconGo.transform.SetParent(slot.Root.transform, false);
        var iconRect = iconGo.AddComponent<RectTransform>();
        iconRect.anchorMin = new Vector2(0.1f, 0.1f);
        iconRect.anchorMax = new Vector2(0.9f, 0.9f);
        iconRect.offsetMin = Vector2.zero;
        iconRect.offsetMax = Vector2.zero;
        slot.IconPlaceholder = iconGo.AddComponent<Image>();
        slot.IconPlaceholder.color = new Color(0.3f, 0.3f, 0.3f, 1f);

        // 冷却覆盖层
        var cdGo = new GameObject("CooldownOverlay");
        cdGo.transform.SetParent(slot.Root.transform, false);
        var cdRect = cdGo.AddComponent<RectTransform>();
        cdRect.anchorMin = Vector2.zero;
        cdRect.anchorMax = Vector2.one;
        cdRect.offsetMin = Vector2.zero;
        cdRect.offsetMax = Vector2.zero;
        slot.CooldownOverlay = cdGo.AddComponent<Image>();
        slot.CooldownOverlay.color = cooldownOverlayColor;
        slot.CooldownOverlay.fillMethod = Image.FillMethod.Radial360;
        slot.CooldownOverlay.fillOrigin = 2; // Top
        slot.CooldownOverlay.fillAmount = 0f;
        slot.CooldownOverlay.type = Image.Type.Filled;
        slot.CooldownOverlay.raycastTarget = false;

        // 冷却文字
        slot.CooldownText = CreateTMP(slot.Root.transform, "CooldownText", "", 22, Color.white);
        var ctRect = slot.CooldownText.GetComponent<RectTransform>();
        ctRect.anchorMin = Vector2.zero;
        ctRect.anchorMax = Vector2.one;
        ctRect.offsetMin = Vector2.zero;
        ctRect.offsetMax = Vector2.zero;

        // 描述信息面板（显示在技能槽上方）
        var descGo = new GameObject("Description");
        descGo.transform.SetParent(slot.Root.transform, false);
        var descRect = descGo.AddComponent<RectTransform>();
        descRect.anchorMin = new Vector2(0.5f, 1f);
        descRect.anchorMax = new Vector2(0.5f, 1f);
        descRect.pivot = new Vector2(0.5f, 1f);
        descRect.sizeDelta = new Vector2(slotSize * 1.5f, 36f);
        descRect.anchoredPosition = new Vector2(0, 4);

        var descLayout = descGo.AddComponent<VerticalLayoutGroup>();
        descLayout.childAlignment = TextAnchor.LowerCenter;
        descLayout.childControlHeight = false;
        descLayout.childControlWidth = false;
        descLayout.childForceExpandHeight = false;
        descLayout.childForceExpandWidth = true;
        descLayout.spacing = 0;

        // 按键提示
        slot.KeyText = CreateTMP(descGo.transform, "KeyText", keyName, 18, Color.white);
        var keyRect = slot.KeyText.GetComponent<RectTransform>();
        keyRect.sizeDelta = new Vector2(slotSize * 1.5f, 20);

        // 技能名称
        slot.NameText = CreateTMP(descGo.transform, "NameText", "", 14, new Color(0.8f, 0.8f, 0.8f));
        var nameRect = slot.NameText.GetComponent<RectTransform>();
        nameRect.sizeDelta = new Vector2(slotSize * 1.5f, 18);

        // 激活状态覆盖层
        var activeGo = new GameObject("ActiveOverlay");
        activeGo.transform.SetParent(slot.Root.transform, false);
        var activeRect = activeGo.AddComponent<RectTransform>();
        activeRect.anchorMin = Vector2.zero;
        activeRect.anchorMax = Vector2.one;
        activeRect.offsetMin = Vector2.zero;
        activeRect.offsetMax = Vector2.zero;
        slot.ActiveOverlay = activeGo.AddComponent<Image>();
        slot.ActiveOverlay.color = Color.clear;
        slot.ActiveOverlay.raycastTarget = false;

        // 持续时间条（底部）
        var durGo = new GameObject("DurationBar");
        durGo.transform.SetParent(slot.Root.transform, false);
        var durRect = durGo.AddComponent<RectTransform>();
        durRect.anchorMin = new Vector2(0f, 0f);
        durRect.anchorMax = new Vector2(1f, 0f);
        durRect.pivot = new Vector2(0f, 0f);
        durRect.sizeDelta = new Vector2(0, 4);
        durRect.anchoredPosition = new Vector2(0, -2);
        slot.DurationBar = durGo.AddComponent<Image>();
        slot.DurationBar.color = new Color(1f, 0.85f, 0f, 1f);
        slot.DurationBar.raycastTarget = false;
        slot.DurationBar.type = Image.Type.Filled;
        slot.DurationBar.fillMethod = Image.FillMethod.Horizontal;
        slot.DurationBar.fillAmount = 0f;
    }

    private void RefreshSlot(SlotUI slot, int abilityIndex)
    {
        var world = ClientECSWorld.Instance;
        if (world == null) return;

        var entity = world.GetLocalPlayerEntity();
        if (!world.EntityManager.IsValid(entity)) return;

        var em = world.EntityManager;
        if (!em.TryGetComponent<AbilityOwnerComponent>(entity, out var owner) || owner.GrantedAbilities == null)
        {
            SetSlotInactive(slot);
            return;
        }

        // HeroConfig.Abilities is a 4-element array: [Fire, Reload, QSkill, ESkill]
        if (abilityIndex < 0 || abilityIndex >= owner.GrantedAbilities.Count)
        {
            SetSlotInactive(slot);
            return;
        }

        var abilityCfg = owner.GrantedAbilities[abilityIndex];
        slot.NameText.text = abilityCfg.Name;

        // 读取运行时状态
        float cooldownRemaining = 0f;
        float durationRemaining = 0f;
        AbilityState state = AbilityState.Inactive;
        float maxCooldown = abilityCfg.Cooldown;
        float maxDuration = abilityCfg.Duration;

        if (em.TryGetComponent<AbilityInstanceComponent>(entity, out var instances))
        {
            for (int i = 0; i < 4; i++)
            {
                var inst = instances.GetSlot(i);
                if (inst.AssetId != abilityCfg.AssetId) continue;

                if (inst.IsActive)
                {
                    cooldownRemaining = inst.CooldownRemaining;
                    durationRemaining = inst.DurationRemaining;
                    state = inst.State;
                    break;
                }
                if (inst.CooldownRemaining > 0)
                {
                    cooldownRemaining = inst.CooldownRemaining;
                    break;
                }
            }
        }

        bool isOnCooldown = cooldownRemaining > 0f && state == AbilityState.Inactive;
        bool isActive = state == AbilityState.Active || state == AbilityState.Predicting;

        // 冷却覆盖层
        if (isOnCooldown && maxCooldown > 0f)
        {
            slot.CooldownOverlay.fillAmount = cooldownRemaining / maxCooldown;
            slot.CooldownText.text = cooldownRemaining > 1f
                ? $"{cooldownRemaining:F0}"
                : $"{cooldownRemaining:F1}";
        }
        else
        {
            slot.CooldownOverlay.fillAmount = 0f;
            slot.CooldownText.text = "";
        }

        // 激活状态高亮
        if (state == AbilityState.Predicting)
            slot.ActiveOverlay.color = predictingColor;
        else if (state == AbilityState.Active)
            slot.ActiveOverlay.color = activeColor;
        else
            slot.ActiveOverlay.color = Color.clear;

        // 持续时间条
        if (isActive && maxDuration > 0f)
            slot.DurationBar.fillAmount = durationRemaining / maxDuration;
        else
            slot.DurationBar.fillAmount = 0f;

        // 禁用态
        slot.Background.color = (isOnCooldown || isActive)
            ? new Color(0.1f, 0.1f, 0.1f, 0.6f)
            : normalSlotColor;
    }

    private void SetSlotInactive(SlotUI slot)
    {
        slot.NameText.text = "";
        slot.CooldownOverlay.fillAmount = 0f;
        slot.CooldownText.text = "";
        slot.ActiveOverlay.color = Color.clear;
        slot.DurationBar.fillAmount = 0f;
        slot.Background.color = normalSlotColor;
    }

    private TMP_Text CreateTMP(Transform parent, string name, string text, float fontSize, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        if (_font != null) tmp.font = _font;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        return tmp;
    }
}
