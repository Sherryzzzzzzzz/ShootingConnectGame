// 大厅面板UI控制器 - 房间列表
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ShootingGame.Shared.Protocol;

/// <summary>
/// 大厅面板。显示房间列表，支持创建/加入房间。
/// 所有 UI 元素通过 Inspector 赋值（使用 Editor 工具 "配置完整UI" 自动创建）。
/// </summary>
public class LobbyPanel : MonoBehaviour
{
    [Header("匹配按钮")]
    [SerializeField] private Button joinQueueButton;
    [SerializeField] private Button leaveQueueButton;

    [Header("状态显示")]
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text queueStatusText;
    [SerializeField] private TMP_Text playerCountText;

    [Header("匹配动画")]
    [SerializeField] private GameObject matchingIndicator;
    [SerializeField] private float matchingAnimationSpeed = 1f;

    [Header("面板切换")]
    [SerializeField] private GameObject loginPanel;
    [SerializeField] private GameObject battleUI;

    [Header("房间列表")]
    [SerializeField] private Transform roomListContainer;
    [SerializeField] private GameObject roomEntryPrefab;
    [SerializeField] private Button refreshRoomsButton;
    [SerializeField] private Button createRoomButton;
    [SerializeField] private TMP_InputField roomNameInput;
    [SerializeField] private TMP_InputField maxPlayersInput;

    [Header("我的房间")]
    [SerializeField] private GameObject myRoomPanel;
    [SerializeField] private TMP_Text myRoomNameText;
    [SerializeField] private TMP_Text myRoomPlayersText;
    [SerializeField] private Button leaveRoomButton;

    [Header("英雄选择")]
    [SerializeField] private TMP_Text selectedHeroText;
    [SerializeField] private Button selectSoldierButton;
    [SerializeField] private Button selectTankButton;
    [SerializeField] private Button selectSniperButton;

    private bool _isInQueue;
    private float _queueTime;
    private int _dotCount;
    private int _onlinePlayerCount;
    private int _selectedHeroId = ShootingGame.Shared.Hero.HeroRegistry.DefaultHeroId;
    private List<RoomInfo> _roomList = new List<RoomInfo>();

    private void Awake()
    {
        if (joinQueueButton != null) joinQueueButton.onClick.AddListener(OnJoinQueueClick);
        if (leaveQueueButton != null) leaveQueueButton.onClick.AddListener(OnLeaveQueueClick);
        if (refreshRoomsButton != null) refreshRoomsButton.onClick.AddListener(OnRefreshRoomsClick);
        if (createRoomButton != null) createRoomButton.onClick.AddListener(OnCreateRoomClick);
        if (leaveRoomButton != null) leaveRoomButton.onClick.AddListener(OnLeaveRoomClick);
        if (selectSoldierButton != null) selectSoldierButton.onClick.AddListener(() => SelectHeroByIndex(0));
        if (selectTankButton != null) selectTankButton.onClick.AddListener(() => SelectHeroByIndex(1));
        if (selectSniperButton != null) selectSniperButton.onClick.AddListener(() => SelectHeroByIndex(2));
        RefreshHeroButtons();
    }

    private void OnEnable()
    {
        if (LobbyClient.Instance == null) return;

        LobbyClient.Instance.OnJoinQueueResult += OnJoinQueueResult;
        LobbyClient.Instance.OnMatchFound += OnMatchFound;
        LobbyClient.Instance.OnOnlinePlayerCountChanged += OnOnlinePlayerCountChanged;
        LobbyClient.Instance.OnRoomListReceived += OnRoomListReceived;
        LobbyClient.Instance.OnRoomCreated += OnRoomCreated;
        LobbyClient.Instance.OnRoomJoined += OnRoomJoined;
        LobbyClient.Instance.OnRoomLeft += OnRoomLeft;

        if (LobbyClient.Instance.IsMatchFound && LobbyClient.Instance.MatchedBattleInfo != null)
            OnMatchFound(LobbyClient.Instance.MatchedBattleInfo);

        RequestRoomList();
        UpdateUI();
    }

    private void OnDisable()
    {
        if (LobbyClient.Instance != null)
        {
            LobbyClient.Instance.OnJoinQueueResult -= OnJoinQueueResult;
            LobbyClient.Instance.OnMatchFound -= OnMatchFound;
            LobbyClient.Instance.OnOnlinePlayerCountChanged -= OnOnlinePlayerCountChanged;
            LobbyClient.Instance.OnRoomListReceived -= OnRoomListReceived;
            LobbyClient.Instance.OnRoomCreated -= OnRoomCreated;
            LobbyClient.Instance.OnRoomJoined -= OnRoomJoined;
            LobbyClient.Instance.OnRoomLeft -= OnRoomLeft;
        }
    }

    private void Update()
    {
        UpdateUI();
        UpdateMatchingAnimation();
    }

    private void RequestRoomList()
    {
        if (LobbyClient.Instance != null && LobbyClient.Instance.IsConnected)
            LobbyClient.Instance.RequestRoomList();
    }

    private float _diagLogTimer;

    private void UpdateUI()
    {
        if (LobbyClient.Instance == null) return;

        // 直接从 LobbyClient 读取状态，避免缓存不同步
        _isInQueue = LobbyClient.Instance.IsInQueue;
        bool isLoggedIn = LobbyClient.Instance.IsLoggedIn;
        bool isInRoom = LobbyClient.Instance.IsInRoom;
        bool isConnected = LobbyClient.Instance.IsConnected;

        // 定期诊断日志
        _diagLogTimer -= Time.deltaTime;
        if (_diagLogTimer <= 0f)
        {
            _diagLogTimer = 2f;
            Debug.Log($"[LobbyPanel-DIAG] isLoggedIn={isLoggedIn} isInQueue={_isInQueue} isConnected={isConnected} isInRoom={isInRoom} isMatchFound={LobbyClient.Instance.IsMatchFound} joinBtnActive={joinQueueButton?.gameObject?.activeSelf} joinBtnInteractable={joinQueueButton?.interactable}");
        }

        if (joinQueueButton != null)
        {
            joinQueueButton.gameObject.SetActive(!_isInQueue);
            joinQueueButton.interactable = isLoggedIn && !_isInQueue && isConnected;
        }

        if (leaveQueueButton != null)
        {
            leaveQueueButton.gameObject.SetActive(_isInQueue);
            leaveQueueButton.interactable = _isInQueue;
        }

        if (statusText != null)
        {
            statusText.text = LobbyClient.Instance.IsLoggedIn
                ? $"欢迎, {LobbyClient.Instance.username}!"
                : "未登录";
        }

        if (queueStatusText != null)
        {
            if (!isConnected)
            {
                queueStatusText.text = "与服务器断开连接，请返回登录";
                queueStatusText.color = Color.red;
            }
            else if (!isLoggedIn)
            {
                queueStatusText.text = "未登录，请返回登录界面重新登录";
                queueStatusText.color = Color.red;
            }
            else if (isInRoom)
            {
                queueStatusText.text = $"已在房间 #{LobbyClient.Instance.CurrentRoomId} 中";
                queueStatusText.color = Color.green;
            }
            else if (_isInQueue)
            {
                queueStatusText.text = $"正在匹配中... ({_queueTime:F0}秒)";
                queueStatusText.color = Color.yellow;
            }
            else
            {
                queueStatusText.text = "选择一个房间加入 或 创建新房间";
                queueStatusText.color = Color.white;
            }
        }

        if (playerCountText != null)
            playerCountText.text = $"在线玩家: {_onlinePlayerCount}";

        if (matchingIndicator != null)
            matchingIndicator.SetActive(_isInQueue);

        if (myRoomPanel != null)
            myRoomPanel.SetActive(isInRoom);

        if (isInRoom && myRoomNameText != null)
        {
            foreach (var r in _roomList)
            {
                if (r.RoomId == LobbyClient.Instance.CurrentRoomId)
                {
                    myRoomNameText.text = r.RoomName;
                    if (myRoomPlayersText != null)
                        myRoomPlayersText.text = $"玩家: {r.PlayerCount}/{r.MaxPlayers}";
                    break;
                }
            }
        }

        if (createRoomButton != null)
            createRoomButton.interactable = LobbyClient.Instance.IsLoggedIn && !isInRoom && !_isInQueue;

        // 断线恢复：如果未连接且未登录，自动返回登录界面
        if (!isConnected && !isLoggedIn)
        {
            BackToLogin();
        }
    }

    private void UpdateMatchingAnimation()
    {
        if (!_isInQueue) { _queueTime = 0f; return; }
        _queueTime += Time.deltaTime;
        _dotCount = (int)(_queueTime * matchingAnimationSpeed) % 4;
        if (queueStatusText != null)
            queueStatusText.text = $"正在匹配中{new string('.', _dotCount)} ({_queueTime:F0}秒)";
    }

    // ---------------------------------------------------------------
    // 房间列表刷新（基于预制体 roomEntryPrefab）
    // ---------------------------------------------------------------

    private void RefreshRoomListUI()
    {
        if (roomListContainer == null)
        {
            Debug.LogWarning("[LobbyPanel] roomListContainer 为空，跳过刷新");
            return;
        }
        if (roomEntryPrefab == null)
        {
            Debug.LogWarning("[LobbyPanel] roomEntryPrefab 为空，跳过刷新");
            return;
        }

        // 清除旧条目
        foreach (Transform child in roomListContainer)
            Destroy(child.gameObject);

        // 创建新条目
        foreach (var room in _roomList)
        {
            var entry = Instantiate(roomEntryPrefab, roomListContainer);
            entry.name = $"RoomEntry_{room.RoomId}";
            BindRoomEntry(entry, room);
        }

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(roomListContainer as RectTransform);
    }

    /// <summary>
    /// 将房间数据绑定到预制体实例的子对象上。
    /// 预制体需要包含以下命名的子对象：
    ///   RoomName (TMP_Text), Creator (TMP_Text), PlayerCount (TMP_Text),
    ///   JoinButton (Button), Status (TMP_Text, 可选)
    /// </summary>
    private void BindRoomEntry(GameObject entry, RoomInfo room)
    {
        // 房间名
        var nameText = entry.transform.Find("RoomName")?.GetComponent<TextMeshProUGUI>();
        if (nameText != null) nameText.text = room.RoomName;

        // 创建者
        var creatorText = entry.transform.Find("Creator")?.GetComponent<TextMeshProUGUI>();
        if (creatorText != null) creatorText.text = room.CreatorName;

        // 人数
        var playersText = entry.transform.Find("PlayerCount")?.GetComponent<TextMeshProUGUI>();
        if (playersText != null)
        {
            playersText.text = $"{room.PlayerCount}/{room.MaxPlayers}";
            playersText.color = room.PlayerCount >= room.MaxPlayers ? Color.red : Color.green;
        }

        // 加入按钮
        var joinBtn = entry.transform.Find("JoinButton")?.GetComponent<Button>();
        if (joinBtn != null)
        {
            int roomId = room.RoomId;
            bool canJoin = room.Status == 0 && room.PlayerCount < room.MaxPlayers
                && !LobbyClient.Instance.IsInRoom;
            joinBtn.interactable = canJoin;
            joinBtn.onClick.RemoveAllListeners();
            joinBtn.onClick.AddListener(() => OnJoinRoomClick(roomId));
        }

        // 状态标签
        var statusText = entry.transform.Find("Status")?.GetComponent<TextMeshProUGUI>();
        if (statusText != null)
        {
            if (room.Status != 0)
            {
                statusText.gameObject.SetActive(true);
                statusText.text = room.Status == 1 ? "[游戏中]" : "[已结束]";
            }
            else
            {
                statusText.gameObject.SetActive(false);
            }
        }
    }

    // ---------------------------------------------------------------
    // 按钮事件
    // ---------------------------------------------------------------

    private void OnJoinQueueClick()
    {
        if (LobbyClient.Instance == null)
        {
            Debug.LogError("[LobbyPanel] LobbyClient.Instance is null, cannot join queue");
            return;
        }

        if (!LobbyClient.Instance.IsConnected)
        {
            SetStatus("未连接到服务器，请先连接", Color.red);
            Debug.LogWarning("[LobbyPanel] Cannot join queue: not connected");
            return;
        }

        if (!LobbyClient.Instance.IsLoggedIn)
        {
            SetStatus("未登录，请先登录", Color.red);
            Debug.LogWarning("[LobbyPanel] Cannot join queue: not logged in");
            return;
        }

        if (_isInQueue)
        {
            Debug.LogWarning("[LobbyPanel] Already in queue, ignoring duplicate click");
            return;
        }

        Debug.Log($"[LobbyPanel] Sending JoinQueue: heroId={_selectedHeroId}, userId={LobbyClient.Instance.userId}");
        LobbyClient.Instance.SelectedHeroId = _selectedHeroId;
        LobbyClient.Instance.JoinQueue(heroId: _selectedHeroId);
    }

    private void SelectHero(int heroId)
    {
        _selectedHeroId = heroId;
        var hero = ShootingGame.Shared.Hero.HeroRegistry.GetHero(heroId);
        string heroName = hero != null ? hero.Name : "Unknown";
        if (selectedHeroText != null && hero != null)
            selectedHeroText.text = $"已选择: {heroName} (HP:{hero.MaxHP} 速度:{hero.MoveSpeed})";
    }

    /// <summary>按英雄列表序号选择（大厅按钮是数据驱动的，不再硬编码 heroId）</summary>
    private void SelectHeroByIndex(int index)
    {
        var heroes = ShootingGame.Shared.Hero.HeroRegistry.GetAllHeroes();
        if (index >= 0 && index < heroes.Count)
            SelectHero(heroes[index].HeroId);
    }

    /// <summary>把大厅三个英雄按钮重绑定到 HeroRegistry 的实际英雄（图标/名字数据驱动）</summary>
    private void RefreshHeroButtons()
    {
        var heroes = ShootingGame.Shared.Hero.HeroRegistry.GetAllHeroes();
        var buttons = new[] { selectSoldierButton, selectTankButton, selectSniperButton };
        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] == null) continue;
            if (i < heroes.Count)
            {
                var label = buttons[i].GetComponentInChildren<TMP_Text>();
                if (label != null) label.text = heroes[i].Name;
                buttons[i].gameObject.SetActive(true);
            }
            else
            {
                // 英雄不足 3 个时隐藏多余按钮
                buttons[i].gameObject.SetActive(false);
            }
        }
        // 默认选中第一个英雄
        if (heroes.Count > 0) SelectHero(heroes[0].HeroId);
    }

    private void OnLeaveQueueClick()
    {
        if (LobbyClient.Instance != null && _isInQueue)
        {
            LobbyClient.Instance.LeaveQueue();
            _queueTime = 0f;
        }
    }

    private void OnRefreshRoomsClick() => RequestRoomList();

    private void OnCreateRoomClick()
    {
        if (LobbyClient.Instance == null || !LobbyClient.Instance.IsLoggedIn) return;
        if (LobbyClient.Instance.IsInRoom)
        {
            SetStatus("已在房间中，请先离开当前房间", Color.yellow);
            return;
        }

        string roomName = roomNameInput != null && !string.IsNullOrWhiteSpace(roomNameInput.text)
            ? roomNameInput.text.Trim()
            : $"{LobbyClient.Instance.username}'s Room";
        int maxPlayers = 2;
        if (maxPlayersInput != null && int.TryParse(maxPlayersInput.text, out int mp))
            maxPlayers = Mathf.Clamp(mp, 2, 8);

        LobbyClient.Instance.CreateRoom(roomName, maxPlayers);
        SetStatus($"正在创建房间 \"{roomName}\"...", Color.yellow);
    }

    private void OnJoinRoomClick(int roomId)
    {
        if (LobbyClient.Instance == null || !LobbyClient.Instance.IsLoggedIn) return;
        if (LobbyClient.Instance.IsInRoom)
        {
            SetStatus("已在房间中，请先离开当前房间", Color.yellow);
            return;
        }
        LobbyClient.Instance.JoinRoom(roomId);
        SetStatus("正在加入房间...", Color.yellow);
    }

    private void OnLeaveRoomClick()
    {
        if (LobbyClient.Instance != null && LobbyClient.Instance.IsInRoom)
        {
            LobbyClient.Instance.LeaveRoom();
            SetStatus("已离开房间", Color.white);
        }
    }

    // ---------------------------------------------------------------
    // 网络事件回调
    // ---------------------------------------------------------------

    private void OnJoinQueueResult(bool success, string error)
    {
        if (success)
        {
            SetStatus("已加入匹配队列，等待其他玩家...", Color.yellow);
        }
        else
        {
            string msg = string.IsNullOrEmpty(error) ? "加入匹配队列失败" : $"匹配失败: {error}";
            SetStatus(msg, Color.red);
            Debug.LogWarning($"[LobbyPanel] JoinQueue 失败: {error}");
        }
    }

    private void OnOnlinePlayerCountChanged(int count)
    {
        _onlinePlayerCount = count;
    }

    private void OnMatchFound(BattleInfo battleInfo)
    {
        SetStatus("匹配成功！正在准备战斗...", Color.green);
        if (battleUI != null) battleUI.SetActive(true);
    }

    private void OnRoomListReceived(List<RoomInfo> rooms)
    {
        _roomList = rooms ?? new List<RoomInfo>();
        RefreshRoomListUI();
    }

    private void OnRoomCreated(bool success, string error, RoomInfo room)
    {
        if (success)
        {
            SetStatus($"房间 \"{room?.RoomName}\" 创建成功！", Color.green);
            RequestRoomList();
        }
        else SetStatus($"创建房间失败: {error}", Color.red);
    }

    private void OnRoomJoined(bool success, string error)
    {
        if (success)
        {
            SetStatus("加入房间成功！等待其他玩家...", Color.green);
            RequestRoomList();
        }
        else SetStatus($"加入房间失败: {error}", Color.red);
    }

    private void OnRoomLeft(bool success)
    {
        SetStatus("已离开房间", Color.white);
        RequestRoomList();
    }

    // ---------------------------------------------------------------
    // UI 辅助
    // ---------------------------------------------------------------

    private void SetStatus(string message, Color color)
    {
        if (statusText != null)
        {
            statusText.text = message;
            statusText.color = color;
        }
    }

    public void BackToLogin()
    {
        if (_isInQueue && LobbyClient.Instance != null)
            LobbyClient.Instance.LeaveQueue();
        if (LobbyClient.Instance != null && LobbyClient.Instance.IsInRoom)
            LobbyClient.Instance.LeaveRoom();

        if (loginPanel != null)
        {
            gameObject.SetActive(false);
            loginPanel.SetActive(true);
        }
        else
        {
            // 兜底查找 LoginPanel
            var login = FindObjectOfType<LoginPanel>(true);
            if (login != null)
            {
                gameObject.SetActive(false);
                login.gameObject.SetActive(true);
            }
        }
    }
}
