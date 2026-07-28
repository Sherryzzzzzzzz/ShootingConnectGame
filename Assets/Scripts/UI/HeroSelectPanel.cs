using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ShootingGame.Shared.Hero;

/// <summary>
/// 选角面板。匹配成功后显示，30 秒倒计时。
/// 角色卡片从 HeroRegistry 动态生成（从 Resources/Heroes/ 加载 HeroConfigSO）。
/// </summary>
public class HeroSelectPanel : MonoBehaviour
{
    [Header("UI 容器")]
    [SerializeField] private Transform _cardContainer;  // 卡片父节点
    [SerializeField] private GameObject _cardPrefab;     // 卡片模板（可选）
    [SerializeField] private TMP_Text _titleText;
    [SerializeField] private TMP_Text _timerText;
    [SerializeField] private TMP_Text _statusText;
    [SerializeField] private Button _confirmButton;
    [SerializeField] private GameObject _loadingOverlay;
    [SerializeField] private Color _selectedColor = Color.green;
    [SerializeField] private Color _normalColor = new Color(0.15f, 0.15f, 0.2f);

    [Header("卡片布局")]
    [SerializeField] private Vector2 _cardSize = new Vector2(180, 240);
    [SerializeField] private Vector2 _iconSize = new Vector2(80, 80);

    [Header("设置")]
    [SerializeField] private float _selectTimeout = 30f;

    private int _selectedHeroId = -1;
    private bool _confirmed;
    private float _timer;
    private bool _opponentConfirmed;

    private readonly Dictionary<int, Image> _cardHighlights = new Dictionary<int, Image>();

    public int SelectedHeroId => _selectedHeroId;
    public bool OpponentConfirmed => _opponentConfirmed;

    private void Awake()
    {
        if (_confirmButton != null) _confirmButton.onClick.AddListener(OnConfirmClick);
        PopulateCards();
    }

    /// <summary>
    /// 从 HeroRegistry 动态生成角色卡片。
    /// </summary>
    private void PopulateCards()
    {
        HeroRegistry.Initialize();
        if (_cardContainer == null) return;

        var font = Resources.Load<TMP_FontAsset>("Fonts/NotoSansCJK-Black-7 SDF");

        var heroes = HeroRegistry.GetAllHeroes();
        if (heroes.Count == 0)
        {
            Debug.LogWarning("[HeroSelectPanel] HeroRegistry 中没有英雄配置！");
            return;
        }

        foreach (var hero in heroes)
        {
            var card = CreateHeroCard(hero, font);
            card.transform.SetParent(_cardContainer, false);
        }
    }

    private GameObject CreateHeroCard(HeroConfig hero, TMP_FontAsset font)
    {
        var card = new GameObject($"HeroCard_{hero.HeroId}");
        var rt = card.AddComponent<RectTransform>();
        rt.sizeDelta = _cardSize;

        var img = card.AddComponent<Image>();
        img.color = _normalColor;
        _cardHighlights[hero.HeroId] = img;

        var layout = card.AddComponent<LayoutElement>();
        layout.preferredWidth = _cardSize.x;
        layout.preferredHeight = _cardSize.y;

        var vlg = card.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.spacing = 6;
        vlg.padding = new RectOffset(10, 10, 14, 10);

        // --- 头像 ---
        if (hero.HeroIcon != null)
        {
            var iconGo = new GameObject("Icon");
            iconGo.transform.SetParent(card.transform, false);
            var iconImg = iconGo.AddComponent<Image>();
            iconImg.sprite = hero.HeroIcon;
            iconImg.preserveAspect = true;
            var iconRt = iconGo.GetComponent<RectTransform>();
            iconRt.sizeDelta = _iconSize;

            var iconLayout = iconGo.AddComponent<LayoutElement>();
            iconLayout.preferredWidth = _iconSize.x;
            iconLayout.preferredHeight = _iconSize.y;
        }
        else
        {
            // 无头像时显示占位色块
            var placeholderGo = new GameObject("IconPlaceholder");
            placeholderGo.transform.SetParent(card.transform, false);
            var placeholderImg = placeholderGo.AddComponent<Image>();
            placeholderImg.color = new Color(0.3f, 0.3f, 0.4f);
            var placeholderRt = placeholderGo.GetComponent<RectTransform>();
            placeholderRt.sizeDelta = _iconSize;

            var placeholderLayout = placeholderGo.AddComponent<LayoutElement>();
            placeholderLayout.preferredWidth = _iconSize.x;
            placeholderLayout.preferredHeight = _iconSize.y;
        }

        // --- 名字 ---
        var nameGo = new GameObject("Name");
        nameGo.transform.SetParent(card.transform, false);
        var nameText = nameGo.AddComponent<TextMeshProUGUI>();
        nameText.text = hero.Name;
        nameText.fontSize = 20;
        nameText.alignment = TextAlignmentOptions.Center;
        nameText.color = Color.white;
        if (font != null) nameText.font = font;

        // --- 描述（HP + 枪械） ---
        var gunName = hero.StartingGun != null ? hero.StartingGun.GunName : "无";
        var descGo = new GameObject("Desc");
        descGo.transform.SetParent(card.transform, false);
        var descText = descGo.AddComponent<TextMeshProUGUI>();
        descText.text = $"HP: {hero.MaxHP}\n{gunName}";
        descText.fontSize = 13;
        descText.alignment = TextAlignmentOptions.Center;
        descText.color = new Color(0.7f, 0.7f, 0.7f);
        if (font != null) descText.font = font;

        // --- 速度 ---
        var speedGo = new GameObject("Speed");
        speedGo.transform.SetParent(card.transform, false);
        var speedText = speedGo.AddComponent<TextMeshProUGUI>();
        speedText.text = $"速度: {hero.MoveSpeed:F1}";
        speedText.fontSize = 11;
        speedText.alignment = TextAlignmentOptions.Center;
        speedText.color = new Color(0.5f, 0.5f, 0.6f);
        if (font != null) speedText.font = font;

        // --- 按钮 ---
        var btn = card.AddComponent<Button>();
        var heroId = hero.HeroId;
        btn.onClick.AddListener(() => SelectHero(heroId));

        return card;
    }

    private void OnEnable()
    {
        _selectedHeroId = -1;
        _confirmed = false;
        _opponentConfirmed = false;
        _timer = _selectTimeout;

        if (_confirmButton != null) _confirmButton.interactable = false;
        if (_loadingOverlay != null) _loadingOverlay.SetActive(false);

        // 重置所有卡片高亮
        foreach (var kv in _cardHighlights) kv.Value.color = _normalColor;

        UpdateUI();

        if (LobbyClient.Instance != null)
        {
            LobbyClient.Instance.OnHeroSelected += OnOpponentHeroSelected;
            LobbyClient.Instance.OnHeroConfirmed += OnOpponentHeroConfirmed;
        }
    }

    private void OnDisable()
    {
        if (LobbyClient.Instance != null)
        {
            LobbyClient.Instance.OnHeroSelected -= OnOpponentHeroSelected;
            LobbyClient.Instance.OnHeroConfirmed -= OnOpponentHeroConfirmed;
        }
    }

    private void Update()
    {
        if (_confirmed) return;

        _timer -= Time.deltaTime;
        if (_timer <= 0f)
        {
            _timer = 0f;
            if (_selectedHeroId < 0) SelectHero(HeroRegistry.DefaultHeroId);
            ConfirmSelection();
        }
        if (_timerText != null) _timerText.text = $"{_timer:F0}";
    }

    private void SelectHero(int heroId)
    {
        if (_confirmed) return;
        _selectedHeroId = heroId;

        foreach (var kv in _cardHighlights)
            kv.Value.color = kv.Key == heroId ? _selectedColor : _normalColor;

        if (_confirmButton != null) _confirmButton.interactable = true;
        LobbyClient.Instance?.SendHeroSelected(heroId);
    }

    private void OnConfirmClick()
    {
        if (_selectedHeroId > 0 && !_confirmed) ConfirmSelection();
    }

    private void ConfirmSelection()
    {
        if (_confirmed) return;
        _confirmed = true;
        if (_confirmButton != null) _confirmButton.interactable = false;
        LobbyClient.Instance?.SendHeroConfirmed(_selectedHeroId);
        UpdateUI();
    }

    private void OnOpponentHeroSelected(int heroId)
    {
        if (_statusText != null) _statusText.text = $"对手选择了英雄 #{heroId}";
    }

    private void OnOpponentHeroConfirmed()
    {
        _opponentConfirmed = true;
        UpdateUI();
    }

    private void UpdateUI()
    {
        if (_statusText == null) return;
        if (_confirmed && _opponentConfirmed) _statusText.text = "双方已锁定，准备进入战斗...";
        else if (_confirmed) _statusText.text = "等待对手锁定...";
        else _statusText.text = "请选择你的英雄";
    }
}
