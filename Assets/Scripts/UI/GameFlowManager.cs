// 游戏流程管理器。管理场景切换和游戏状态。
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 游戏流程管理器。管理游戏场景的切换和整体状态。
/// </summary>
public class GameFlowManager : MonoBehaviour
{
    public enum GameState
    {
        None,
        Login,
        Lobby,
        Matching,
        LoadingBattle,
        InBattle,
        GameOver
    }

    [Header("场景名称")]
    [SerializeField] private string loginScene = "StartScene";
    [SerializeField] private string battleScene = "Fight";

    [Header("调试")]
    [SerializeField] private bool debugMode = false;
    [SerializeField] private bool skipToBattle = false;

    // 当前状态
    private GameState _currentState = GameState.None;
    public GameState CurrentState => _currentState;

    // 单例
    public static GameFlowManager Instance { get; private set; }

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
        // 初始化状态
        SetState(GameState.Login);

        // 订阅事件
        SubscribeEvents();

        // 调试模式：直接进入战斗
        if (debugMode && skipToBattle)
        {
            Debug.Log("[GameFlowManager] 调试模式：直接进入战斗");
            StartBattle();
        }
    }

    private void OnDestroy()
    {
        UnsubscribeEvents();
    }

    private void SubscribeEvents()
    {
        // 确保 BattleManager 存在
        if (BattleManager.Instance == null)
        {
            Debug.LogWarning("[GameFlowManager] BattleManager.Instance is null, will retry later");
            Invoke(nameof(SubscribeEvents), 0.1f);
            return;
        }

        BattleManager.Instance.OnBattleStart += OnBattleStart;
        BattleManager.Instance.OnGameOver += OnGameOver;
        BattleManager.Instance.OnStateChanged += OnBattleStateChanged;

        Debug.Log("[GameFlowManager] 事件订阅完成");
    }

    private void UnsubscribeEvents()
    {
        if (BattleManager.Instance != null)
        {
            BattleManager.Instance.OnBattleStart -= OnBattleStart;
            BattleManager.Instance.OnGameOver -= OnGameOver;
            BattleManager.Instance.OnStateChanged -= OnBattleStateChanged;
        }
    }

    #region 公开API

    /// <summary>
    /// 进入大厅
    /// </summary>
    public void EnterLobby()
    {
        SetState(GameState.Lobby);
    }

    /// <summary>
    /// 开始匹配
    /// </summary>
    public void StartMatching()
    {
        SetState(GameState.Matching);

        if (BattleManager.Instance != null)
        {
            BattleManager.Instance.StartMatching();
        }
    }

    /// <summary>
    /// 取消匹配
    /// </summary>
    public void CancelMatching()
    {
        if (BattleManager.Instance != null)
        {
            BattleManager.Instance.CancelMatching();
        }

        SetState(GameState.Lobby);
    }

    /// <summary>
    /// 开始战斗
    /// </summary>
    public void StartBattle()
    {
        SetState(GameState.LoadingBattle);

        // 加载战斗场景
        if (!string.IsNullOrEmpty(battleScene))
        {
            SceneManager.LoadScene(battleScene);
        }
    }

    /// <summary>
    /// 结束战斗，返回大厅
    /// </summary>
    public void EndBattle()
    {
        if (BattleManager.Instance != null)
        {
            BattleManager.Instance.EndBattle();
        }

        // 加载登录场景
        if (!string.IsNullOrEmpty(loginScene))
        {
            SceneManager.LoadScene(loginScene);
        }

        SetState(GameState.Login);
    }

    /// <summary>
    /// 退出游戏
    /// </summary>
    public void QuitGame()
    {
        // 断开连接
        if (LobbyClient.Instance != null)
        {
            LobbyClient.Instance.Disconnect();
        }

        if (BattleClient.Instance != null)
        {
            BattleClient.Instance.Disconnect();
        }

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    #endregion

    #region 事件处理

    private void OnBattleStart()
    {
        SetState(GameState.InBattle);
    }

    private void OnGameOver(int winnerTeamId)
    {
        SetState(GameState.GameOver);
    }

    private void OnBattleStateChanged(BattleManager.BattleState state)
    {
        // 可以在这里添加状态变化的处理逻辑
        Debug.Log($"[GameFlowManager] 战斗状态变化: {state}");
    }

    #endregion

    #region 状态管理

    private void SetState(GameState newState)
    {
        if (_currentState == newState) return;

        GameState oldState = _currentState;
        _currentState = newState;

        Debug.Log($"[GameFlowManager] 状态切换: {oldState} -> {newState}");

        // 可以在这里添加状态切换的处理逻辑
        switch (newState)
        {
            case GameState.Login:
                // 显示登录界面
                break;
            case GameState.Lobby:
                // 显示大厅界面
                break;
            case GameState.Matching:
                // 显示匹配界面
                break;
            case GameState.LoadingBattle:
                // 显示加载界面
                break;
            case GameState.InBattle:
                // 隐藏所有UI，开始战斗
                break;
            case GameState.GameOver:
                // 显示结算界面
                break;
        }
    }

    #endregion
}