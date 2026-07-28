using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ShootingGame.Shared.Hero;

/// <summary>
/// 选角场景控制器。在现有 3D 场景基础上叠加 Canvas UI，
/// 左侧选角/服装/枪色，右侧 3D 预览。
/// </summary>
public class HeroSelectController : MonoBehaviour
{
    [Header("预览")]
    [SerializeField] private CharacterPreviewController _preview;
    [SerializeField] private Transform _previewSpawnPoint;

    [Header("UI 按钮模板（Prefab）")]
    [SerializeField] private Button _charBtnTemplate;   // 角色按钮模板
    [SerializeField] private Button _colorBtnTemplate;  // 颜色按钮模板
    [SerializeField] private Transform _charBtnParent;   // 角色按钮父节点
    [SerializeField] private Transform _colorBtnParent;  // 颜色按钮父节点

    [Header("外观切换按钮")]
    [SerializeField] private Button _outfitPrevBtn;
    [SerializeField] private Button _outfitNextBtn;

    [Header("文本")]
    [SerializeField] private Text _outfitLabel;
    [SerializeField] private Text _heroInfoLabel;

    [Header("操作")]
    [SerializeField] private Button _confirmBtn;
    [SerializeField] private string _fightScene = "Fight";

    private List<HeroConfig> _heroes;
    private int _outfitIdx;
    private HeroConfig _selectedHero;

    private readonly Color[] _gunColors =
    {
        new Color(0.3f, 0.3f, 0.3f),  // 深灰
        Color.black,
        new Color(0.7f, 0.15f, 0.1f), // 红
        new Color(0.1f, 0.3f, 0.7f),  // 蓝
        new Color(0.1f, 0.6f, 0.2f),  // 绿
        new Color(0.8f, 0.6f, 0.1f),  // 金
    };

    private void Start()
    {
        HeroRegistry.Initialize();
        _heroes = HeroRegistry.GetAllHeroes();
        if (_heroes.Count == 0) return;

        BuildCharButtons();
        BuildColorButtons();

        if (_outfitPrevBtn) _outfitPrevBtn.onClick.AddListener(() => CycleOutfit(-1));
        if (_outfitNextBtn) _outfitNextBtn.onClick.AddListener(() => CycleOutfit(1));
        if (_confirmBtn) _confirmBtn.onClick.AddListener(OnConfirm);

        SelectHero(0);
    }

    private void BuildCharButtons()
    {
        if (_charBtnTemplate == null || _charBtnParent == null) return;

        for (int i = 0; i < _heroes.Count; i++)
        {
            int idx = i;
            var btn = i == 0 ? _charBtnTemplate : Instantiate(_charBtnTemplate, _charBtnParent);
            btn.gameObject.SetActive(true);

            var label = btn.GetComponentInChildren<Text>();
            if (label != null) label.text = _heroes[i].Name;

            btn.onClick.AddListener(() => SelectHero(idx));
        }
        _charBtnTemplate.gameObject.SetActive(false); // 模板隐藏
    }

    private void BuildColorButtons()
    {
        if (_colorBtnTemplate == null || _colorBtnParent == null) return;

        for (int i = 0; i < _gunColors.Length; i++)
        {
            int idx = i;
            var btn = i == 0 ? _colorBtnTemplate : Instantiate(_colorBtnTemplate, _colorBtnParent);
            btn.gameObject.SetActive(true);

            var img = btn.GetComponent<Image>();
            if (img != null) img.color = _gunColors[i];

            btn.onClick.AddListener(() => SetGunColor(idx));
        }
        _colorBtnTemplate.gameObject.SetActive(false);
    }

    private void SelectHero(int index)
    {
        if (index < 0 || index >= _heroes.Count) return;
        _selectedHero = _heroes[index];
        _outfitIdx = 0;

        if (_preview != null && _previewSpawnPoint != null)
            _preview.ShowHero(_selectedHero, _previewSpawnPoint.position);

        if (_heroInfoLabel != null)
            _heroInfoLabel.text = $"{_selectedHero.Name}\nHP:{_selectedHero.MaxHP}  速度:{_selectedHero.MoveSpeed:F1}";
        if (_outfitLabel != null)
            _outfitLabel.text = _preview != null ? _preview.GetOutfitName(0) : "默认";
    }

    private void CycleOutfit(int delta)
    {
        if (_preview == null || _preview.OutfitCount <= 1) return;
        _outfitIdx = (_outfitIdx + delta + _preview.OutfitCount) % _preview.OutfitCount;
        _preview.SwitchOutfit(_outfitIdx);
        if (_outfitLabel != null) _outfitLabel.text = _preview.GetOutfitName(_outfitIdx);
    }

    private void SetGunColor(int index)
    {
        if (index < 0 || index >= _gunColors.Length) return;
        _preview?.SetGunColor(_gunColors[index]);
        CharacterSelectUI.SelectedGunColor = _gunColors[index];
    }

    private void OnConfirm()
    {
        if (_selectedHero != null)
        {
            CharacterSelectUI.SelectedHeroId = _selectedHero.HeroId;
            CharacterSelectUI.SelectedOutfitIndex = _outfitIdx;
        }
        if (LobbyClient.Instance != null)
            LobbyClient.Instance.SendHeroSelected(_selectedHero?.HeroId ?? 1);
        UnityEngine.SceneManagement.SceneManager.LoadScene(_fightScene);
    }
}
