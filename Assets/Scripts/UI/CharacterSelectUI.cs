using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using ShootingGame.Shared.Hero;

/// <summary>
/// 选人界面控制器。管理角色选择、服装切换、枪械颜色、确认进入战斗。
/// 挂到 HeroSelectScene 的 Canvas 上。
/// </summary>
public class CharacterSelectUI : MonoBehaviour
{
    [Header("角色预览")]
    [SerializeField] private CharacterPreviewController _preview;
    [SerializeField] private Transform _previewSpawnPoint; // 预览模型生成位置

    [Header("UI - 角色选择")]
    [SerializeField] private Button[] _heroButtons;        // 每个角色一个按钮
    [SerializeField] private Text _heroNameText;
    [SerializeField] private Text _heroDescText;

    [Header("UI - 服装")]
    [SerializeField] private Button _outfitPrevBtn;
    [SerializeField] private Button _outfitNextBtn;
    [SerializeField] private Text _outfitNameText;

    [Header("UI - 枪械颜色")]
    [SerializeField] private Button[] _gunColorBtns;       // 预设颜色按钮
    [SerializeField] private Color[] _gunPresetColors = new[]
    {
        Color.gray, Color.black, new Color(0.8f, 0.2f, 0.1f), // 红
        new Color(0.1f, 0.3f, 0.8f), // 蓝
        new Color(0.1f, 0.7f, 0.2f), // 绿
        new Color(0.9f, 0.7f, 0.1f), // 金
    };

    [Header("UI - 操作")]
    [SerializeField] private Button _confirmBtn;
    [SerializeField] private Button _backBtn;
    [SerializeField] private string _fightSceneName = "Fight";

    // 状态
    private int _selectedHeroIndex;
    private int _selectedOutfitIndex;
    private Color _selectedGunColor = Color.gray;
    private List<HeroConfig> _heroes;

    // 选中结果（进入战斗时使用）
    public static int SelectedHeroId { get; set; } = HeroRegistry.DefaultHeroId;
    public static int SelectedOutfitIndex { get; set; }
    public static Color SelectedGunColor { get; set; } = Color.gray;

    private void Start()
    {
        HeroRegistry.Initialize();
        _heroes = HeroRegistry.GetAllHeroes();
        if (_heroes.Count == 0) { Debug.LogError("[SelectUI] 没有英雄配置！"); return; }

        // 绑定按钮事件
        for (int i = 0; i < _heroButtons.Length && i < _heroes.Count; i++)
        {
            int idx = i;
            _heroButtons[i].onClick.AddListener(() => SelectHero(idx));
        }

        if (_outfitPrevBtn) _outfitPrevBtn.onClick.AddListener(() => CycleOutfit(-1));
        if (_outfitNextBtn) _outfitNextBtn.onClick.AddListener(() => CycleOutfit(1));

        for (int i = 0; i < _gunColorBtns.Length && i < _gunPresetColors.Length; i++)
        {
            int idx = i;
            _gunColorBtns[i].onClick.AddListener(() => SelectGunColor(idx));
            // 按钮背景显示颜色
            var img = _gunColorBtns[i].GetComponent<Image>();
            if (img != null) img.color = _gunPresetColors[i];
        }

        if (_confirmBtn) _confirmBtn.onClick.AddListener(OnConfirm);
        if (_backBtn) _backBtn.onClick.AddListener(() => SceneManager.LoadScene("StartScene"));

        // 默认选中第一个英雄
        SelectHero(0);
    }

    private void SelectHero(int index)
    {
        if (index < 0 || index >= _heroes.Count) return;
        _selectedHeroIndex = index;
        var hero = _heroes[index];

        if (_heroNameText) _heroNameText.text = hero.Name;
        if (_heroDescText) _heroDescText.text = $"HP:{hero.MaxHP} 速度:{hero.MoveSpeed:F1}";

        // 高亮选中按钮
        for (int i = 0; i < _heroButtons.Length; i++)
        {
            if (_heroButtons[i] == null) continue;
            _heroButtons[i].interactable = (i != index);
        }

        // 生成预览模型（不播动画）
        if (_preview != null && _previewSpawnPoint != null)
            _preview.ShowHero(hero, _previewSpawnPoint.position);

        _selectedOutfitIndex = 0;
        UpdateOutfitDisplay();
    }

    private void CycleOutfit(int delta)
    {
        if (_preview == null) return;
        int max = _preview.OutfitCount;
        if (max <= 1) return;
        _selectedOutfitIndex = (_selectedOutfitIndex + delta + max) % max;
        UpdateOutfitDisplay();
        _preview.SwitchOutfit(_selectedOutfitIndex);
    }

    private void UpdateOutfitDisplay()
    {
        if (_outfitNameText)
            _outfitNameText.text = _preview != null ? _preview.GetOutfitName(_selectedOutfitIndex) : "默认";
    }

    private void SelectGunColor(int index)
    {
        if (index < 0 || index >= _gunPresetColors.Length) return;
        _selectedGunColor = _gunPresetColors[index];
        _preview?.SetGunColor(_selectedGunColor);

        // 高亮选中颜色
        for (int i = 0; i < _gunColorBtns.Length; i++)
        {
            if (_gunColorBtns[i] == null) continue;
            var outline = _gunColorBtns[i].GetComponent<Outline>();
            if (outline != null) outline.enabled = (i == index);
        }
    }

    private void OnConfirm()
    {
        if (_heroes == null || _selectedHeroIndex >= _heroes.Count) return;

        SelectedHeroId = _heroes[_selectedHeroIndex].HeroId;
        SelectedOutfitIndex = _selectedOutfitIndex;
        SelectedGunColor = _selectedGunColor;

        Debug.Log($"[SelectUI] 确认选择: Hero={SelectedHeroId} Outfit={SelectedOutfitIndex} GunColor={_selectedGunColor}");

        // 通知大厅服务器选角
        if (LobbyClient.Instance != null)
            LobbyClient.Instance.SendHeroSelected(SelectedHeroId);

        // 加载战斗场景
        SceneManager.LoadScene(_fightSceneName);
    }
}
