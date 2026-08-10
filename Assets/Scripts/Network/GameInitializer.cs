using UnityEngine;

/// <summary>
/// 游戏初始化器。在场景启动时初始化所有必要的单例组件。
/// 将此脚本挂载到场景中的一个 GameObject 上。
/// </summary>
public class GameInitializer : MonoBehaviour
{
    // 单例
    public static GameInitializer Instance { get; private set; }

    [Header("设置")]
    [SerializeField] private string serverIP = "127.0.0.1";
    [SerializeField] private int lobbyPort = 7778;
    [SerializeField] private int battlePort = 7777;

    [Header("用户信息")]
    [SerializeField] private int userId = 0; // 0 = auto-generate random ID
    [SerializeField] private string username = "Player";

    [Header("自动连接")]
    [SerializeField] private bool autoConnectOnStart = false;
    [SerializeField] private bool autoLoginOnConnect = false;

    private void Awake()
    {
        // 单例初始化
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // 失焦时继续运行（匹配/战斗/渲染不暂停）
        Application.runInBackground = true;

        // 确保帧率不受后台限制
        Application.targetFrameRate = 60;

        // 确保所有必要的单例都存在
        EnsureSingletons();
    }

    private void Start()
    {
        if (autoConnectOnStart)
        {
            ConnectToServer();
        }
    }

    private void EnsureSingletons()
    {
        // 确保主线程调度器存在
        if (UnityMainThreadDispatcher.Instance == null)
        {
            var go = new GameObject("UnityMainThreadDispatcher");
            go.AddComponent<UnityMainThreadDispatcher>();
        }

        // 确保 LobbyClient 存在
        int actualUserId = userId > 0 ? userId : UnityEngine.Random.Range(1001, 99999);
        string actualUsername = username == "Player"
            ? $"Player_{actualUserId}"
            : username;

        if (LobbyClient.Instance == null)
        {
            var go = new GameObject("LobbyClient");
            var client = go.AddComponent<LobbyClient>();
            client.serverIP = serverIP;
            client.serverPort = lobbyPort;
            client.userId = actualUserId;
            client.username = actualUsername;
        }
        else
        {
            LobbyClient.Instance.serverIP = serverIP;
            LobbyClient.Instance.serverPort = lobbyPort;
            LobbyClient.Instance.userId = actualUserId;
            LobbyClient.Instance.username = actualUsername;
        }

        // 确保 BattleClient 存在
        if (BattleClient.Instance == null)
        {
            var go = new GameObject("BattleClient");
            var client = go.AddComponent<BattleClient>();
            client.serverIP = serverIP;
            client.serverPort = battlePort;
        }
        else
        {
            BattleClient.Instance.serverIP = serverIP;
            BattleClient.Instance.serverPort = battlePort;
        }

        // 确保其他管理器存在
        if (DynamicTickSystem.Instance == null)
        {
            var go = new GameObject("DynamicTickSystem");
            go.AddComponent<DynamicTickSystem>();
        }

        if (ClientBulletSystem.Instance == null)
        {
            var go = new GameObject("ClientBulletSystem");
            go.AddComponent<ClientBulletSystem>();
        }

        if (HitEventView.Instance == null)
        {
            var go = new GameObject("HitEventView");
            go.AddComponent<HitEventView>();
        }

        if (AuthoritySync.Instance == null)
        {
            var go = new GameObject("AuthoritySync");
            go.AddComponent<AuthoritySync>();
        }

        if (BattleManager.Instance == null)
        {
            var go = new GameObject("BattleManager");
            go.AddComponent<BattleManager>();
        }

        // 确保卡通后处理 Global Volume 存在（跨场景生效）
        if (FindFirstObjectByType<AutoPostFXSetup>() == null)
        {
            var go = new GameObject("AutoPostFXSetup");
            go.AddComponent<AutoPostFXSetup>();
            DontDestroyOnLoad(go);
        }

        Debug.Log("[GameInitializer] 所有单例组件已初始化");
    }

    public void ConnectToServer()
    {
        if (LobbyClient.Instance != null)
        {
            LobbyClient.Instance.Connect();

            if (autoLoginOnConnect)
            {
                // 延迟登录，等待连接建立
                Invoke(nameof(Login), 0.5f);
            }
        }
    }

    public void Login()
    {
        if (LobbyClient.Instance != null && LobbyClient.Instance.IsConnected)
        {
            LobbyClient.Instance.Login();
        }
        else
        {
            Debug.LogWarning("[GameInitializer] 未连接到服务器，无法登录");
        }
    }

    public void StartMatching()
    {
        if (BattleManager.Instance != null)
        {
            BattleManager.Instance.StartMatching();
        }
    }

    /// <summary>
    /// 设置服务器地址
    /// </summary>
    public void SetServerAddress(string ip, int lobby, int battle)
    {
        serverIP = ip;
        lobbyPort = lobby;
        battlePort = battle;

        if (LobbyClient.Instance != null)
        {
            LobbyClient.Instance.serverIP = ip;
            LobbyClient.Instance.serverPort = lobby;
        }

        if (BattleClient.Instance != null)
        {
            BattleClient.Instance.serverIP = ip;
            BattleClient.Instance.serverPort = battle;
        }
    }

    /// <summary>
    /// 设置用户信息
    /// </summary>
    public void SetUserInfo(int id, string name)
    {
        userId = id;
        username = name;

        if (LobbyClient.Instance != null)
        {
            LobbyClient.Instance.userId = id;
            LobbyClient.Instance.username = name;
        }
    }
}