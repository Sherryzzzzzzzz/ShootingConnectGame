// 登录面板UI控制器
using System.Collections;
using ShootingGame.Network.Server;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 登录面板。处理服务器连接和用户登录。
/// </summary>
public class LoginPanel : MonoBehaviour
{
    [Header("服务器设置")]
    [SerializeField] private TMP_InputField serverIPInput;
    [SerializeField] private TMP_InputField lobbyPortInput;
    [SerializeField] private TMP_InputField battlePortInput;

    [Header("用户信息")]
    [SerializeField] private TMP_InputField userIdInput;
    [SerializeField] private TMP_InputField usernameInput;

    [Header("按钮")]
    [SerializeField] private Button connectButton;
    [SerializeField] private Button loginButton;
    [SerializeField] private Button quickPlayButton;
    [SerializeField] private Button hostPlayButton; // "Host & Play" 一键本地服

    [Header("状态显示")]
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private GameObject loadingIndicator;

    [Header("面板切换")]
    [SerializeField] private GameObject lobbyPanel;

    // 默认值
    private const string DefaultServerIP = "127.0.0.1";
    private const int DefaultLobbyPort = 7778;
    private const int DefaultBattlePort = 7777;

    private void Awake()
    {
        // 确保 EventSystem 使用 InputSystemUIInputModule（兼容仅 New Input System 模式）
        EnsureInputSystemUIModule();

        // 诊断：检查关键引用是否已赋值
        if (connectButton == null) Debug.LogWarning("[LoginPanel] connectButton 未在 Inspector 中赋值，连接按钮点击无效");
        if (loginButton == null) Debug.LogWarning("[LoginPanel] loginButton 未在 Inspector 中赋值，登录按钮点击无效");
        if (statusText == null) Debug.LogWarning("[LoginPanel] statusText 未在 Inspector 中赋值，状态文字不会更新");

        // 设置默认值
        if (serverIPInput != null) serverIPInput.text = DefaultServerIP;
        if (lobbyPortInput != null) lobbyPortInput.text = DefaultLobbyPort.ToString();
        if (battlePortInput != null) battlePortInput.text = DefaultBattlePort.ToString();
        if (userIdInput != null) userIdInput.text = UnityEngine.Random.Range(1, 10000).ToString();
        if (usernameInput != null) usernameInput.text = $"P{UnityEngine.Random.Range(1000, 9999)}";

        // 绑定按钮事件
        if (connectButton != null) connectButton.onClick.AddListener(OnConnectClick);
        if (loginButton != null) loginButton.onClick.AddListener(OnLoginClick);
        if (quickPlayButton != null) quickPlayButton.onClick.AddListener(OnQuickPlayClick);
        if (hostPlayButton != null) hostPlayButton.onClick.AddListener(OnHostPlayClick);

        Debug.Log($"[LoginPanel] 初始化完成 | connectBtn:{connectButton != null} loginBtn:{loginButton != null} quickBtn:{quickPlayButton != null} statusText:{statusText != null} lobbyPanel:{lobbyPanel != null}");

        UpdateUI();
    }

    private void OnEnable()
    {
        // 确保 LobbyClient 单例存在
        EnsureLobbyClient();

        // 订阅事件
        if (LobbyClient.Instance != null)
        {
            LobbyClient.Instance.OnConnected += OnConnected;
            LobbyClient.Instance.OnDisconnected += OnDisconnected;
            LobbyClient.Instance.OnLoginResult += OnLoginResult;

            Debug.Log($"[LoginPanel-DIAG] OnEnable: IsConnected={LobbyClient.Instance.IsConnected} IsLoggedIn={LobbyClient.Instance.IsLoggedIn} IsInQueue={LobbyClient.Instance.IsInQueue} IsMatchFound={LobbyClient.Instance.IsMatchFound}");

            // 如果已经连接，立即更新状态
            if (LobbyClient.Instance.IsConnected)
            {
                OnConnected();
            }
            // 如果已经登录，立即更新状态
            if (LobbyClient.Instance.IsLoggedIn)
            {
                OnLoginResult(true, "已登录");
            }
        }
    }

    private void EnsureLobbyClient()
    {
        if (LobbyClient.Instance == null)
        {
            // 如果 GameInitializer 不存在，创建必要的组件
            if (GameInitializer.Instance == null)
            {
                var go = new GameObject("NetworkManagers");
                go.AddComponent<UnityMainThreadDispatcher>();
                go.AddComponent<LobbyClient>();
                go.AddComponent<BattleClient>();
                DontDestroyOnLoad(go);
                Debug.Log("[LoginPanel] 自动创建了网络管理器");
            }
        }
    }

    private void OnDisable()
    {
        // 取消订阅
        if (LobbyClient.Instance != null)
        {
            LobbyClient.Instance.OnConnected -= OnConnected;
            LobbyClient.Instance.OnDisconnected -= OnDisconnected;
            LobbyClient.Instance.OnLoginResult -= OnLoginResult;
        }
    }

    private void Update()
    {
        UpdateUI();
    }

    private void UpdateUI()
    {
        bool isConnected = LobbyClient.Instance != null && LobbyClient.Instance.IsConnected;
        bool isLoggedIn = LobbyClient.Instance != null && LobbyClient.Instance.IsLoggedIn;

        // 更新按钮状态
        if (connectButton != null)
        {
            connectButton.interactable = !isConnected;
            connectButton.GetComponentInChildren<TMP_Text>().text = isConnected ? "已连接" : "连接服务器";
        }

        if (loginButton != null)
        {
            loginButton.interactable = isConnected && !isLoggedIn;
            loginButton.GetComponentInChildren<TMP_Text>().text = isLoggedIn ? "已登录" : "登录";
        }

        if (quickPlayButton != null)
        {
            quickPlayButton.interactable = isLoggedIn;
        }

        // 更新状态文本
        if (statusText != null)
        {
            if (!isConnected)
            {
                statusText.text = "未连接到服务器";
                statusText.color = Color.red;
            }
            else if (!isLoggedIn)
            {
                statusText.text = "已连接，请登录";
                statusText.color = Color.yellow;
            }
            else
            {
                statusText.text = $"欢迎, {LobbyClient.Instance.username}!";
                statusText.color = Color.green;
            }
        }
    }

    #region 按钮事件

    private void OnConnectClick()
    {
        // 获取服务器地址
        string ip = serverIPInput?.text ?? DefaultServerIP;
        int lobbyPort = int.TryParse(lobbyPortInput?.text, out int lp) ? lp : DefaultLobbyPort;
        int battlePort = int.TryParse(battlePortInput?.text, out int bp) ? bp : DefaultBattlePort;

        // 设置服务器地址
        if (GameInitializer.Instance != null)
        {
            GameInitializer.Instance.SetServerAddress(ip, lobbyPort, battlePort);
        }
        else if (LobbyClient.Instance != null)
        {
            LobbyClient.Instance.serverIP = ip;
            LobbyClient.Instance.serverPort = lobbyPort;
        }

        if (BattleClient.Instance != null)
        {
            BattleClient.Instance.serverIP = ip;
            BattleClient.Instance.serverPort = battlePort;
        }

        // 连接
        if (LobbyClient.Instance != null)
        {
            LobbyClient.Instance.Connect();
            ShowLoading(true);
            SetStatus("正在连接服务器...", Color.yellow);
        }
    }

    private void OnLoginClick()
    {
        // 获取用户信息
        int userId = int.TryParse(userIdInput?.text, out int id) ? id : 1;
        string username = usernameInput?.text ?? "Player";

        // 设置用户信息
        if (GameInitializer.Instance != null)
        {
            GameInitializer.Instance.SetUserInfo(userId, username);
        }
        else if (LobbyClient.Instance != null)
        {
            LobbyClient.Instance.userId = userId;
            LobbyClient.Instance.username = username;
        }

        // 确保已连接
        if (LobbyClient.Instance == null)
        {
            Debug.LogError("[LoginPanel] LobbyClient.Instance 为空！请确保场景中有 GameInitializer 或 NetworkManagers");
            return;
        }

        if (!LobbyClient.Instance.IsConnected)
        {
            Debug.Log("[LoginPanel] 未连接，自动连接服务器...");
            SetStatus("正在连接...", Color.yellow);
            LobbyClient.Instance.Connect();
        }

        if (!LobbyClient.Instance.IsConnected)
        {
            Debug.LogError("[LoginPanel] 连接服务器失败，无法登录");
            SetStatus("连接失败，请检查服务器是否启动", Color.red);
            return;
        }

        // 登录
        LobbyClient.Instance.Login();
        ShowLoading(true);
        SetStatus("正在登录...", Color.yellow);
        Debug.Log($"[LoginPanel] 发送登录请求: userId={userId}, username={username}");
    }

    private void OnQuickPlayClick()
    {
        // 快速开始：如果未登录，先自动登录
        if (LobbyClient.Instance != null)
        {
            if (!LobbyClient.Instance.IsConnected)
            {
                OnConnectClick();
                // 连接后需要等一会才能登录，改用协程延迟登录
                StartCoroutine(AutoLoginAfterConnect());
                return;
            }

            if (!LobbyClient.Instance.IsLoggedIn)
            {
                OnLoginClick();
                return;
            }
        }

        // 切换到大厅面板并开始匹配
        if (LobbyClient.Instance != null && LobbyClient.Instance.IsLoggedIn)
        {
            SwitchToLobbyPanel();

            if (BattleManager.Instance != null)
            {
                BattleManager.Instance.StartMatching();
            }
        }
    }

    /// <summary>
    /// "Host & Play" 一键启动本地服 + 自动连接。
    /// </summary>
    private void OnHostPlayClick()
    {
        // 1. 确保 LocalServerStarter 存在并启动
        var starter = LocalServerStarter.Instance ?? CreateLocalServerStarter();
        if (!starter.IsRunning)
            starter.StartServer();

        // 2. 强制使用 localhost
        string ip = "127.0.0.1";
        int lobbyPort = starter.LobbyPort;
        int battlePort = starter.BattlePort;

        // 3. 设置网络管理器地址
        if (GameInitializer.Instance != null)
        {
            GameInitializer.Instance.SetServerAddress(ip, lobbyPort, battlePort);
        }
        if (LobbyClient.Instance != null)
        {
            LobbyClient.Instance.serverIP = ip;
            LobbyClient.Instance.serverPort = lobbyPort;
        }
        if (BattleClient.Instance != null)
        {
            BattleClient.Instance.serverIP = ip;
            BattleClient.Instance.serverPort = battlePort;
        }

        // 4. 更新输入框显示
        if (serverIPInput != null) serverIPInput.text = ip;
        if (lobbyPortInput != null) lobbyPortInput.text = lobbyPort.ToString();
        if (battlePortInput != null) battlePortInput.text = battlePort.ToString();

        // 5. 自动连接 + 登录 + 匹配
        ShowLoading(true);
        SetStatus("启动本地服务器...", Color.cyan);
        StartCoroutine(AutoHostFlow());
    }

    private ShootingGame.Network.Server.LocalServerStarter CreateLocalServerStarter()
    {
        var go = new GameObject("LocalServerStarter");
        DontDestroyOnLoad(go);
        return go.AddComponent<ShootingGame.Network.Server.LocalServerStarter>();
    }

    private System.Collections.IEnumerator AutoHostFlow()
    {
        // 等待服务端就绪
        var starter = LocalServerStarter.Instance;
        float timeout = 8f;
        while (!starter.IsRunning && timeout > 0)
        {
            yield return new WaitForSeconds(0.3f);
            timeout -= 0.3f;
        }
        if (!starter.IsRunning)
        {
            ShowLoading(false);
            SetStatus("服务端启动失败", Color.red);
            yield break;
        }

        SetStatus("正在连接本地服务器...", Color.yellow);

        // 确保连接
        if (LobbyClient.Instance == null)
        {
            ShowLoading(false);
            SetStatus("LobbyClient 不存在", Color.red);
            yield break;
        }

        LobbyClient.Instance.Connect();

        // 等待连接成功
        timeout = 5f;
        while (!LobbyClient.Instance.IsConnected && timeout > 0)
        {
            yield return new WaitForSeconds(0.2f);
            timeout -= 0.2f;
        }
        if (!LobbyClient.Instance.IsConnected)
        {
            ShowLoading(false);
            SetStatus("连接失败", Color.red);
            yield break;
        }

        SetStatus("已连接，正在登录...", Color.yellow);

        // 登录
        LobbyClient.Instance.Login();

        // 等待登录结果（由 OnLoginResult 回调切换面板）
        timeout = 5f;
        while (!LobbyClient.Instance.IsLoggedIn && timeout > 0)
        {
            yield return new WaitForSeconds(0.2f);
            timeout -= 0.2f;
        }
        if (!LobbyClient.Instance.IsLoggedIn)
        {
            ShowLoading(false);
            SetStatus("登录失败", Color.red);
            yield break;
        }

        // 登录成功后切换到大厅
        SwitchToLobbyPanel();

        // 自动开始匹配
        if (BattleManager.Instance != null)
        {
            BattleManager.Instance.StartMatching();
            SetStatus("正在匹配...", Color.yellow);
        }

        ShowLoading(false);
    }

    /// <summary>
    /// 切换到大厅面板（带兜底查找）。
    /// </summary>
    private void SwitchToLobbyPanel()
    {
        if (lobbyPanel != null)
        {
            gameObject.SetActive(false);
            lobbyPanel.SetActive(true);
        }
        else
        {
            var lobby = FindObjectOfType<LobbyPanel>(true);
            if (lobby != null)
            {
                gameObject.SetActive(false);
                lobby.gameObject.SetActive(true);
            }
        }
    }

    private System.Collections.IEnumerator AutoLoginAfterConnect()
    {
        // 等待连接建立
        float timeout = 5f;
        while (!LobbyClient.Instance.IsConnected && timeout > 0)
        {
            yield return new WaitForSeconds(0.2f);
            timeout -= 0.2f;
        }

        if (LobbyClient.Instance.IsConnected && !LobbyClient.Instance.IsLoggedIn)
        {
            yield return new WaitForSeconds(0.3f);
            OnLoginClick();
        }
    }

    #endregion

    #region 网络事件回调

    private void OnConnected()
    {
        ShowLoading(false);
        SetStatus("连接成功!", Color.green);
        Debug.Log("[LoginPanel] 已连接到服务器");

        // 接通新框架 RPC 传输：客户端 → 服务端走 TCP
        ShootingGame.Network.NetworkBehaviour.SendServerRpcTransport = (rpcPayload) =>
        {
            if (LobbyClient.Instance != null && LobbyClient.Instance.IsConnected)
            {
                var pack = new ShootingGame.Shared.Protocol.MainPack
                {
                    RequestCode = ShootingGame.Shared.Protocol.RequestCode.Battle,
                    ActionCode = ShootingGame.Shared.Protocol.ActionCode.RpcCall,
                    RpcPayload = rpcPayload
                };
                LobbyClient.Instance.Send(pack);
            }
        };
        Debug.Log("[LoginPanel] RPC transport (client→server) wired to TCP");
    }

    private void OnDisconnected()
    {
        ShowLoading(false);
        SetStatus("与服务器断开连接", Color.red);
        Debug.Log("[LoginPanel] 与服务器断开连接");
    }

    private void OnLoginResult(bool success, string message)
    {
        ShowLoading(false);

        if (success)
        {
            SetStatus("登录成功!", Color.green);
            Debug.Log($"[LoginPanel] 登录成功: {message}");

            // 登录成功后自动切换到大厅面板
            SwitchToLobbyPanel();

            // 通知 GameFlowManager 进入大厅状态
            if (GameFlowManager.Instance != null)
            {
                GameFlowManager.Instance.EnterLobby();
            }
        }
        else
        {
            SetStatus($"登录失败: {message}", Color.red);
            Debug.LogWarning($"[LoginPanel] 登录失败: {message}");
        }
    }

    #endregion

    #region UI辅助方法

    private void ShowLoading(bool show)
    {
        if (loadingIndicator != null)
        {
            loadingIndicator.SetActive(show);
        }
    }

    private void SetStatus(string message, Color color)
    {
        if (statusText != null)
        {
            statusText.text = message;
            statusText.color = color;
        }
    }

    /// <summary>
    /// 确保 EventSystem 使用 InputSystemUIInputModule（兼容仅 New Input System 模式）。
    /// </summary>
    private static void EnsureInputSystemUIModule()
    {
        var eventSystem = FindFirstObjectByType<EventSystem>();
        if (eventSystem == null)
        {
            var go = new GameObject("EventSystem");
            eventSystem = go.AddComponent<EventSystem>();
        }
        if (eventSystem.GetComponent<InputSystemUIInputModule>() == null)
        {
            eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
        }
    }

    #endregion
}