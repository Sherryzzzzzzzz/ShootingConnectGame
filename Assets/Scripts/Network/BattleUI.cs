using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using TMPro;
using ShootingGame.Shared.ECS;
using ShootingGame.Shared.Protocol;
using ShootingGame.Shared.Simulation;

/// <summary>
/// 战斗 UI 管理器。负责血条、准星、击杀提示、游戏结束界面等。
/// </summary>
public class BattleUI : MonoBehaviour
{
    [Header("血条")]
    [SerializeField] private Slider healthBar;
    [SerializeField] private TMP_Text healthText;
    [SerializeField] private Image healthBarFill;
    [SerializeField] private Gradient healthColorGradient;

    [Header("弹药")]
    [SerializeField] private TMP_Text ammoText;
    [SerializeField] private TMP_Text reloadText;

    [Header("准星")]
    [SerializeField] private Image crosshair;
    [SerializeField] private Color crosshairNormalColor = Color.white;
    [SerializeField] private Color crosshairHitColor = Color.red;
    [SerializeField] private float crosshairHitFlashDuration = 0.1f;

    [Header("击杀信息")]
    [SerializeField] private Transform killFeedContainer;
    [SerializeField] private GameObject killFeedItemPrefab;
    [SerializeField] private int maxKillFeedItems = 5;
    [SerializeField] private float killFeedDuration = 5f;

    [Header("游戏结束")]
    [SerializeField] private GameObject gameOverPanel;
    [SerializeField] private TMP_Text gameOverTitle;
    [SerializeField] private TMP_Text gameOverSubtitle;
    [SerializeField] private Button returnToLobbyButton;
    [SerializeField] private string victoryText = "胜利！";
    [SerializeField] private string defeatText = "失败...";

    [Header("匹配界面")]
    [SerializeField] private GameObject matchingPanel;
    [SerializeField] private TMP_Text matchingStatusText;
    [SerializeField] private Button cancelMatchingButton;

    [Header("计分")]
    [SerializeField] private TMP_Text killsText;
    [SerializeField] private TMP_Text deathsText;

    [Header("网络状态")]
    [SerializeField] private TMP_Text pingText;
    [SerializeField] private TMP_Text fpsText;

    // 记分板（Tab）
    private GameObject scoreboardPanel;
    private Transform scoreboardContainer;
    private readonly List<TMP_Text> _scoreboardRows = new List<TMP_Text>();

    // 击杀横幅（爆头/首杀）
    private TMP_Text killBannerText;
    private float _killBannerTimer;
    private const float KillBannerDuration = 2.5f;
    private bool _firstBloodHappened;

    // 状态
    private int _currentHp = 100;
    private int _maxHp = 100;
    private int _kills;
    private int _deaths;
    private float _crosshairHitTimer;
    private readonly Queue<GameObject> _killFeedItems = new Queue<GameObject>();
    private TMP_FontAsset _cjkFallbackFont;

    // 单例
    public static BattleUI Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        // 如果未通过预制体赋值，自动创建 UI
        if (healthBar == null)
            CreateDefaultUI();

        // 初始化 UI 状态
        if (gameOverPanel != null) gameOverPanel.SetActive(false);
        if (matchingPanel != null) matchingPanel.SetActive(false);

        // 订阅事件
        if (BattleManager.Instance != null)
        {
            BattleManager.Instance.OnMatchingStart += ShowMatchingUI;
            BattleManager.Instance.OnBattleStart += OnBattleStartHandler;
            BattleManager.Instance.OnGameOver += ShowGameOverUI;
            BattleManager.Instance.OnStateChanged += OnStateChanged;
        }

        if (HitEventView.Instance != null)
        {
            HitEventView.Instance.OnHitEvent += OnHitEvent;
        }

        if (AuthoritySync.Instance != null)
        {
            AuthoritySync.Instance.OnPlayerHpChanged += OnHpChanged;
        }

        // 按钮事件
        if (cancelMatchingButton != null)
        {
            cancelMatchingButton.onClick.AddListener(OnCancelMatchingClick);
        }

        if (returnToLobbyButton != null)
        {
            returnToLobbyButton.onClick.AddListener(OnReturnToLobbyClick);
        }

        UpdateHealthDisplay();
    }

    private void OnDestroy()
    {
        if (BattleManager.Instance != null)
        {
            BattleManager.Instance.OnMatchingStart -= ShowMatchingUI;
            BattleManager.Instance.OnBattleStart -= OnBattleStartHandler;
            BattleManager.Instance.OnGameOver -= ShowGameOverUI;
            BattleManager.Instance.OnStateChanged -= OnStateChanged;
        }

        if (HitEventView.Instance != null)
        {
            HitEventView.Instance.OnHitEvent -= OnHitEvent;
        }

        if (AuthoritySync.Instance != null)
        {
            AuthoritySync.Instance.OnPlayerHpChanged -= OnHpChanged;
        }
    }

    private void Update()
    {
        // 更新准星闪烁
        if (_crosshairHitTimer > 0)
        {
            _crosshairHitTimer -= Time.deltaTime;
            if (_crosshairHitTimer <= 0 && crosshair != null)
            {
                crosshair.color = crosshairNormalColor;
            }
        }

        // 击杀横幅淡出
        if (_killBannerTimer > 0)
        {
            _killBannerTimer -= Time.deltaTime;
            if (_killBannerTimer <= 0 && killBannerText != null)
                killBannerText.gameObject.SetActive(false);
        }

        // Tab 记分板（按住显示）。安全访问 Keyboard，防止 InputSystem 未初始化时抛异常
        if (scoreboardPanel != null && BattleManager.Instance != null &&
            BattleManager.Instance.State == BattleManager.BattleState.Playing)
        {
            bool show = false;
            try
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                show = kb != null && kb.tabKey.isPressed;
            }
            catch { /* InputSystem 未初始化——忽略 */ }

            if (show != scoreboardPanel.activeSelf)
            {
                scoreboardPanel.SetActive(show);
                if (show) RefreshScoreboard();
            }
            else if (show)
            {
                RefreshScoreboard();
            }
        }

        // 更新网络状态显示
        UpdateNetworkStatus();

        // 更新弹药显示
        UpdateAmmoDisplay();

        // 准星扩散
        UpdateCrosshairBloom();

        // 服务器权威 K/D 节流刷新
        _scoreRefreshTimer += Time.deltaTime;
        if (_scoreRefreshTimer >= 0.5f)
        {
            _scoreRefreshTimer = 0f;
            UpdateScoreDisplay();
        }

        // 更新 FPS
        UpdateFPS();
    }

    private float _scoreRefreshTimer;

    #region 血条

    public void SetHealth(int hp, int maxHp = 100)
    {
        _currentHp = Mathf.Max(0, hp);
        _maxHp = maxHp;
        UpdateHealthDisplay();
    }

    private void OnHpChanged(int playerId, int newHp)
    {
        Debug.Log($"[BattleUI] OnHpChanged: playerId={playerId} newHp={newHp} localPlayerId={BattleClient.Instance?.BattlePlayerId}");
        // 只更新本地玩家的血条
        if (BattleClient.Instance != null && playerId == BattleClient.Instance.BattlePlayerId)
        {
            SetHealth(newHp);
        }
    }

    private void UpdateHealthDisplay()
    {
        if (healthBar != null)
        {
            healthBar.maxValue = _maxHp;
            healthBar.value = _currentHp;
        }

        if (healthText != null)
        {
            healthText.text = $"{_currentHp}/{_maxHp}";
        }

        if (healthBarFill != null && healthColorGradient != null)
        {
            float t = (float)_currentHp / _maxHp;
            healthBarFill.color = healthColorGradient.Evaluate(t);
        }
    }

    #endregion

    #region 弹药

    private void UpdateAmmoDisplay()
    {
        int currentAmmo = 30; // 回退值，实际由 ECS AmmoComponent 覆盖
        int maxAmmo = 30;
        bool isReloading = false;

        // Read from local player's ECS entity for real-time prediction
        var world = ClientECSWorld.Instance;
        if (world != null)
        {
            var entity = world.GetLocalPlayerEntity();
            if (world.EntityManager.IsValid(entity))
            {
                if (world.EntityManager.TryGetComponent<AmmoComponent>(entity, out var ammo))
                {
                    currentAmmo = ammo.Current;
                    maxAmmo = ammo.Max;
                }

                if (world.EntityManager.TryGetComponent<ReloadComponent>(entity, out var reload))
                {
                    isReloading = reload.IsReloading;
                }
            }
        }

        if (ammoText != null)
        {
            ammoText.text = $"{currentAmmo}/{maxAmmo}";
            ammoText.color = currentAmmo == 0 ? Color.red :
                             currentAmmo <= 5 ? Color.yellow : Color.white;
        }

        if (reloadText != null)
        {
            reloadText.gameObject.SetActive(isReloading);
        }
    }

    #endregion

    #region 计分

    private void UpdateScoreDisplay()
    {
        // 优先用服务器权威 K/D（最新帧），回退到本地计数
        int kills = _kills, deaths = _deaths;
        var frame = BattleClient.Instance != null ? BattleClient.Instance.GetLatestFrame() : null;
        if (frame?.PlayerStates != null)
        {
            foreach (var ps in frame.PlayerStates)
            {
                if (ps.PlayerId == BattleClient.Instance.BattlePlayerId)
                {
                    kills = ps.Kills;
                    deaths = ps.Deaths;
                    break;
                }
            }
        }

        if (killsText != null)
        {
            killsText.text = $"击杀: {kills}";
        }

        if (deathsText != null)
        {
            deathsText.text = $"死亡: {deaths}";
        }
    }

    #endregion

    #region 准星

    public void OnHitEnemy()
    {
        if (crosshair == null) return;

        crosshair.color = crosshairHitColor;
        _crosshairHitTimer = crosshairHitFlashDuration;
    }

    // 准星扩散：随当前散射角放大（Valorant 式 bloom 反馈）
    private float _playerFindTimer;

    private void UpdateCrosshairBloom()
    {
        if (crosshair == null) return;

        float spread = ClientECSWorld.Instance != null ? ClientECSWorld.Instance.CurrentSpreadDeg : 0f;
        float scale = 1f + spread * 0.15f;
        crosshair.rectTransform.localScale = new Vector3(scale, scale, 1f);
    }

    #endregion

    #region 击杀信息

    private void OnHitEvent(HitEventMsg hitEvent)
    {
        // 检查是否是击杀
        if (!hitEvent.IsKill) return;

        // 检查是否涉及本地玩家
        bool isLocalKiller = BattleClient.Instance != null && hitEvent.AttackerId == BattleClient.Instance.BattlePlayerId;
        bool isLocalVictim = BattleClient.Instance != null && hitEvent.VictimId == BattleClient.Instance.BattlePlayerId;
        bool isHeadshot = hitEvent.BodyPart == 1;

        // 更新计分
        if (isLocalKiller)
        {
            _kills++;
            OnHitEnemy();

            // 击杀横幅：首杀 > 爆头
            if (!_firstBloodHappened)
            {
                ShowKillBanner("首杀！", new Color(1f, 0.5f, 0.2f));
            }
            else if (isHeadshot)
            {
                ShowKillBanner("爆头击杀！", new Color(1f, 0.85f, 0.2f));
            }
        }
        if (isLocalVictim)
        {
            _deaths++;
        }
        _firstBloodHappened = true;
        UpdateScoreDisplay();

        // 显示击杀信息
        string killerName = GetPlayerName(hitEvent.AttackerId);
        string victimName = GetPlayerName(hitEvent.VictimId);

        AddKillFeedItem(killerName, victimName, isLocalKiller, isLocalVictim, isHeadshot);
    }

    private void AddKillFeedItem(string killer, string victim, bool isLocalKiller, bool isLocalVictim, bool isHeadshot = false)
    {
        if (killFeedContainer == null || killFeedItemPrefab == null) return;

        // 创建击杀信息条目
        var item = Instantiate(killFeedItemPrefab, killFeedContainer);
        var text = item.GetComponent<TMP_Text>();

        if (text != null)
        {
            string colorTag = isLocalKiller ? "<color=green>" : "<color=red>";
            string endTag = "</color>";
            string headshotTag = isHeadshot ? "<color=#ffd94d>[爆头] </color>" : "";
            text.text = $"{headshotTag}{colorTag}{killer}{endTag} 击杀了 {colorTag}{victim}{endTag}";
        }

        _killFeedItems.Enqueue(item);

        // 限制数量
        while (_killFeedItems.Count > maxKillFeedItems)
        {
            var old = _killFeedItems.Dequeue();
            if (old != null) Destroy(old);
        }

        // 延迟销毁
        Destroy(item, killFeedDuration);
    }

    private string GetPlayerName(int playerId)
    {
        if (BattleClient.Instance != null)
            return BattleClient.Instance.GetPlayerName(playerId);
        return $"玩家{playerId}";
    }

    #endregion

    #region 记分板

    /// <summary>刷新 Tab 记分板：从服务器最新帧取各玩家 K/D（服务器权威），按击杀降序。</summary>
    private void RefreshScoreboard()
    {
        if (scoreboardContainer == null) return;

        var entries = new List<(string name, int kills, int deaths, bool isLocal)>();
        var frame = BattleClient.Instance != null ? BattleClient.Instance.GetLatestFrame() : null;
        if (frame?.PlayerStates != null)
        {
            foreach (var ps in frame.PlayerStates)
            {
                bool isLocal = BattleClient.Instance != null && ps.PlayerId == BattleClient.Instance.BattlePlayerId;
                entries.Add((GetPlayerName(ps.PlayerId), ps.Kills, ps.Deaths, isLocal));
            }
        }
        entries.Sort((a, b) => b.kills != a.kills ? b.kills - a.kills : a.deaths - b.deaths);
        FillScoreboardRows(entries);
    }

    /// <summary>用给定数据填充记分板行（行不够则新建）。</summary>
    private void FillScoreboardRows(List<(string name, int kills, int deaths, bool isLocal)> entries)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            TMP_Text row;
            if (i < _scoreboardRows.Count)
            {
                row = _scoreboardRows[i];
            }
            else
            {
                row = CreateTMPText(scoreboardContainer, $"Row{i}", "", _cjkFallbackFont, 20, Color.white, TextAlignmentOptions.Left);
                var rect = row.GetComponent<RectTransform>();
                rect.sizeDelta = new Vector2(460, 26);
                _scoreboardRows.Add(row);
            }

            var e = entries[i];
            string medal = i == 0 ? "★ " : $"{i + 1}. ";
            row.text = $"{medal}{e.name}    <color=#7f7>{e.kills}</color> / <color=#f77>{e.deaths}</color>";
            row.color = e.isLocal ? new Color(1f, 0.9f, 0.4f) : Color.white;
            row.gameObject.SetActive(true);
        }
        for (int i = entries.Count; i < _scoreboardRows.Count; i++)
            _scoreboardRows[i].gameObject.SetActive(false);
    }

    /// <summary>展示最终记分板（GameOver 时调用，不依赖 Tab）。</summary>
    private void ShowFinalScoreboard()
    {
        if (scoreboardPanel == null) return;

        var entries = new List<(string name, int kills, int deaths, bool isLocal)>();
        var board = BattleClient.Instance != null ? BattleClient.Instance.LastScoreboard : null;
        if (board != null && board.Count > 0)
        {
            foreach (var se in board)
            {
                bool isLocal = BattleClient.Instance != null && se.PlayerId == BattleClient.Instance.BattlePlayerId;
                entries.Add((se.PlayerName ?? GetPlayerName(se.PlayerId), se.Kills, se.Deaths, isLocal));
            }
        }
        else
        {
            // 老服务器无记分板数据：回退到最新帧
            RefreshScoreboard();
            scoreboardPanel.SetActive(true);
            return;
        }

        FillScoreboardRows(entries);
        scoreboardPanel.SetActive(true);
    }

    #endregion

    #region 击杀横幅

    private void ShowKillBanner(string text, Color color)
    {
        if (killBannerText == null) return;
        killBannerText.text = text;
        killBannerText.color = color;
        killBannerText.gameObject.SetActive(true);
        _killBannerTimer = KillBannerDuration;
    }

    #endregion

    #region 游戏结束

    private void ShowGameOverUI(int winnerId)
    {
        if (gameOverPanel == null) return;

        int mode = BattleClient.Instance != null ? BattleClient.Instance.LastGameOverMode : 0;
        bool isVictory;
        string subtitle;

        if (mode == 1)
        {
            // 死斗：winnerId 是胜者 bpId
            isVictory = BattleClient.Instance != null && BattleClient.Instance.BattlePlayerId == winnerId;
            string winnerName = BattleClient.Instance != null ? BattleClient.Instance.GetPlayerName(winnerId) : $"玩家{winnerId}";
            subtitle = isVictory ? "你是本场最佳！" : $"{winnerName} 拿下了本场最佳";
        }
        else
        {
            // 团队模式：winnerId 是队伍 ID
            isVictory = BattleClient.Instance != null && BattleClient.Instance.TeamId == winnerId;
            subtitle = $"胜利队伍: {winnerId}";
        }

        if (gameOverTitle != null)
        {
            gameOverTitle.text = isVictory ? victoryText : defeatText;
            gameOverTitle.color = isVictory ? Color.green : Color.red;
        }

        if (gameOverSubtitle != null)
        {
            gameOverSubtitle.text = subtitle;
        }

        gameOverPanel.SetActive(true);

        // 结算时展示最终记分板
        ShowFinalScoreboard();
    }

    private void HideGameOverUI()
    {
        if (gameOverPanel != null)
        {
            gameOverPanel.SetActive(false);
        }
        if (scoreboardPanel != null)
        {
            scoreboardPanel.SetActive(false);
        }
    }

    #endregion

    #region 匹配界面

    private void OnBattleStartHandler()
    {
        HideMatchingUI();
        HideGameOverUI();
        // 重置血量为满血
        _currentHp = 100;
        _maxHp = 100; // 运行时由 OnHpChanged/SetHealth 更新为英雄真实血量
        _kills = 0;
        _deaths = 0;
        _firstBloodHappened = false;
        UpdateHealthDisplay();
        UpdateScoreDisplay();
    }

    private void ShowMatchingUI()
    {
        if (matchingPanel != null)
        {
            matchingPanel.SetActive(true);
        }

        if (matchingStatusText != null)
        {
            matchingStatusText.text = "正在匹配...";
        }
    }

    private void HideMatchingUI()
    {
        if (matchingPanel != null)
        {
            matchingPanel.SetActive(false);
        }
    }

    private void OnCancelMatchingClick()
    {
        if (BattleManager.Instance != null)
        {
            BattleManager.Instance.CancelMatching();
        }
        HideMatchingUI();
    }

    private void OnReturnToLobbyClick()
    {
        HideGameOverUI();

        if (BattleManager.Instance != null)
        {
            BattleManager.Instance.EndBattle();
        }
    }

    #endregion

    #region 状态变化

    private void OnStateChanged(BattleManager.BattleState state)
    {
        switch (state)
        {
            case BattleManager.BattleState.None:
                HideMatchingUI();
                HideGameOverUI();
                // 返回大厅时隐藏整个战斗 UI
                var canvas = GetComponentInChildren<Canvas>(includeInactive: true);
                if (canvas != null) canvas.gameObject.SetActive(false);
                break;

            case BattleManager.BattleState.Playing:
                // 确保战斗 UI 可见
                var c = GetComponentInChildren<Canvas>(includeInactive: true);
                if (c != null) c.gameObject.SetActive(true);
                break;

            case BattleManager.BattleState.GameOver:
                // 游戏结束 UI 由 OnGameOver 事件处理
                break;
        }
    }

    #endregion

    #region 网络状态

    private void UpdateNetworkStatus()
    {
        if (pingText != null && BattleClient.Instance != null)
        {
            float rtt = BattleClient.Instance.SmoothedRtt;
            pingText.text = $"RTT: {rtt * 1000:F0}ms";

            // 根据延迟变色
            pingText.color = rtt < 0.05f ? Color.green :
                             rtt < 0.1f ? Color.yellow : Color.red;
        }
    }

    private float _fpsUpdateTimer;
    private int _frameCount;
    private float _fps;

    private void UpdateFPS()
    {
        if (fpsText == null) return;

        _frameCount++;
        _fpsUpdateTimer += Time.deltaTime;

        if (_fpsUpdateTimer >= 0.5f)
        {
            _fps = _frameCount / _fpsUpdateTimer;
            fpsText.text = $"FPS: {_fps:F0}";

            _frameCount = 0;
            _fpsUpdateTimer = 0f;
        }
    }

    #endregion

    #region 程序化 UI 创建

    private void CreateDefaultUI()
    {
        Debug.Log("[BattleUI] 程序化创建默认 UI...");

        // Canvas
        var canvasGo = new GameObject("BattleCanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        // EventSystem：战斗内不需要 UI 交互（Tab 记分板也不走 EventSystem），
        // EventSystem+InputModule 会吞键盘鼠标事件导致游戏输入失效——直接禁用
        var eventSystem = FindFirstObjectByType<EventSystem>();
        if (eventSystem != null)
        {
            eventSystem.enabled = false;
            Debug.Log("[BattleUI] 已禁用 EventSystem（防止吞游戏输入）");
        }

        // TMP 字体 — 多路径回退
        TMP_FontAsset font = TMP_Settings.defaultFontAsset;
        if (font == null)
            font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        if (font == null)
            font = Resources.Load<TMP_FontAsset>("LiberationSans SDF");
        if (font == null)
            Debug.LogWarning("[BattleUI] 未找到 TMP 字体，文字可能无法显示。请确保 TMP Essentials 已导入。");

        // 中文字体回退
        _cjkFallbackFont = Resources.Load<TMP_FontAsset>("Fonts/NotoSansCJK-Black-7 SDF");
        if (_cjkFallbackFont != null)
        {
            Debug.Log("[BattleUI] 加载中文字体: NotoSansCJK-Black-7 SDF");
            RegisterCJKFallbackFont();
        }
        else
            Debug.LogWarning("[BattleUI] 未找到中文字体，中文可能显示为方块");

        // 创建各部分 UI
        CreateHealthBarUI(canvasGo.transform, font);
        CreateAmmoUI(canvasGo.transform, font);
        CreateCrosshairUI(canvasGo.transform);
        CreateScoreUI(canvasGo.transform, font);
        CreateKillFeedUI(canvasGo.transform, font);
        CreateKillBannerUI(canvasGo.transform, font);
        CreateScoreboardUI(canvasGo.transform, font);
        CreateGameOverPanelUI(canvasGo.transform, font);
        CreateMatchingPanelUI(canvasGo.transform, font);
        CreateNetworkStatusUI(canvasGo.transform, font);

        // 技能栏（Q/E）——之前是死代码，挂到本对象上启用
        if (AbilityBar.Instance == null)
            gameObject.AddComponent<AbilityBar>();

        // 默认颜色渐变
        if (healthColorGradient == null)
        {
            healthColorGradient = new Gradient();
            var gck = new GradientColorKey[]
            {
                new GradientColorKey(Color.red, 0f),
                new GradientColorKey(Color.yellow, 0.5f),
                new GradientColorKey(Color.green, 1f),
            };
            var gak = new GradientAlphaKey[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 1f),
            };
            healthColorGradient.SetKeys(gck, gak);
        }

        Debug.Log("[BattleUI] 默认 UI 创建完成");
    }

    private TMP_Text CreateTMPText(Transform parent, string name, string text, TMP_FontAsset font, float fontSize, Color? color = null, TextAlignmentOptions alignment = TextAlignmentOptions.Center)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        if (font != null) tmp.font = font;
        tmp.fontSize = fontSize;
        tmp.color = color ?? Color.white;
        tmp.alignment = alignment;
        return tmp;
    }

    /// <summary>
    /// 将 CJK 回退字体注册到 TMP 全局设置，使中文文字能正常渲染。
    /// </summary>
    private void RegisterCJKFallbackFont()
    {
        if (_cjkFallbackFont == null) return;
        if (TMP_Settings.fallbackFontAssets == null)
        {
            Debug.LogWarning("[BattleUI] TMP_Settings.fallbackFontAssets is null, cannot register CJK fallback");
            return;
        }
        if (!TMP_Settings.fallbackFontAssets.Contains(_cjkFallbackFont))
        {
            TMP_Settings.fallbackFontAssets.Add(_cjkFallbackFont);
            Debug.Log("[BattleUI] 已注册中文字体回退到 TMP 全局设置");
        }
    }

    private void CreateHealthBarUI(Transform canvasTransform, TMP_FontAsset font)
    {
        // 血条面板（左上角）
        var panel = new GameObject("HealthPanel");
        panel.transform.SetParent(canvasTransform, false);
        var panelRect = panel.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.01f, 0.95f);
        panelRect.anchorMax = new Vector2(0.01f, 0.95f);
        panelRect.pivot = new Vector2(0f, 1f);
        panelRect.sizeDelta = new Vector2(300, 60);

        // 背景
        var bg = new GameObject("HealthBarBG");
        bg.transform.SetParent(panel.transform, false);
        var bgRect = bg.AddComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;
        var bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0, 0, 0, 0.5f);

        // Slider
        var sliderGo = new GameObject("HealthBar");
        sliderGo.transform.SetParent(panel.transform, false);
        var sliderRect = sliderGo.AddComponent<RectTransform>();
        sliderRect.anchorMin = new Vector2(0.05f, 0.15f);
        sliderRect.anchorMax = new Vector2(0.95f, 0.7f);
        sliderRect.offsetMin = Vector2.zero;
        sliderRect.offsetMax = Vector2.zero;
        healthBar = sliderGo.AddComponent<Slider>();
        healthBar.minValue = 0;
        healthBar.maxValue = 100;
        healthBar.value = 100;
        healthBar.transition = Selectable.Transition.None;

        // Slider Background
        var sliderBgGo = new GameObject("Background");
        sliderBgGo.transform.SetParent(sliderGo.transform, false);
        var sliderBgRect = sliderBgGo.AddComponent<RectTransform>();
        sliderBgRect.anchorMin = Vector2.zero;
        sliderBgRect.anchorMax = Vector2.one;
        sliderBgRect.offsetMin = Vector2.zero;
        sliderBgRect.offsetMax = Vector2.zero;
        var sliderBgImg = sliderBgGo.AddComponent<Image>();
        sliderBgImg.color = new Color(0.2f, 0.2f, 0.2f, 0.8f);

        // Slider Fill Area
        var fillArea = new GameObject("Fill Area");
        fillArea.transform.SetParent(sliderGo.transform, false);
        var fillAreaRect = fillArea.AddComponent<RectTransform>();
        fillAreaRect.anchorMin = Vector2.zero;
        fillAreaRect.anchorMax = Vector2.one;
        fillAreaRect.offsetMin = new Vector2(2, 2);
        fillAreaRect.offsetMax = new Vector2(-2, -2);

        var fillGo = new GameObject("Fill");
        fillGo.transform.SetParent(fillArea.transform, false);
        var fillRect = fillGo.AddComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        healthBarFill = fillGo.AddComponent<Image>();
        healthBarFill.color = Color.green;
        healthBar.fillRect = fillRect;
        healthBar.targetGraphic = healthBarFill;
        healthBar.image = healthBarFill;

        // Handle Slide Area (empty, required by Slider)
        var handleArea = new GameObject("Handle Slide Area");
        handleArea.transform.SetParent(sliderGo.transform, false);
        var handleAreaRect = handleArea.AddComponent<RectTransform>();
        handleAreaRect.anchorMin = Vector2.zero;
        handleAreaRect.anchorMax = Vector2.one;
        handleAreaRect.sizeDelta = Vector2.zero;

        // HP 文字
        healthText = CreateTMPText(panel.transform, "HealthText", "100/100", font, 18, Color.white, TextAlignmentOptions.Center);
        var htRect = healthText.GetComponent<RectTransform>();
        htRect.anchorMin = new Vector2(0f, 0f);
        htRect.anchorMax = new Vector2(1f, 0.2f);
        htRect.offsetMin = Vector2.zero;
        htRect.offsetMax = Vector2.zero;
    }

    private void CreateAmmoUI(Transform canvasTransform, TMP_FontAsset font)
    {
        // 弹药面板（血条右侧）
        var panel = new GameObject("AmmoPanel");
        panel.transform.SetParent(canvasTransform, false);
        var panelRect = panel.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.01f, 0.88f);
        panelRect.anchorMax = new Vector2(0.01f, 0.88f);
        panelRect.pivot = new Vector2(0f, 1f);
        panelRect.sizeDelta = new Vector2(180, 40);

        ammoText = CreateTMPText(panel.transform, "AmmoText", "30/30", font, 28, Color.white, TextAlignmentOptions.Left);
        var aRect = ammoText.GetComponent<RectTransform>();
        aRect.anchorMin = Vector2.zero;
        aRect.anchorMax = Vector2.one;
        aRect.offsetMin = Vector2.zero;
        aRect.offsetMax = Vector2.zero;

        reloadText = CreateTMPText(panel.transform, "ReloadText", "换弹中...", font, 16, new Color(1f, 0.8f, 0f), TextAlignmentOptions.Left);
        var rRect = reloadText.GetComponent<RectTransform>();
        rRect.anchorMin = new Vector2(0f, -0.5f);
        rRect.anchorMax = new Vector2(1f, -0.1f);
        rRect.offsetMin = Vector2.zero;
        rRect.offsetMax = Vector2.zero;
        reloadText.gameObject.SetActive(false);
    }

    private void CreateCrosshairUI(Transform canvasTransform)
    {
        var go = new GameObject("Crosshair");
        go.transform.SetParent(canvasTransform, false);
        var rect = go.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(32, 32);

        // 程序化生成十字准星贴图
        int size = 32;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var colors = new Color32[size * size];
        for (int i = 0; i < colors.Length; i++) colors[i] = new Color32(0, 0, 0, 0);

        // 水平线
        for (int x = 0; x < size; x++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                int y = size / 2 + dy;
                if (y >= 0 && y < size) colors[y * size + x] = new Color32(255, 255, 255, 255);
            }
        }
        // 垂直线
        for (int y = 0; y < size; y++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int x = size / 2 + dx;
                if (x >= 0 && x < size) colors[y * size + x] = new Color32(255, 255, 255, 255);
            }
        }
        // 中心缺口
        for (int dy = -2; dy <= 2; dy++)
        {
            for (int dx = -2; dx <= 2; dx++)
            {
                int cx = size / 2 + dx;
                int cy = size / 2 + dy;
                if (cx >= 0 && cx < size && cy >= 0 && cy < size)
                    colors[cy * size + cx] = new Color32(0, 0, 0, 0);
            }
        }

        tex.SetPixels32(colors);
        tex.Apply();
        crosshair = go.AddComponent<Image>();
        crosshair.sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        crosshair.color = crosshairNormalColor;
    }

    private void CreateScoreUI(Transform canvasTransform, TMP_FontAsset font)
    {
        // 计分面板（右上角，击杀信息上方）
        var panel = new GameObject("ScorePanel");
        panel.transform.SetParent(canvasTransform, false);
        var panelRect = panel.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.99f, 0.98f);
        panelRect.anchorMax = new Vector2(0.99f, 0.98f);
        panelRect.pivot = new Vector2(1f, 1f);
        panelRect.sizeDelta = new Vector2(200, 50);

        var layout = panel.AddComponent<HorizontalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleRight;
        layout.childControlHeight = false;
        layout.childControlWidth = false;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = false;
        layout.spacing = 16;
        layout.padding = new RectOffset(4, 4, 2, 2);

        killsText = CreateTMPText(panel.transform, "KillsText", "击杀: 0", font, 20, new Color(0.3f, 1f, 0.3f), TextAlignmentOptions.Right);
        var kRect = killsText.GetComponent<RectTransform>();
        kRect.sizeDelta = new Vector2(90, 26);

        deathsText = CreateTMPText(panel.transform, "DeathsText", "死亡: 0", font, 20, new Color(1f, 0.4f, 0.4f), TextAlignmentOptions.Right);
        var dRect = deathsText.GetComponent<RectTransform>();
        dRect.sizeDelta = new Vector2(90, 26);
    }

    private void CreateKillFeedUI(Transform canvasTransform, TMP_FontAsset font)
    {
        // 击杀信息面板（右上角）
        var panel = new GameObject("KillFeedPanel");
        panel.transform.SetParent(canvasTransform, false);
        var panelRect = panel.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.99f, 0.95f);
        panelRect.anchorMax = new Vector2(0.99f, 0.95f);
        panelRect.pivot = new Vector2(1f, 1f);
        panelRect.sizeDelta = new Vector2(350, 300);

        var layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.UpperRight;
        layout.childControlHeight = false;
        layout.childControlWidth = false;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;
        layout.spacing = 2;
        layout.padding = new RectOffset(5, 5, 5, 5);

        var fitter = panel.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        killFeedContainer = panel.transform;

        // 创建模板项
        killFeedItemPrefab = new GameObject("KillFeedItemTemplate");
        killFeedItemPrefab.SetActive(false);
        var tmp = killFeedItemPrefab.AddComponent<TextMeshProUGUI>();
        if (font != null) tmp.font = font;
        tmp.fontSize = 16;
        tmp.alignment = TextAlignmentOptions.Right;
        var tmpRect = killFeedItemPrefab.GetComponent<RectTransform>();
        tmpRect.sizeDelta = new Vector2(340, 22);
    }

    private void CreateKillBannerUI(Transform canvasTransform, TMP_FontAsset font)
    {
        // 击杀横幅（屏幕中上方，爆头/首杀提示）
        killBannerText = CreateTMPText(canvasTransform, "KillBanner", "", font, 42, new Color(1f, 0.85f, 0.2f));
        var rect = killBannerText.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.78f);
        rect.anchorMax = new Vector2(0.5f, 0.78f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(600, 60);
        killBannerText.alignment = TextAlignmentOptions.Center;
        killBannerText.fontStyle = FontStyles.Bold;
        killBannerText.gameObject.SetActive(false);
    }

    private void CreateScoreboardUI(Transform canvasTransform, TMP_FontAsset font)
    {
        // 记分板（屏幕中央偏左，按住 Tab 显示 / 结算时显示）
        scoreboardPanel = new GameObject("ScoreboardPanel");
        scoreboardPanel.transform.SetParent(canvasTransform, false);
        var rect = scoreboardPanel.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(520, 420);

        var bg = scoreboardPanel.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.8f);

        var title = CreateTMPText(scoreboardPanel.transform, "Title", "记分板", font, 26, Color.white, TextAlignmentOptions.Center);
        var titleRect = title.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.sizeDelta = new Vector2(0, 40);
        titleRect.anchoredPosition = new Vector2(0, -8);

        var header = CreateTMPText(scoreboardPanel.transform, "Header", "玩家            击杀 / 死亡", font, 18, new Color(0.7f, 0.7f, 0.7f), TextAlignmentOptions.Left);
        var headerRect = header.GetComponent<RectTransform>();
        headerRect.anchorMin = new Vector2(0f, 1f);
        headerRect.anchorMax = new Vector2(1f, 1f);
        headerRect.pivot = new Vector2(0.5f, 1f);
        headerRect.sizeDelta = new Vector2(-40, 26);
        headerRect.anchoredPosition = new Vector2(0, -52);

        var listGo = new GameObject("Rows");
        listGo.transform.SetParent(scoreboardPanel.transform, false);
        var listRect = listGo.AddComponent<RectTransform>();
        listRect.anchorMin = new Vector2(0f, 0f);
        listRect.anchorMax = new Vector2(1f, 1f);
        listRect.offsetMin = new Vector2(20, 12);
        listRect.offsetMax = new Vector2(-20, -86);

        var layout = listGo.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlHeight = false;
        layout.childControlWidth = false;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = false;
        layout.spacing = 4;

        scoreboardContainer = listGo.transform;
        scoreboardPanel.SetActive(false);
    }

    private void CreateGameOverPanelUI(Transform canvasTransform, TMP_FontAsset font)
    {
        gameOverPanel = new GameObject("GameOverPanel");
        gameOverPanel.transform.SetParent(canvasTransform, false);
        var rect = gameOverPanel.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.3f, 0.3f);
        rect.anchorMax = new Vector2(0.7f, 0.7f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        var bg = gameOverPanel.AddComponent<Image>();
        bg.color = new Color(0, 0, 0, 0.85f);

        gameOverTitle = CreateTMPText(gameOverPanel.transform, "Title", victoryText, font, 48, Color.green);
        var titleRect = gameOverTitle.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0.1f, 0.6f);
        titleRect.anchorMax = new Vector2(0.9f, 0.9f);
        titleRect.offsetMin = Vector2.zero;
        titleRect.offsetMax = Vector2.zero;

        gameOverSubtitle = CreateTMPText(gameOverPanel.transform, "Subtitle", "", font, 24, Color.white);
        var subRect = gameOverSubtitle.GetComponent<RectTransform>();
        subRect.anchorMin = new Vector2(0.1f, 0.45f);
        subRect.anchorMax = new Vector2(0.9f, 0.6f);
        subRect.offsetMin = Vector2.zero;
        subRect.offsetMax = Vector2.zero;

        var btnGo = new GameObject("ReturnButton");
        btnGo.transform.SetParent(gameOverPanel.transform, false);
        var btnRect = btnGo.AddComponent<RectTransform>();
        btnRect.anchorMin = new Vector2(0.35f, 0.2f);
        btnRect.anchorMax = new Vector2(0.65f, 0.35f);
        btnRect.offsetMin = Vector2.zero;
        btnRect.offsetMax = Vector2.zero;
        var btnImg = btnGo.AddComponent<Image>();
        btnImg.color = new Color(0.3f, 0.3f, 0.3f, 1f);
        returnToLobbyButton = btnGo.AddComponent<Button>();
        returnToLobbyButton.targetGraphic = btnImg;

        var btnLabel = CreateTMPText(btnGo.transform, "Label", "返回大厅", font, 24, Color.white);
        var btnLabelRect = btnLabel.GetComponent<RectTransform>();
        btnLabelRect.anchorMin = Vector2.zero;
        btnLabelRect.anchorMax = Vector2.one;
        btnLabelRect.offsetMin = Vector2.zero;
        btnLabelRect.offsetMax = Vector2.zero;

        gameOverPanel.SetActive(false);
    }

    private void CreateMatchingPanelUI(Transform canvasTransform, TMP_FontAsset font)
    {
        matchingPanel = new GameObject("MatchingPanel");
        matchingPanel.transform.SetParent(canvasTransform, false);
        var rect = matchingPanel.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.35f, 0.35f);
        rect.anchorMax = new Vector2(0.65f, 0.65f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        var bg = matchingPanel.AddComponent<Image>();
        bg.color = new Color(0, 0, 0, 0.8f);

        matchingStatusText = CreateTMPText(matchingPanel.transform, "StatusText", "正在匹配...", font, 32, Color.white);
        var stRect = matchingStatusText.GetComponent<RectTransform>();
        stRect.anchorMin = new Vector2(0.1f, 0.55f);
        stRect.anchorMax = new Vector2(0.9f, 0.85f);
        stRect.offsetMin = Vector2.zero;
        stRect.offsetMax = Vector2.zero;

        var btnGo = new GameObject("CancelButton");
        btnGo.transform.SetParent(matchingPanel.transform, false);
        var btnRect = btnGo.AddComponent<RectTransform>();
        btnRect.anchorMin = new Vector2(0.3f, 0.2f);
        btnRect.anchorMax = new Vector2(0.7f, 0.4f);
        btnRect.offsetMin = Vector2.zero;
        btnRect.offsetMax = Vector2.zero;
        var btnImg = btnGo.AddComponent<Image>();
        btnImg.color = new Color(0.6f, 0.2f, 0.2f, 1f);
        cancelMatchingButton = btnGo.AddComponent<Button>();
        cancelMatchingButton.targetGraphic = btnImg;

        var btnLabel = CreateTMPText(btnGo.transform, "Label", "取消匹配", font, 24, Color.white);
        var btnLabelRect = btnLabel.GetComponent<RectTransform>();
        btnLabelRect.anchorMin = Vector2.zero;
        btnLabelRect.anchorMax = Vector2.one;
        btnLabelRect.offsetMin = Vector2.zero;
        btnLabelRect.offsetMax = Vector2.zero;

        matchingPanel.SetActive(false);
    }

    private void CreateNetworkStatusUI(Transform canvasTransform, TMP_FontAsset font)
    {
        // 右下角
        var panel = new GameObject("NetworkStatusPanel");
        panel.transform.SetParent(canvasTransform, false);
        var panelRect = panel.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.99f, 0.01f);
        panelRect.anchorMax = new Vector2(0.99f, 0.01f);
        panelRect.pivot = new Vector2(1f, 0f);
        panelRect.sizeDelta = new Vector2(200, 50);

        pingText = CreateTMPText(panel.transform, "PingText", "RTT: --ms", font, 16, Color.green, TextAlignmentOptions.Right);
        var pRect = pingText.GetComponent<RectTransform>();
        pRect.anchorMin = new Vector2(0f, 0.55f);
        pRect.anchorMax = new Vector2(1f, 1f);
        pRect.offsetMin = Vector2.zero;
        pRect.offsetMax = Vector2.zero;

        fpsText = CreateTMPText(panel.transform, "FPSText", "FPS: --", font, 16, Color.white, TextAlignmentOptions.Right);
        var fRect = fpsText.GetComponent<RectTransform>();
        fRect.anchorMin = new Vector2(0f, 0f);
        fRect.anchorMax = new Vector2(1f, 0.45f);
        fRect.offsetMin = Vector2.zero;
        fRect.offsetMax = Vector2.zero;
    }

    #endregion
}