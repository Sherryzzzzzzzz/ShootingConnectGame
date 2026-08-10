using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Cinemachine;
using ShootingGame.Shared.Hero;
using ShootingGame.Shared.Physics;
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
    public IReadOnlyDictionary<int, GameObject> RemotePlayers => _remotePlayers;

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
        if (AuthoritySync.Instance == null)
        {
            var go = new GameObject("AuthoritySync");
            go.AddComponent<AuthoritySync>();
            Debug.Log("[BattleManager] 自动创建 AuthoritySync");
        }

        if (ClientBulletSystem.Instance == null)
        {
            var go = new GameObject("ClientBulletSystem");
            go.AddComponent<ClientBulletSystem>();
            Debug.Log("[BattleManager] 自动创建 ClientBulletSystem");
        }

        if (HitEventView.Instance == null)
        {
            var go = new GameObject("HitEventView");
            go.AddComponent<HitEventView>();
            Debug.Log("[BattleManager] 自动创建 HitEventView");
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
            LobbyClient.Instance.OnStartEnterBattle += OnStartEnterBattleReceived;
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
            LobbyClient.Instance.OnStartEnterBattle -= OnStartEnterBattleReceived;
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

    /// <summary>全员确认后服务端广播 StartEnterBattle → 统一进入战斗场景</summary>
    private void OnStartEnterBattleReceived(int battleId)
    {
        Debug.Log($"[BattleManager] StartEnterBattle received, battleId={battleId}");
        if (_state == BattleState.Loading && _battleInfo != null)
        {
            // 停止可能仍在运行的选角等待协程，直接加载
            StopAllCoroutines();
            LoadBattleSceneDirectly(_battleInfo);
        }
    }

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

        // 碰撞数据：优先用服务端下发的（如有），否则用本地 StreamingAssets/collision.bin（CollisionWorldLoader.Awake 已加载）
        if (info.CollisionData != null && info.CollisionData.Length > 0)
        {
            CollisionWorldLoader.LoadFromBytes(info.CollisionData);
            Debug.Log($"[BattleManager] 从服务器加载碰撞数据: {info.CollisionData.Length} 字节");
        }
        else
        {
            Debug.Log($"[BattleManager] 使用本地碰撞数据: boxes={CollisionWorldLoader.Instance?.Count ?? 0}");
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

        // 确保场景只有一个活动渲染相机和一个 AudioListener（防止重复相机导致渲染/音频异常）
        EnsureSingleCameraAndListener();

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

    /// <summary>
    /// 战斗场景只保留一个活动渲染相机和一个 AudioListener。
    /// 场景中可能存在多套相机 rig（如 EVM + Cinema），会同时渲染并产生双音频监听警告。
    /// </summary>
    private void EnsureSingleCameraAndListener()
    {
        var allCams = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
        Camera keepCam = null;
        foreach (var cam in allCams)
        {
            if (cam.enabled && cam.gameObject.activeInHierarchy)
            {
                keepCam = cam;
                break;
            }
        }

        // 禁用除主渲染相机外的相机 GameObject（其上的 AudioListener 也随之失效）
        int disabledCameras = 0;
        foreach (var cam in allCams)
        {
            if (cam == keepCam || !cam.gameObject.activeInHierarchy) continue;
            cam.gameObject.SetActive(false);
            disabledCameras++;
        }

        // 兜底：确保只剩一个活动 AudioListener
        var listeners = UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
        bool kept = false;
        int disabledListeners = 0;
        foreach (var listener in listeners)
        {
            if (!listener.gameObject.activeInHierarchy || !listener.enabled) continue;
            if (!kept) { kept = true; continue; }
            listener.enabled = false;
            disabledListeners++;
        }

        Debug.Log($"[BattleManager] 相机/音频修正: 保留相机={(keepCam != null ? keepCam.name : "无")}, " +
                  $"禁用相机={disabledCameras}, 禁用监听器={disabledListeners}");
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

        // 优先使用英雄配置的 HeroPrefab（如 PistolGirl_Player），没有则用默认 Player
        var prefabToUse = (heroConfig != null && heroConfig.HeroPrefab != null)
            ? heroConfig.HeroPrefab
            : localPlayerPrefab;
        Debug.Log($"[BattleManager] SpawnLocalPlayer: heroConfig.HeroPrefab={(heroConfig?.HeroPrefab != null ? heroConfig.HeroPrefab.name : "NULL")} → using prefab: {(prefabToUse != null ? prefabToUse.name : "NULL")}");
        if (prefabToUse != null)
        {
            _localPlayer = Instantiate(prefabToUse, position, Quaternion.identity);
            _localPlayer.name = $"LocalPlayer_{_localBattlePlayerId}";

            // 禁用物理碰撞体，防止玩家之间产生碰撞"吸附"
            DisablePhysicsComponents(_localPlayer);

            // 更新 PG 状态机的 capsule 引用（根 Collider 不再被 Destroy，引用仍然有效）

            // 禁用预制体自带的摄像机（使用场景中的 CinemachineFreeLook 替代）
            DisableAllCameras(_localPlayer);

            // --- 组件检测 ---
            // 确保 BodyPartHitbox 存在
            if (_localPlayer.GetComponent<BodyPartHitbox>() == null)
            {
                _localPlayer.AddComponent<BodyPartHitbox>();
                Debug.Log("[BattleManager] 为本地玩家添加 BodyPartHitbox");
            }

            // 确保 PlayerAnimationView + AnimancerComponent 存在（表现层薄壳）
            if (_localPlayer.GetComponent<Animancer.AnimancerComponent>() == null)
                _localPlayer.AddComponent<Animancer.AnimancerComponent>();
            // Animancer 驱动 Body 上的 Animator
            var bodyAnim = _localPlayer.GetComponentInChildren<Animator>(true);
            if (bodyAnim != null)
            {
                _localPlayer.GetComponent<Animancer.AnimancerComponent>().Animator = bodyAnim;
                bodyAnim.runtimeAnimatorController = null; // 先清空 Controller，防止默认状态（如 Death）被播放
                bodyAnim.applyRootMotion = false;
                bodyAnim.enabled = true;
                Debug.Log($"[BattleManager] Body Animator: enabled={bodyAnim.enabled}, avatar={bodyAnim.avatar != null}, hasController={bodyAnim.runtimeAnimatorController != null}");
            }
            var animView = _localPlayer.GetComponent<PlayerAnimationView>();
            if (animView == null) animView = _localPlayer.AddComponent<PlayerAnimationView>();
            animView.animSet = Resources.Load<PlayerAnimationSet>("PistolGirl_AnimSet");
            animView.capsule = _localPlayer.GetComponent<CapsuleCollider>();
            animView.BindCamera(); // 只在本地玩家接管相机（远程玩家不调用）
            Debug.Log("[BattleManager] AnimationView 已配置: animSet=" + (animView.animSet != null));

            if (_localPlayer.GetComponent<CursorControl>() == null)
            {
                Debug.LogWarning("[BattleManager] ⚠ 预制体缺少 CursorControl！请在 Player 预制体上添加 CursorControl（用于锁定鼠标）。");
            }

            // 将场景中的 CinemachineFreeLook 摄像机的 Follow/LookAt 设置到本地玩家
            SetupFreeLookCamera(_localPlayer);

            // 注册到 ECS 世界（含表现组件）
            var world = FindFirstObjectByType<ClientECSWorld>();
            if (world == null)
            {
                var go = new GameObject("ClientECSWorld");
                world = go.AddComponent<ClientECSWorld>();
            }
            world.RegisterLocalPlayer(_localBattlePlayerId, position.ToShared(), heroConfig, _localPlayer);

            // 应用选角界面的外观选择
            HeroAppearanceApplier.Apply(_localPlayer,
                CharacterSelectUI.SelectedOutfitIndex,
                CharacterSelectUI.SelectedGunColor);

            Debug.Log($"[BattleManager] 生成本地玩家: {_localPlayer.name}, PlayerId={_localBattlePlayerId}, 位置: {position}");
            return;
        }

        // 回退：查找场景中已有的 ClientECSWorld 本地玩家
        var existingWorld = FindFirstObjectByType<ClientECSWorld>();
        if (existingWorld != null && existingWorld.LocalPlayerId >= 0)
        {
            _localPlayer = FindFirstObjectByType<PlayerAnimationView>()?.gameObject;
            if (_localPlayer == null)
            {
                _localPlayer = new GameObject($"LocalPlayer_{_localBattlePlayerId}");
            }
            _localPlayer.name = $"LocalPlayer_{_localBattlePlayerId}";
            _localPlayer.transform.position = position;

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

        // 添加表现层薄壳
        var animView = go.AddComponent<PlayerAnimationView>();
        animView.animSet = Resources.Load<PlayerAnimationSet>("PistolGirl_AnimSet");

        // 添加基础可视表示
        var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        capsule.transform.SetParent(go.transform, false);
        capsule.transform.localPosition = new Vector3(0, 1, 0);
        capsule.transform.localScale = new Vector3(0.6f, 1, 0.6f);
        // 移除碰撞体，使用网络模拟
        var capsuleCollider = capsule.GetComponent<CapsuleCollider>();
        if (capsuleCollider != null) UnityEngine.Object.Destroy(capsuleCollider);

        // 注册到 ECS 世界
        var world = FindFirstObjectByType<ClientECSWorld>();
        if (world == null)
        {
            var go2 = new GameObject("ClientECSWorld");
            world = go2.AddComponent<ClientECSWorld>();
        }
        world.RegisterLocalPlayer(_localBattlePlayerId, position.ToShared(), null, go);

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

        var prefabToUse = (heroConfig != null && heroConfig.HeroPrefab != null)
            ? heroConfig.HeroPrefab
            : remotePlayerPrefab;
        if (prefabToUse != null)
        {
            var go = Instantiate(prefabToUse, position, Quaternion.identity);
            go.name = $"RemotePlayer_{playerId}";

            // 禁用物理碰撞体，防止玩家之间产生碰撞"吸附"
            DisablePhysicsComponents(go);

            // 禁用远程玩家的摄像机（只保留本地玩家的摄像机）
            DisableAllCameras(go);

            // 确保 BodyPartHitbox 存在
            if (go.GetComponent<BodyPartHitbox>() == null)
            {
                go.AddComponent<BodyPartHitbox>();
                Debug.Log($"[BattleManager] 为远程玩家 {playerId} 添加 BodyPartHitbox");
            }

            // 远程玩家：确保 Animancer + PlayerAnimationView（从 AnimSet 读动画，表现层薄壳）
            var remoteAnim = go.GetComponentInChildren<Animator>(true);
            if (remoteAnim != null)
            {
                remoteAnim.runtimeAnimatorController = null; // 先清空 Controller，防止默认状态闪现
                remoteAnim.applyRootMotion = false;
                remoteAnim.enabled = true;
                var remoteAnimancer = go.GetComponent<Animancer.AnimancerComponent>();
                if (remoteAnimancer == null)
                    remoteAnimancer = go.AddComponent<Animancer.AnimancerComponent>();
                remoteAnimancer.Animator = remoteAnim;
                var animView = go.GetComponent<PlayerAnimationView>();
                if (animView == null)
                    animView = go.AddComponent<PlayerAnimationView>();
                animView.animSet = Resources.Load<PlayerAnimationSet>("PistolGirl_AnimSet");
            }

            // 注册到 ECS 世界（远程玩家实体 + 表现组件）
            var world = FindFirstObjectByType<ClientECSWorld>();
            if (world == null)
            {
                var wgo = new GameObject("ClientECSWorld");
                world = wgo.AddComponent<ClientECSWorld>();
            }
            world.RegisterRemotePlayer(playerId, position.ToShared(), heroConfig, go);

            // 表现薄壳：初始化静态字典（NetworkDebugOverlay 遍历用）
            var remoteCtrl = go.GetComponent<RemotePlayerController>();
            if (remoteCtrl != null)
                remoteCtrl.Initialize(playerId, teamId, position, heroConfig);

            // TODO: 远程玩家外观需要从网络同步（对方的 outfitIndex / gunColor）
            // 当前远程玩家使用默认外观

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

        // 表现层薄壳
        var animView = go.AddComponent<PlayerAnimationView>();
        animView.animSet = Resources.Load<PlayerAnimationSet>("PistolGirl_AnimSet");

        // 表现薄壳（静态字典注册）
        go.AddComponent<RemotePlayerController>();

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

        // 注册到 ECS 世界
        var world = FindFirstObjectByType<ClientECSWorld>();
        if (world == null)
        {
            var wgo = new GameObject("ClientECSWorld");
            world = wgo.AddComponent<ClientECSWorld>();
        }
        world.RegisterRemotePlayer(playerId, position.ToShared(), heroConfig, go);

        return go;
    }
    private Vector3 GetSpawnPosition(int playerId, int teamId)
    {
        var world = CollisionWorldLoader.Instance;
        var candidates = new List<Vector3>();

        // 优先使用服务器下发的出生点（BattleInfo.SpawnPoints）
        if (_battleInfo != null && _battleInfo.SpawnPoints != null && _battleInfo.SpawnPoints.Count > 0)
        {
            foreach (var sp in _battleInfo.SpawnPoints)
            {
                if (sp.TeamId == teamId || sp.TeamId == 0)
                    candidates.Add(new Vector3(sp.Position.x, sp.Position.y, sp.Position.z));
            }
        }

        // Fallback 1: SpawnPoints.json
        if (candidates.Count == 0)
        {
            try
            {
                var cfg = Resources.Load<TextAsset>("SpawnPoints");
                if (cfg != null)
                {
                    var wrapper = JsonUtility.FromJson<SpawnPointsWrapper>(cfg.text);
                    if (wrapper?.spawnPoints?.Count > 0)
                    {
                        foreach (var sp in wrapper.spawnPoints)
                            candidates.Add(new Vector3(sp.x, sp.y, sp.z));
                    }
                }
            }
            catch { }
        }

        // Fallback 2: Inspector Transform[]
        if (candidates.Count == 0 && spawnPoints != null)
        {
            foreach (var t in spawnPoints)
                if (t != null) candidates.Add(t.position);
        }

        if (candidates.Count > 0)
        {
            // 打乱顺序后逐个检查合法性，选第一个合法的
            Shuffle(candidates);
            for (int i = 0; i < candidates.Count; i++)
            {
                if (SpawnValidator.IsSpawnValid(candidates[i], world))
                {
                    Debug.Log($"[Spawn] Player {playerId} → 合法出生点 ({candidates[i].x:F1},{candidates[i].z:F1}) idx={i}/{candidates.Count}");
                    return candidates[i];
                }
            }

            // 全部不合法 → 从随机一个出发螺旋搜索最近合法点
            var fallback = candidates[0];
            Debug.LogWarning($"[Spawn] Player {playerId}: {candidates.Count} 个预设出生点全部不合法，从 ({fallback.x:F1},{fallback.z:F1}) 开始搜索...");
            return SpawnValidator.FindNearestValidSpawn(fallback, world);
        }

        // 最终回退
        float x = (teamId == 1) ? -5f : 5f;
        float z = playerId * 2f;
        var finalFallback = new Vector3(x, 0.1f, z);
        return world != null ? SpawnValidator.FindNearestValidSpawn(finalFallback, world) : finalFallback;
    }

    /// <summary>Fisher-Yates shuffle</summary>
    private static void Shuffle<T>(System.Collections.Generic.IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
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
        if (ClientECSWorld.Instance != null)
        {
            var world = ClientECSWorld.Instance;
            var entity = world.GetLocalPlayerEntity();
            if (world.EntityManager.IsValid(entity))
                ClientAttackSystem.Clear(world.EntityManager, entity);
            world.ClearAll();
        }

        ClientHitEventSystem.Clear();

        if (ClientBulletSystem.Instance != null)
            ClientBulletSystem.Instance.ClearAll();

        if (AuthoritySync.Instance != null)
            AuthoritySync.Instance.Reset();

        // 销毁 BattleUI（DontDestroyOnLoad，会覆盖大厅UI阻挡点击）
        if (BattleUI.Instance != null)
        {
            Destroy(BattleUI.Instance.gameObject);
        }

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
            LobbyClient.Instance.HeroConfirmed = false;
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

        // 禁用子对象上的 Collider（保留根上的 CapsuleCollider，蹲伏/角色碰撞需要它）
        var colliders = go.GetComponentsInChildren<Collider>(includeInactive: true);
        foreach (var col in colliders)
        {
            if (col.gameObject == go) continue; // 保留根 GameObject 上的
            UnityEngine.Object.Destroy(col);
            Debug.Log($"[BattleManager] 销毁子 Collider ({col.GetType().Name}) on {col.gameObject.name}");
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
        var allCams = FindObjectsOfType<CinemachineFreeLook>(true);
        foreach (var cam in allCams)
        {
            cam.Follow = localPlayer.transform;
            cam.LookAt = localPlayer.transform;
            Debug.Log($"[BattleManager] Camera '{cam.name}' Follow/LookAt → {localPlayer.name}");
        }
        if (allCams.Length == 0)
            Debug.LogWarning("[BattleManager] 场景中无 CinemachineFreeLook");
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

    [System.Serializable]
    private class SpawnPointsWrapper { public List<SpawnEntry> spawnPoints; }
    [System.Serializable]
    private class SpawnEntry { public float x; public float y; public float z; public float yaw; public int teamId; }
}