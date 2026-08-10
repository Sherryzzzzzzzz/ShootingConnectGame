// 场景启动配置。挂载到场景中，确保所有必要组件存在。
using UnityEngine;

/// <summary>
/// 场景启动器。确保场景中有所有必要的组件。
/// 在编辑器中使用：菜单 → 游戏 → 配置当前场景
/// </summary>
public class SceneBootstrapper : MonoBehaviour
{
    [Header("自动创建")]
    [SerializeField] private bool createGameInitializer = true;
    [SerializeField] private bool createGameFlowManager = true;
    [SerializeField] private bool createMainThreadDispatcher = true;
    [SerializeField] private bool createLobbyClient = true;
    [SerializeField] private bool createBattleClient = true;
    [SerializeField] private bool createBattleManager = true;

    [Header("服务器设置")]
    [SerializeField] private string serverIP = "127.0.0.1";
    [SerializeField] private int lobbyPort = 7778;
    [SerializeField] private int battlePort = 7777;

    [Header("用户设置")]
    [SerializeField] private int userId = 1;
    [SerializeField] private string username = "Player";

    [ContextMenu("配置场景")]
    public void ConfigureScene()
    {
        // 确保主线程调度器存在
        if (createMainThreadDispatcher && UnityMainThreadDispatcher.Instance == null)
        {
            var go = new GameObject("UnityMainThreadDispatcher");
            go.AddComponent<UnityMainThreadDispatcher>();
            Debug.Log("[SceneBootstrapper] 创建 UnityMainThreadDispatcher");
        }

        // 确保 GameInitializer 存在
        if (createGameInitializer && GameInitializer.Instance == null)
        {
            var go = new GameObject("GameInitializer");
            var initializer = go.AddComponent<GameInitializer>();
            initializer.SetServerAddress(serverIP, lobbyPort, battlePort);
            initializer.SetUserInfo(userId, username);
            Debug.Log("[SceneBootstrapper] 创建 GameInitializer");
        }

        // 确保 GameFlowManager 存在
        if (createGameFlowManager && GameFlowManager.Instance == null)
        {
            var go = new GameObject("GameFlowManager");
            go.AddComponent<GameFlowManager>();
            Debug.Log("[SceneBootstrapper] 创建 GameFlowManager");
        }

        // 确保 LobbyClient 存在
        if (createLobbyClient && LobbyClient.Instance == null)
        {
            var go = new GameObject("LobbyClient");
            var client = go.AddComponent<LobbyClient>();
            client.serverIP = serverIP;
            client.serverPort = lobbyPort;
            client.userId = userId;
            client.username = username;
            Debug.Log("[SceneBootstrapper] 创建 LobbyClient");
        }

        // 确保 BattleClient 存在
        if (createBattleClient && BattleClient.Instance == null)
        {
            var go = new GameObject("BattleClient");
            var client = go.AddComponent<BattleClient>();
            client.serverIP = serverIP;
            client.serverPort = battlePort;
            Debug.Log("[SceneBootstrapper] 创建 BattleClient");
        }

        // 确保 BattleManager 存在
        if (createBattleManager && BattleManager.Instance == null)
        {
            var go = new GameObject("BattleManager");
            go.AddComponent<BattleManager>();
            Debug.Log("[SceneBootstrapper] 创建 BattleManager");
        }

        // 确保其他必要的管理器存在
        EnsureManager<DynamicTickSystem>("DynamicTickSystem");
        EnsureManager<ClientBulletSystem>("ClientBulletSystem");
        EnsureManager<HitEventView>("HitEventView");
        EnsureManager<AuthoritySync>("AuthoritySync");
        EnsureManager<ProceduralEffectManager>("ProceduralEffectManager");

        // 确保卡通后处理 Global Volume 存在
        if (FindFirstObjectByType<AutoPostFXSetup>() == null)
        {
            var go = new GameObject("AutoPostFXSetup");
            go.AddComponent<AutoPostFXSetup>();
            Debug.Log("[SceneBootstrapper] 创建 AutoPostFXSetup (卡通后处理)");
        }

        Debug.Log("[SceneBootstrapper] 场景配置完成！");
    }

    private void EnsureManager<T>(string name) where T : Component
    {
        var instance = FindObjectOfType<T>();
        if (instance == null)
        {
            var go = new GameObject(name);
            go.AddComponent<T>();
        }
    }

    private void Awake()
    {
        ConfigureScene();
    }
}