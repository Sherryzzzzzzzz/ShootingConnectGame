using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Cinemachine;
using ShootingGame.Shared.Hero;
using ShootingGame.Shared.Protocol;
using ShootingGame.Shared.Simulation;

/// <summary>
/// 战斗管理器。统一管理战斗流程，包括匹配、进入战斗、状态管理、战斗结束。
/// </summary>
public class BattleManager : MonoBehaviour
{
    [Header("预制体")]
    [SerializeField] private GameObject localPlayerPrefab;
    [SerializeField] private GameObject remotePlayerPrefab;

    [Header("生成点")]
    [SerializeField] private Transform[] spawnPoints;

    [Header("战斗设置")]
    [SerializeField] private int playersPerTeam = 1;
    [SerializeField] private string battleScene = "Fight";

    // 战斗状态
    public enum BattleState
    {
        None,
        Matching,
        Loading,
        Playing,
        GameOver
    }

    private BattleState _state = BattleState.None;
    private BattleInfo _battleInfo;
    private int _localBattlePlayerId = -1;
    private int _localTeamId = 0;

    // 玩家实例
    private GameObject _localPlayer;
    private readonly Dictionary<int, GameObject> _remotePlayers = new Dictionary<int, GameObject>();

    // 新框架 DeltaState 接收器
    private ShootingGame.Network.Server.ClientDeltaReceiver _deltaReceiver;

    // 公开属性
    public BattleState State => _state;
    public int LocalPlayerId => _localBattlePlayerId;
    public int LocalTeamId => _localTeamId;
    public bool IsInBattle => _state == BattleState.Playing;

    // 事件
    public event Action OnMatchingStart;
    public event Action OnBattleStart;
    public event Action<int> OnGameOver; // winnerTeamId
    public event Action<BattleState> OnStateChanged;

    // 单例
    public static BattleManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        EnsureNetworkManagers();
    }

    private void EnsureNetworkManagers()
    {
        if (AttackManager.Instance == null)
        {
            var go = new GameObject("AttackManager");
            go.AddComponent<AttackManager>();
            Debug.Log("[BattleManager] 自动创建 AttackManager");
        }

        if (AuthoritySync.Instance == null)
        {
            var go = new GameObject("AuthoritySync");
            go.AddComponent<AuthoritySync>();
            Debug.Log("[BattleManager] 自动创建 AuthoritySync");
        }

        if (HitEventManager.Instance == null)
        {
            var go = new GameObject("HitEventManager");
            go.AddComponent<HitEventManager>();
            Debug.Log("[BattleManager] 自动创建 HitEventManager");
        }

        if (VisualBulletManager.Instance == null)
        {
            var go = new GameObject("VisualBulletManager");
            go.AddComponent<VisualBulletManager>();
            Debug.Log("[BattleManager] 自动创建 VisualBulletManager");
        }

        if (AudioPoolManager.Instance == null)
        {
            var go = new GameObject("AudioPoolManager");
            go.AddComponent<AudioPoolManager>();
            Debug.Log("[BattleManager] 自动创建 AudioPoolManager");
        }

        // BattleUI 在战斗开始时创建（OnBattleStartReceived），不在 Awake 中创建
        // 避免在 StartScene 中就显示战斗 UI
    }

    private void Start()
    {
        Debug.Log($"[BattleManager] Start() called. LobbyClient={LobbyClient.Instance != null}, BattleClient={BattleClient.Instance != null}, state={_state}");

        // 订阅大厅客户端事件
        if (LobbyClient.Instance != null)
        {
            LobbyClient.Instance.OnMatchFound += OnMatchFound;
            Debug.Log("[BattleManager] Subscribed to LobbyClient.OnMatchFound");

            // 如果已经匹配成功，立即处理
            if (LobbyClient.Instance.IsMatchFound && LobbyClient.Instance.MatchedBattleInfo != null)
            {
                Debug.Log("[BattleManager] MatchFound already received before Start(), processing now");
                OnMatchFound(LobbyClient.Instance.MatchedBattleInfo);
            }
        }
        else
        {
            Debug.LogError("[BattleManager] LobbyClient.Instance is null! Cannot subscribe to match events.");
        }

        // 订阅战斗客户端事件
        if (BattleClient.Instance != null)
        {
            BattleClient.Instance.OnBattleStart += OnBattleStartReceived;
            BattleClient.Instance.OnGameOver += OnGameOverReceived;
            BattleClient.Instance.OnDeltaStateReceived += OnDeltaStateReceived;

            // 如果已经在战斗中，立即处理
            if (BattleClient.Instance.IsInBattle)
            {
                OnBattleStartReceived();
            }
        }
    }

    private void OnDestroy()
    {
        if (LobbyClient.Instance != null)
        {
            LobbyClient.Instance.OnMatchFound -= OnMatchFound;
        }

        if (BattleClient.Instance != null)
        {
            BattleClient.Instance.OnBattleStart -= OnBattleStartReceived;
            BattleClient.Instance.OnGameOver -= OnGameOverReceived;
        }
    }

    #region 公开 API

    /// <summary>
    /// 开始匹配
    /// </summary>
    public void StartMatching()
    {
        if (_state != BattleState.None)
        {
            Debug.LogWarning("[BattleManager] 当前状态不允许匹配");
            return;
        }

        if (LobbyClient.Instance == null || !LobbyClient.Instance.IsLoggedIn)
        {
            Debug.LogError("[BattleManager] 未登录，无法匹配");
            return;
        }

        SetState(BattleState.Matching);
        LobbyClient.Instance.JoinQueue();

        Debug.Log("[BattleManager] 开始匹配");
        OnMatchingStart?.Invoke();
    }

    /// <summary>
    /// 取消匹配
    /// </summary>
    public void CancelMatching()
    {
        if (_state != BattleState.Matching) return;

        LobbyClient.Instance?.LeaveQueue();
        SetState(BattleState.None);

        Debug.Log("[BattleManager] 取消匹配");
    }

    /// <summary>
    /// 结束战斗并清理
    /// </summary>
    public void EndBattle()
    {
        if (_state == BattleState.None) return; // 防止重复调用

        CleanupBattle();
        SetState(BattleState.None);

        // 返回大厅场景
        SceneManager.LoadScene("StartScene");
    }

    #endregion

    #region 事件处理

    private void OnMatchFound(BattleInfo info)
    {
        // 防止重复处理 MatchFound
        if (_state == BattleState.Loading || _state == BattleState.Playing)
        {
            Debug.LogWarning($"[BattleManager] Ignoring duplicate MatchFound (current state: {_state})");
            return;
        }

        if (info == null)
        {
            Debug.LogError("[BattleManager] OnMatchFound called with null BattleInfo!");
            return;
        }

        if (info.BattlePlayers == null || info.BattlePlayers.Count == 0)
        {
            Debug.LogError("[BattleManager] BattleInfo has no BattlePlayers!");
            return;
        }

        _battleInfo = info;

        // 找到本地玩家信息：优先按用户名匹配，其次匹配 userId
        int localUserId = LobbyClient.Instance?.userId ?? 0;
        string localUsername = LobbyClient.Instance?.username ?? "";
        _localBattlePlayerId = -1;
        _localTeamId = 0;

        Debug.Log($"[BattleManager] Matching local player: localUserId={localUserId}, localUsername='{localUsername}', battlePlayers={info.BattlePlayers.Count}");

        // 优先用服务端分配的唯一 userId 匹配
        if (localUserId > 0)
        {
            foreach (var player in info.BattlePlayers)
            {
                if (player.UserId == localUserId)
                {
                    _localBattlePlayerId = player.PlayerId;
                    _localTeamId = player.TeamId;
                    Debug.Log($"[BattleManager] UserId 匹配: userId={localUserId} -> PlayerId={_localBattlePlayerId}");
                    break;
                }
            }
        }

        // 回退：按用户名匹配
        if (_localBattlePlayerId < 0 && !string.IsNullOrEmpty(localUsername))
        {
            foreach (var player in info.BattlePlayers)
            {
                if (player.PlayerName == localUsername)
                {
                    _localBattlePlayerId = player.PlayerId;
                    _localTeamId = player.TeamId;
                    Debug.Log($"[BattleManager] Username 匹配: '{localUsername}' -> PlayerId={_localBattlePlayerId}");
                    break;
                }
            }
        }

        // 最终回退（不应发生，因为 userId 已由服务端保证唯一）
        if (_localBattlePlayerId < 0 && info.BattlePlayers.Count > 0)
        {
            _localBattlePlayerId = info.BattlePlayers[0].PlayerId;
            _localTeamId = info.BattlePlayers[0].TeamId;
            Debug.LogError($"[BattleManager] 无法匹配玩家身份！userId={localUserId}, username='{localUsername}'，回退到第一个玩家 ID={_localBattlePlayerId}");
        }

        Debug.Log($"[BattleManager] 匹配成功！BattleId={info.BattleId}, PlayerId={_localBattlePlayerId}, TeamId={_localTeamId}");

        // 清除 LobbyClient 匹配状态，防止场景重载后被重新处理
        if (LobbyClient.Instance != null)
        {
            LobbyClient.Instance.IsMatchFound = false;
            LobbyClient.Instance.MatchedBattleInfo = null;
        }

        // 从 BattleInfo 加载碰撞数据（服务端下发或本地文件优先）
        if (info.CollisionData != null && info.CollisionData.Length > 0)
        {
            CollisionWorldLoader.LoadFromBytes(info.CollisionData);
            Debug.Log($"[BattleManager] 从服务器加载碰撞数据: {info.CollisionData.Length} 字节");
        }

        SetState(BattleState.Loading);

        // 显示选角面板（HeroSelectPanel 负责倒计时 + 双方锁定后回调）
        var heroPanel = FindFirstObjectByType<HeroSelectPanel>(FindObjectsInactive.Include);
        if (heroPanel != null)
        {
            Debug.Log("[BattleManager] 显示选角面板，等待双方锁定...");
            heroPanel.gameObject.SetActive(true);
            // LobbyPanel 隐藏
            var lobbyPanel = FindFirstObjectByType<LobbyPanel>(FindObjectsInactive.Include);
            if (lobbyPanel != null) lobbyPanel.gameObject.SetActive(false);

            // 等双方锁定后加载战斗场景
            StartCoroutine(WaitForHeroConfirm(heroPanel, info));
        }
        else
        {
            // 回退：没有选角面板则直接加载
            Debug.LogWarning("[BattleManager] HeroSelectPanel 未找到，直接进入战斗");
            LoadBattleSceneDirectly(info);
        }
    }

    /// <summary>
    /// 等待双方选角锁定后加载战斗场景。
    /// </summary>
    private System.Collections.IEnumerator WaitForHeroConfirm(HeroSelectPanel panel, BattleInfo info)
    {
        // 最多等 35 秒 (30s 选角 + 5s 缓冲)
        float timeout = 35f;
        while (timeout > 0 && (LobbyClient.Instance != null && !LobbyClient.Instance.HeroConfirmed))
        {
            yield return new WaitForSeconds(0.3f);
            timeout -= 0.3f;
        }

        // 等待对手也确认
        timeout = 35f;
        while (timeout > 0 && !panel.OpponentConfirmed)
        {
            yield return new WaitForSeconds(0.3f);
            timeout -= 0.3f;
        }

        LoadBattleSceneDirectly(info);
    }

    /// <summary>
    /// 直接加载战斗场景（无选角面板时回退）。
    /// </summary>
    private void LoadBattleSceneDirectly(BattleInfo info)
    {
        if (string.IsNullOrEmpty(battleScene))
        {
            Debug.LogError("[BattleManager] battleScene 为空！");
            return;
        }
        Debug.Log($"[BattleManager] 加载战斗场景: '{battleScene}'");
        StartCoroutine(LoadBattleSceneAsync(battleScene, info));
    }

    private System.Collections.IEnumerator LoadBattleSceneAsync(string sceneName, BattleInfo info)
    {
        Debug.Log($"[BattleManager] LoadBattleSceneAsync 开始: sceneName='{sceneName}', BattleId={info.BattleId}, ActiveScene='{SceneManager.GetActiveScene().name}'");

        // List all scenes in Build Settings for diagnosis
        int totalScenes = SceneManager.sceneCountInBuildSettings;
        Debug.Log($"[BattleManager] Build Settings 中共 {totalScenes} 个场景:");
        for (int i = 0; i < totalScenes; i++)
        {
            Debug.Log($"[BattleManager]   [{i}] {SceneUtility.GetScenePathByBuildIndex(i)}");
        }

        // 确保 BattleManager 已 DontDestroyOnLoad
        if (gameObject.scene.name != "DontDestroyOnLoad")
        {
            DontDestroyOnLoad(gameObject);
            Debug.Log("[BattleManager] 重新设置 DontDestroyOnLoad");
        }

        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);

        // If name-based loading fails, try to find by build index
        if (asyncLoad == null)
        {
            Debug.LogWarning($"[BattleManager] 按名称 '{sceneName}' 加载失败，尝试匹配 Build Settings 中的场景...");
            for (int i = 0; i < totalScenes; i++)
            {
                string path = SceneUtility.GetScenePathByBuildIndex(i);
                string name = System.IO.Path.GetFileNameWithoutExtension(path);
                if (string.Equals(name, sceneName, System.StringComparison.OrdinalIgnoreCase))
                {
                    Debug.Log($"[BattleManager] 找到匹配 [{i}]: {path}");
                    asyncLoad = SceneManager.LoadSceneAsync(i, LoadSceneMode.Single);
                    break;
                }
            }
        }

        // Final fallback: try buildIndex=1
        if (asyncLoad == null && totalScenes > 1)
        {
            Debug.LogWarning($"[BattleManager] 仍未匹配，回退到 buildIndex=1: {SceneUtility.GetScenePathByBuildIndex(1)}");
            asyncLoad = SceneManager.LoadSceneAsync(1, LoadSceneMode.Single);
        }

        if (asyncLoad == null)
        {
            Debug.LogError($"[BattleManager] 无法加载战斗场景！请到 File > Build Settings 添加 Fight.unity 场景。");
            SetState(BattleState.None);
            yield break;
        }

        asyncLoad.allowSceneActivation = true;

        float lastProgress = 0f;
        float loadTimeout = 0f;
        while (!asyncLoad.isDone)
        {
            float progress = Mathf.Max(asyncLoad.progress, lastProgress);
            if (progress > lastProgress + 0.01f)
            {
                Debug.Log($"[BattleManager] 场景加载进度: {progress * 100:F0}%");
                lastProgress = progress;
            }
            loadTimeout += Time.deltaTime;
            if (loadTimeout > 30f)
            {
                Debug.LogError("[BattleManager] 场景加载超时（30秒）！");
                SetState(BattleState.None);
                yield break;
            }
            yield return null;
        }

        Debug.Log($"[BattleManager] 场景加载完成！ActiveScene='{SceneManager.GetActiveScene().name}', isDone={asyncLoad.isDone}");

        // 等待一帧确保新场景中所有对象的 Awake/Start 已执行
        yield return null;
        yield return null;

        Debug.Log("[BattleManager] 两帧后，场景就绪，发送 BattleReady");
        Debug.Log($"[BattleManager] BattleClient.Instance={(BattleClient.Instance != null ? "存在" : "NULL")}, _localBattlePlayerId={_localBattlePlayerId}");

        if (BattleClient.Instance != null)
        {
            BattleClient.Instance.InitializeBattle(info, _localBattlePlayerId);
            Debug.Log("[BattleManager] BattleReady 已发送");
        }
        else
        {
            Debug.LogError("[BattleManager] BattleClient.Instance 为空！尝试恢复...");
            var go = new GameObject("BattleClient_Recovery");
            var bc = go.AddComponent<BattleClient>();
            DontDestroyOnLoad(go);
            bc.InitializeBattle(info, _localBattlePlayerId);
            Debug.Log("[BattleManager] BattleClient 已从恢复路径发送 BattleReady");
        }
    }

    private System.Collections.IEnumerator InitBattleAfterSceneLoad(BattleInfo info)
    {
        yield return null;
        Debug.Log("[BattleManager] InitBattleAfterSceneLoad (旧路径)");

        if (BattleClient.Instance != null)
        {
            BattleClient.Instance.InitializeBattle(info, _localBattlePlayerId);
        }
    }

    /// <summary>
    /// 延迟一帧重新初始化状态机。PlayerModel.Start() 在 Instantiate 时已执行，
    /// 当时 NetPlayerController 还未添加。这里等 NetPlayerController.Start() 完成后重新刷新状态引用。
    /// </summary>
    private IEnumerator ReinitStateMachinesNextFrame(PlayerModel model)
    {
        yield return null;  // 等 NetPlayerController.Start() 执行
        yield return null;  // 再等一帧确保所有组件就绪

        if (model != null)
        {
            // 使用 ReinitCurrentStates 而不是重新进入状态，因为当前状态是 idle/ground，
            // EnterState 检测到相同状态会直接 return，不会重新调用 Init
            model.ReinitCurrentStates();
            Debug.Log("[BattleManager] 状态机重新初始化完成（当前状态引用已刷新）");
        }
    }

    private void OnBattleStartReceived()
    {
        Debug.Log("[BattleManager] 战斗开始！");

        // 重新确保所有网络管理器存在（场景切换后可能已被销毁）
        EnsureNetworkManagers();

        // 在 Fight 场景中创建 BattleUI
        if (BattleUI.Instance == null)
        {
            var go = new GameObject("BattleUI");
            go.AddComponent<BattleUI>();
            Debug.Log("[BattleManager] 自动创建 BattleUI（Fight 场景）");
        }

        SetState(BattleState.Playing);

        // 生成玩家
        SpawnPlayers();

        OnBattleStart?.Invoke();
    }

    private void OnGameOverReceived(int winnerTeamId)
    {
        Debug.Log($"[BattleManager] 战斗结束！胜利队伍: {winnerTeamId}");

        SetState(BattleState.GameOver);

        OnGameOver?.Invoke(winnerTeamId);

        // 延迟清理
        Invoke(nameof(EndBattle), 3f);
    }

    /// <summary>
    /// 接收新框架 DeltaState（I帧/P帧）。
    /// </summary>
    private void OnDeltaStateReceived(byte[] payload)
    {
        if (_deltaReceiver == null)
        {
            var ecsWorld = FindFirstObjectByType<ClientECSWorld>();
            if (ecsWorld == null) return;
            _deltaReceiver = new ShootingGame.Network.Server.ClientDeltaReceiver(ecsWorld.EntityManager);
        }

        var reader = new ShootingGame.Shared.Protocol.PacketReader(payload);
        var msg = ShootingGame.Shared.Protocol.NetworkFrameSerializer.ReadDeltaState(reader);
        _deltaReceiver.OnDeltaStateReceived(msg);
    }

    #endregion

    #region 玩家生成

    private void SpawnPlayers()
    {
        if (_battleInfo == null) return;

        // 确保 HeroRegistry 已初始化
        HeroRegistry.Initialize();

        // 提前加载预制体，确保 SpawnRemotePlayer 被调用时 remotePlayerPrefab 已就绪
        if (localPlayerPrefab == null)
        {
            localPlayerPrefab = Resources.Load<GameObject>("Player");
            if (localPlayerPrefab == null)
                Debug.LogWarning("[BattleManager] Player prefab not found in Resources. Create Assets/Resources/Player.prefab");
        }
        if (remotePlayerPrefab == null)
        {
            remotePlayerPrefab = Resources.Load<GameObject>("Player");
        }

        // 清理旧玩家
        foreach (var player in _remotePlayers.Values)
        {
            if (player != null) Destroy(player);
        }
        _remotePlayers.Clear();

        if (_localPlayer != null) Destroy(_localPlayer);

        // 生成所有玩家
        foreach (var playerInfo in _battleInfo.BattlePlayers)
        {
            bool isLocal = playerInfo.PlayerId == _localBattlePlayerId;
            Vector3 spawnPos = GetSpawnPosition(playerInfo.PlayerId, playerInfo.TeamId);
            var heroConfig = HeroRegistry.GetHero(playerInfo.HeroId > 0 ? playerInfo.HeroId : HeroRegistry.DefaultHeroId);

            if (isLocal)
            {
                SpawnLocalPlayer(spawnPos, heroConfig);
            }
            else
            {
                SpawnRemotePlayer(playerInfo.PlayerId, playerInfo.TeamId, spawnPos, heroConfig);
            }
        }
    }

    private void SpawnLocalPlayer(Vector3 position, HeroConfig heroConfig = null)
    {
        // 销毁场景中预置的 Player（Fight 场景可能自带一个用于测试的 Player）
        var scenePlayer = GameObject.Find("Player");
        if (scenePlayer != null && scenePlayer != _localPlayer)
        {
            Debug.Log("[BattleManager] 销毁场景中预置的 Player");
            Destroy(scenePlayer);
        }

        if (localPlayerPrefab != null)
        {
            _localPlayer = Instantiate(localPlayerPrefab, position, Quaternion.identity);
            _localPlayer.name = $"LocalPlayer_{_localBattlePlayerId}";

            // 禁用物理碰撞体，防止玩家之间产生碰撞"吸附"
            DisablePhysicsComponents(_localPlayer);

            // 禁用预制体自带的摄像机（使用场景中的 CinemachineFreeLook 替代）
            DisableAllCameras(_localPlayer);

            // --- 组件检测 ---
            // 确保 BodyPartHitbox 存在
            if (_localPlayer.GetComponent<BodyPartHitbox>() == null)
            {
                _localPlayer.AddComponent<BodyPartHitbox>();
                Debug.Log("[BattleManager] 为本地玩家添加 BodyPartHitbox");
            }

            var controller = _localPlayer.GetComponent<NetPlayerController>();
            if (controller == null)
            {
                Debug.LogError("[BattleManager] ❌ 预制体缺少 NetPlayerController！请在 Player 预制体上添加 NetPlayerController。");
            }
            else if (heroConfig != null)
            {
                controller.HeroConfig = heroConfig;
            }

            if (_localPlayer.GetComponent<CursorControl>() == null)
            {
                Debug.LogWarning("[BattleManager] ⚠ 预制体缺少 CursorControl！请在 Player 预制体上添加 CursorControl（用于锁定鼠标）。");
            }

            // 将场景中的 CinemachineFreeLook 摄像机的 Follow/LookAt 设置到本地玩家
            SetupFreeLookCamera(_localPlayer);

            // 重新刷新摄像机引用（Start 在 Instantiate 时已执行，当时 FreeLook 尚未配置）
            if (controller != null)
            {
                controller.RefreshCamera();
            }

            // 检测 PlayerModel
            var playerModel = _localPlayer.GetComponent<PlayerModel>();
            if (playerModel == null)
                playerModel = _localPlayer.GetComponentInChildren<PlayerModel>();

            if (playerModel != null)
            {
                playerModel.isLocalPlayer = true;
                StartCoroutine(ReinitStateMachinesNextFrame(playerModel));
            }
            else
            {
                Debug.LogError("[BattleManager] ❌ 预制体缺少 PlayerModel！请在 Player 预制体上添加 PlayerModel 组件。");
            }

            Debug.Log($"[BattleManager] 生成本地玩家: {_localPlayer.name}, PlayerId={_localBattlePlayerId}, isLocalPlayer={playerModel?.isLocalPlayer}, 位置: {position}");
            return;
        }

        // 回退：查找场景中已有的 NetPlayerController
        var existingController = FindFirstObjectByType<NetPlayerController>();
        if (existingController != null)
        {
            _localPlayer = existingController.gameObject;
            _localPlayer.name = $"LocalPlayer_{_localBattlePlayerId}";
            existingController.transform.position = position;
            existingController.RefreshCamera();
            var pm = existingController.GetComponent<PlayerModel>();
            if (pm == null) pm = existingController.GetComponentInChildren<PlayerModel>();
            if (pm != null) pm.isLocalPlayer = true;
            Debug.Log($"[BattleManager] 使用场景中已有的本地玩家，位置: {position}");
            return;
        }

        // 最后回退：创建一个基础玩家对象
        Debug.LogWarning("[BattleManager] 未找到玩家预制体或场景玩家，创建基础玩家对象");
        _localPlayer = CreateFallbackLocalPlayer(position);
    }

    /// <summary>
    /// 创建基础本地玩家（无预制体时的回退方案）
    /// </summary>
    private GameObject CreateFallbackLocalPlayer(Vector3 position)
    {
        var go = new GameObject($"LocalPlayer_{_localBattlePlayerId}");
        go.transform.position = position;

        // 添加角色控制器所需的组件
        var controller = go.AddComponent<NetPlayerController>();

        // 如果场景中有 PlayerModel，从现有 Player 复制设置
        var existingPlayer = FindFirstObjectByType<PlayerModel>();
        if (existingPlayer != null)
        {
            // 使用现有 Player 的 PlayerModel（更复杂的设置，如相机、动画等）
            _localPlayer = existingPlayer.gameObject;
            existingPlayer.transform.position = position;
            existingPlayer.isLocalPlayer = true;
            _localPlayer.name = $"LocalPlayer_{_localBattlePlayerId}";
            UnityEngine.Object.Destroy(go); // 销毁刚创建的基础对象
            Debug.Log($"[BattleManager] 复用场景 PlayerModel 作为本地玩家，位置: {position}");
            return _localPlayer;
        }

        // 添加基础可视表示
        var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        capsule.transform.SetParent(go.transform, false);
        capsule.transform.localPosition = new Vector3(0, 1, 0);
        capsule.transform.localScale = new Vector3(0.6f, 1, 0.6f);
        // 移除碰撞体，使用网络模拟
        var capsuleCollider = capsule.GetComponent<CapsuleCollider>();
        if (capsuleCollider != null) UnityEngine.Object.Destroy(capsuleCollider);

        Debug.Log($"[BattleManager] 创建基础本地玩家，位置: {position}");
        return go;
    }

    private void SpawnRemotePlayer(int playerId, int teamId, Vector3 position, HeroConfig heroConfig = null)
    {
        // 防止重复生成同一个远程玩家
        if (_remotePlayers.ContainsKey(playerId))
        {
            Debug.LogWarning($"[BattleManager] 远程玩家 {playerId} 已存在，跳过重复生成");
            return;
        }

        if (remotePlayerPrefab != null)
        {
            var go = Instantiate(remotePlayerPrefab, position, Quaternion.identity);
            go.name = $"RemotePlayer_{playerId}";

            // 禁用物理碰撞体，防止玩家之间产生碰撞"吸附"
            DisablePhysicsComponents(go);

            // 禁用远程玩家的摄像机（只保留本地玩家的摄像机）
            DisableAllCameras(go);

            var controller = go.GetComponent<RemotePlayerController>();
            if (controller == null)
            {
                Debug.LogError($"[BattleManager] ❌ 预制体缺少 RemotePlayerController！请在 Player 预制体上添加 RemotePlayerController。");
                controller = go.AddComponent<RemotePlayerController>();
            }

            // 确保 BodyPartHitbox 存在
            if (go.GetComponent<BodyPartHitbox>() == null)
            {
                go.AddComponent<BodyPartHitbox>();
                Debug.Log($"[BattleManager] 为远程玩家 {playerId} 添加 BodyPartHitbox");
            }

            controller.Initialize(playerId, teamId, position, heroConfig);

            var playerModel = go.GetComponent<PlayerModel>();
            if (playerModel == null)
                playerModel = go.GetComponentInChildren<PlayerModel>();
            if (playerModel != null)
            {
                playerModel.isLocalPlayer = false;
            }

            _remotePlayers[playerId] = go;
            Debug.Log($"[BattleManager] 从预制体生成远程玩家 {playerId}，位置: {position}");
            return;
        }

        // 回退：创建基础远程玩家
        var remoteGo = CreateFallbackRemotePlayer(playerId, teamId, position, heroConfig);
        _remotePlayers[playerId] = remoteGo;
        Debug.Log($"[BattleManager] 创建基础远程玩家 {playerId}，队伍 {teamId}，位置: {position}");
    }

    /// <summary>
    /// 创建基础远程玩家（无预制体时的回退方案）
    /// </summary>
    private GameObject CreateFallbackRemotePlayer(int playerId, int teamId, Vector3 position, HeroConfig heroConfig = null)
    {
        var go = new GameObject($"RemotePlayer_{playerId}");
        go.transform.position = position;

        // 添加 RemotePlayerController
        var controller = go.AddComponent<RemotePlayerController>();

        // 基础胶囊体可视化
        var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        capsule.transform.SetParent(go.transform, false);
        capsule.transform.localPosition = new Vector3(0, 1, 0);
        capsule.transform.localScale = new Vector3(0.6f, 1, 0.6f);
        var capsuleCollider = capsule.GetComponent<CapsuleCollider>();
        if (capsuleCollider != null) UnityEngine.Object.Destroy(capsuleCollider);

        // 设置队伍颜色
        var renderer = capsule.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            Color teamColor = teamId == 1 ? Color.blue : Color.red;
            renderer.material.color = teamColor;
        }

        // 初始化
        controller.Initialize(playerId, teamId, position, heroConfig);

        return go;
    }

    private Vector3 GetSpawnPosition(int playerId, int teamId)
    {
        // 优先使用服务器下发的出生点（BattleInfo.SpawnPoints）
        if (_battleInfo != null && _battleInfo.SpawnPoints != null && _battleInfo.SpawnPoints.Count > 0)
        {
            // 先找队伍专属出生点
            var teamSpawns = new List<SpawnPointMsg>();
            var anySpawns = new List<SpawnPointMsg>();
            foreach (var sp in _battleInfo.SpawnPoints)
            {
                if (sp.TeamId == teamId) teamSpawns.Add(sp);
                else if (sp.TeamId == 0) anySpawns.Add(sp);
            }

            var pool = teamSpawns.Count > 0 ? teamSpawns : (anySpawns.Count > 0 ? anySpawns : null);
            if (pool != null)
            {
                var sp = pool[playerId % pool.Count];
                return new Vector3(sp.Position.x, sp.Position.y, sp.Position.z);
            }
        }

        // Fallback 1: Inspector 中配置的 Transform[]
        if (spawnPoints != null && spawnPoints.Length > 0)
        {
            int index = playerId % spawnPoints.Length;
            if (spawnPoints[index] != null)
                return spawnPoints[index].position;
        }

        // Fallback 2: 基于队伍ID的默认位置
        float x = (teamId == 1) ? -5f : 5f;
        float z = playerId * 2f;
        return new Vector3(x, 0.1f, z);
    }

    #endregion

    #region 清理

    private void CleanupBattle()
    {
        // 销毁本地玩家
        if (_localPlayer != null)
        {
            Destroy(_localPlayer);
            _localPlayer = null;
        }

        // 销毁远程玩家
        foreach (var player in _remotePlayers.Values)
        {
            if (player != null) Destroy(player);
        }
        _remotePlayers.Clear();

        // 清理网络状态
        if (AttackManager.Instance != null)
            AttackManager.Instance.Clear();

        if (HitEventManager.Instance != null)
            HitEventManager.Instance.Clear();

        if (VisualBulletManager.Instance != null)
            VisualBulletManager.Instance.ClearAll();

        if (AuthoritySync.Instance != null)
            AuthoritySync.Instance.Reset();

        // 断开战斗连接
        if (BattleClient.Instance != null)
        {
            BattleClient.Instance.Disconnect();
        }

        // 清除匹配状态，防止返回大厅后被重新处理
        if (LobbyClient.Instance != null)
        {
            LobbyClient.Instance.IsMatchFound = false;
            LobbyClient.Instance.MatchedBattleInfo = null;
            LobbyClient.Instance.IsInQueue = false;
        }

        _battleInfo = null;
        _localBattlePlayerId = -1;
        _localTeamId = 0;

        Debug.Log("[BattleManager] 战斗清理完成");
    }

    #endregion

    #region 状态管理

    private void SetState(BattleState newState)
    {
        if (_state == newState) return;

        _state = newState;
        OnStateChanged?.Invoke(newState);

        Debug.Log($"[BattleManager] 状态切换: {newState}");
    }

    #endregion

    #region 辅助方法

    /// <summary>
    /// 禁用 GameObject 及其子对象上的所有物理碰撞体。
    /// 联网角色的位置完全由模拟驱动，Unity 物理碰撞会导致"吸附"效果。
    /// </summary>
    private void DisablePhysicsComponents(GameObject go)
    {
        // 销毁 CharacterController（联网角色无需物理移动）
        var cc = go.GetComponent<CharacterController>();
        if (cc != null)
        {
            UnityEngine.Object.Destroy(cc);
            Debug.Log($"[BattleManager] 销毁 CharacterController on {go.name}");
        }

        // 禁用所有 Collider（胶囊体、盒子等），包括子对象
        var colliders = go.GetComponentsInChildren<Collider>(includeInactive: true);
        foreach (var col in colliders)
        {
            UnityEngine.Object.Destroy(col);
            Debug.Log($"[BattleManager] 销毁 Collider ({col.GetType().Name}) on {col.gameObject.name}");
        }

        // 禁用/销毁所有 Rigidbody（包括子对象）
        var rigidbodies = go.GetComponentsInChildren<Rigidbody>(includeInactive: true);
        foreach (var rb in rigidbodies)
        {
            UnityEngine.Object.Destroy(rb);
            Debug.Log($"[BattleManager] 销毁 Rigidbody on {rb.gameObject.name}");
        }

        // 将玩家及其子对象移到 IgnoreRaycast 层，避免与其他玩家碰撞
        var playerLayer = LayerMask.NameToLayer("Ignore Raycast");
        go.layer = playerLayer;
        foreach (var t in go.GetComponentsInChildren<Transform>(includeInactive: true))
        {
            t.gameObject.layer = playerLayer;
        }
    }

    /// <summary>
    /// 禁用 GameObject 及其子对象上的所有摄像机。
    /// 避免场景中有多个 MainCamera 导致 Camera.main 返回错误的摄像机。
    /// </summary>
    private void DisableAllCameras(GameObject go)
    {
        // 禁用所有 CinemachineFreeLook（包含其下的 Camera）
        var freeLooks = go.GetComponentsInChildren<CinemachineFreeLook>(includeInactive: true);
        foreach (var fl in freeLooks)
        {
            fl.gameObject.SetActive(false);
        }

        // 禁用所有 Camera 组件
        var cameras = go.GetComponentsInChildren<Camera>(includeInactive: true);
        foreach (var cam in cameras)
        {
            cam.tag = "Untagged";
            cam.enabled = false;
        }

        // 禁用 AudioListener（场景中应只有一个）
        var audioListener = go.GetComponentInChildren<AudioListener>(includeInactive: true);
        if (audioListener != null)
            audioListener.enabled = false;
    }

    /// <summary>
    /// 将场景中的 CinemachineFreeLook 摄像机的 Follow/LookAt 设置为本地玩家。
    /// 所有参数（轨道、灵敏度等）由用户在 Inspector 中的 CinemachineFreeLook 和
    /// CinemachineInputAxisController 组件上配置，代码不做修改。
    /// </summary>
    private void SetupFreeLookCamera(GameObject localPlayer)
    {
        var freeLooks = FindObjectsOfType<CinemachineFreeLook>();

        if (freeLooks.Length == 0)
        {
            Debug.LogWarning("[BattleManager] ⚠ 场景中没有 CinemachineFreeLook！将自动创建默认 FreeLook Camera。");
            CreateDefaultFreeLookCamera(localPlayer);
            return;
        }

        // 只改 Follow/LookAt，不动任何其他参数
        var freeLook = freeLooks[0];
        freeLook.Follow = localPlayer.transform;
        freeLook.LookAt = localPlayer.transform;

        // 将 FreeLook 所在的 Camera 标记为 MainCamera
        var freeLookCamera = freeLook.GetComponentInChildren<Camera>();
        if (freeLookCamera != null && freeLookCamera.CompareTag("MainCamera") == false)
        {
            freeLookCamera.tag = "MainCamera";
        }

        Debug.Log($"[BattleManager] FreeLook 已关联: Follow={localPlayer.name}, Camera={freeLook.name}");
    }

    /// <summary>
    /// 创建默认的 CinemachineFreeLook 摄像机（当场景中不存在时）。
    /// 使用 CM3 原生 CinemachineInputAxisController，用户需在 Inspector 中配置输入绑定。
    /// </summary>
    private void CreateDefaultFreeLookCamera(GameObject localPlayer)
    {
        var freeLookGo = new GameObject("FreeLook Camera");
        var freeLook = freeLookGo.AddComponent<CinemachineFreeLook>();
        var camera = freeLookGo.AddComponent<Camera>();
        camera.tag = "MainCamera";

        // 添加 CinemachineBrain
        var brain = FindFirstObjectByType<CinemachineBrain>();
        if (brain == null)
        {
            brain = freeLookGo.AddComponent<CinemachineBrain>();
        }

        freeLook.Follow = localPlayer.transform;
        freeLook.LookAt = localPlayer.transform;

        Debug.Log("[BattleManager] 创建默认 FreeLook Camera（请在 Inspector 中配置 CinemachineInputAxisController 的输入绑定和灵敏度）");
    }

    /// <summary>
    /// 获取玩家状态
    /// </summary>
    public PlayerStateMsg GetPlayerState(int playerId)
    {
        if (AuthoritySync.Instance != null)
        {
            var state = AuthoritySync.Instance.GetPlayerState(playerId);
            if (state != null)
            {
                return new PlayerStateMsg
                {
                    PlayerId = state.PlayerId,
                    Position = state.Position,
                    Hp = state.Hp,
                    IsDead = state.IsDead
                };
            }
        }
        return null;
    }

    /// <summary>
    /// 检查是否是本地玩家
    /// </summary>
    public bool IsLocalPlayer(int playerId)
    {
        return playerId == _localBattlePlayerId;
    }

    /// <summary>
    /// 检查是否是队友
    /// </summary>
    public bool IsTeammate(int playerId)
    {
        if (AuthoritySync.Instance != null)
        {
            var state = AuthoritySync.Instance.GetPlayerState(playerId);
            // 简化：队伍 ID 相同即为队友
            // 实际应从 PlayerState 中获取 TeamId
        }
        return false;
    }

    #endregion
}