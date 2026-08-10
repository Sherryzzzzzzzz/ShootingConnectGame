using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using ShootingGame.Shared.Protocol;

/// <summary>
/// TCP Lobby Client for Unity.
/// Handles connection to lobby server, login, and matchmaking.
/// </summary>
public class LobbyClient : MonoBehaviour
{
    [Header("Connection")]
    public string serverIP = "127.0.0.1";
    public int serverPort = 7778;

    [Header("User Info")]
    public int userId = 1;
    public string username = "Player";

    // State
    public bool IsConnected { get; private set; }
    public bool IsLoggedIn { get; private set; }
    public bool IsInQueue { get; set; }
    public bool IsMatchFound { get; set; }
    public BattleInfo MatchedBattleInfo { get; set; }

    // Hero selection
    public int SelectedHeroId { get; set; } = ShootingGame.Shared.Hero.HeroRegistry.DefaultHeroId;
    public bool HeroConfirmed { get; set; }

    // Room state
    public bool IsInRoom { get; private set; }
    public int CurrentRoomId { get; private set; }
    public List<RoomInfo> RoomList { get; private set; } = new List<RoomInfo>();

    // Network
    private TcpChannel _channel;
    private volatile bool _running;

    // Events
    public event Action OnConnected;
    public event Action OnDisconnected;
    public event Action<bool, string> OnLoginResult;
    public event Action<bool, string> OnJoinQueueResult;
    public event Action<BattleInfo> OnMatchFound;
    public event Action<int> OnOnlinePlayerCountChanged;
    public event Action<int> OnHeroSelected;    // 对方选角通知
    public event Action OnHeroConfirmed;        // 对方锁定通知
    public event Action<int> OnStartEnterBattle; // 全员确认后进入战斗

    /// <summary>最后一次 JoinQueue 失败的错误消息（供 UI 查询）。</summary>
    public string LastJoinQueueError { get; private set; }

    // Room events
    public event Action<List<RoomInfo>> OnRoomListReceived;
    public event Action<bool, string, RoomInfo> OnRoomCreated;
    public event Action<bool, string> OnRoomJoined;
    public event Action<bool> OnRoomLeft;

    // Singleton
    public static LobbyClient Instance { get; private set; }

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

    private void OnDestroy()
    {
        Disconnect();
    }

    public void Connect()
    {
        if (IsConnected) return;

        try
        {
            _channel = new TcpChannel();
            _channel.OnFrameReceived += OnFrameReceived;
            _channel.OnDisconnected += OnChannelDisconnected;

            if (_channel.Connect(serverIP, serverPort))
            {
                _running = true;
                IsConnected = true;
                Debug.Log($"[LobbyClient] Connected to {serverIP}:{serverPort}");
                OnConnected?.Invoke();
            }
            else
            {
                Debug.LogError($"[LobbyClient] Connection failed: timeout");
                IsConnected = false;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[LobbyClient] Connection failed: {ex.Message}");
            IsConnected = false;
        }
    }

    public void Disconnect()
    {
        if (!IsConnected) return;

        _running = false;
        bool wasLoggedIn = IsLoggedIn;
        IsConnected = false;
        IsLoggedIn = false;
        IsInQueue = false;
        IsMatchFound = false;
        IsInRoom = false;
        CurrentRoomId = 0;
        RoomList.Clear();

        try { _channel?.Close(); }
        catch { }

        // 只有在之前已连接时才打印日志和触发事件
        Debug.Log("[LobbyClient] Disconnected");
        OnDisconnected?.Invoke();
    }

    private void OnFrameReceived(byte[] data)
    {
        if (!_running) return;

        try
        {
            var pack = ProtobufSerializer.DeserializeMainPack(data);
            UnityMainThreadDispatcher.Instance.Enqueue(() => HandlePacket(pack));
        }
        catch (Exception ex)
        {
            Debug.LogError($"[LobbyClient] Deserialize error: {ex.Message}");
        }
    }

    private void OnChannelDisconnected()
    {
        if (_running)
        {
            UnityMainThreadDispatcher.Instance.Enqueue(() => Disconnect());
        }
    }

    public void Send(MainPack pack)
    {
        if (!IsConnected) return;

        try
        {
            byte[] body = ProtobufSerializer.SerializeMainPack(pack);
            _channel.Send(body);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[LobbyClient] Send error: {ex.Message}");
            Disconnect();
        }
    }

    private void HandlePacket(MainPack pack)
    {
        switch (pack.ActionCode)
        {
            case ActionCode.LoginResult:
                HandleLoginResult(pack);
                break;

            case ActionCode.JoinQueue:
                HandleJoinQueueResult(pack);
                break;

            case ActionCode.LeaveQueue:
                HandleLeaveQueueResult(pack);
                break;

            case ActionCode.MatchFound:
                HandleMatchFound(pack);
                break;

            case ActionCode.StartEnterBattle:
                HandleStartEnterBattle(pack);
                break;

            case ActionCode.OnlinePlayers:
                HandleOnlinePlayers(pack);
                break;

            case ActionCode.RoomList:
                HandleRoomList(pack);
                break;

            case ActionCode.CreateRoom:
                HandleCreateRoomResult(pack);
                break;

            case ActionCode.JoinRoom:
                HandleJoinRoomResult(pack);
                break;

            case ActionCode.LeaveRoom:
                HandleLeaveRoomResult(pack);
                break;

            case ActionCode.RpcCall:
                if (pack.RpcPayload != null && pack.RpcPayload.Length > 0)
                    ShootingGame.Network.Server.ClientRpcReceiver.ProcessIncomingRpc(pack.RpcPayload);
                break;

            case ActionCode.HeroSelected:
                OnHeroSelected?.Invoke(pack.IntVal);
                break;

            case ActionCode.HeroConfirmed:
                OnHeroConfirmed?.Invoke();
                break;
        }
    }

    #region Public API

    public void Login()
    {
        if (!IsConnected)
        {
            Connect();
        }

        var pack = new MainPack
        {
            RequestCode = RequestCode.User,
            ActionCode = ActionCode.Login,
            UserInfo = new UserInfo
            {
                UserId = userId,
                Username = username
            }
        };
        Send(pack);
    }

    public void JoinQueue(int teamId = 0, int heroId = 0)
    {
        if (!IsConnected)
        {
            Debug.LogError("[LobbyClient] Cannot join queue: not connected to server");
            LastJoinQueueError = "未连接到服务器";
            OnJoinQueueResult?.Invoke(false, LastJoinQueueError);
            return;
        }

        if (!IsLoggedIn)
        {
            Debug.LogError("[LobbyClient] Cannot join queue: not logged in");
            LastJoinQueueError = "未登录";
            OnJoinQueueResult?.Invoke(false, LastJoinQueueError);
            return;
        }

        if (heroId <= 0) heroId = SelectedHeroId;
        IsMatchFound = false;
        MatchedBattleInfo = null;

        var pack = new MainPack
        {
            RequestCode = RequestCode.Matching,
            ActionCode = ActionCode.JoinQueue,
            IntVal = heroId
        };
        Send(pack);
        Debug.Log($"[LobbyClient] JoinQueue sent: heroId={heroId}");
    }

    /// <summary>
    /// 发送选角（匹配成功后）。
    /// </summary>
    public void SendHeroSelected(int heroId)
    {
        if (!IsConnected) return;
        var pack = new MainPack
        {
            RequestCode = RequestCode.Battle,
            ActionCode = ActionCode.HeroSelected,
            IntVal = heroId
        };
        Send(pack);
    }

    /// <summary>
    /// 发送选角锁定。
    /// </summary>
    public void SendHeroConfirmed(int heroId)
    {
        if (!IsConnected) return;
        HeroConfirmed = true;
        SelectedHeroId = heroId;
        var pack = new MainPack
        {
            RequestCode = RequestCode.Battle,
            ActionCode = ActionCode.HeroConfirmed,
            IntVal = heroId
        };
        Send(pack);
    }

    public void LeaveQueue()
    {
        var pack = new MainPack
        {
            RequestCode = RequestCode.Matching,
            ActionCode = ActionCode.LeaveQueue
        };
        Send(pack);
    }

    public void RequestRoomList()
    {
        var pack = new MainPack
        {
            RequestCode = RequestCode.Matching,
            ActionCode = ActionCode.RoomList
        };
        Send(pack);
    }

    public void CreateRoom(string roomName, int maxPlayers = 2)
    {
        var pack = new MainPack
        {
            RequestCode = RequestCode.Matching,
            ActionCode = ActionCode.CreateRoom,
            RoomInfo = new RoomInfo
            {
                RoomName = roomName,
                MaxPlayers = maxPlayers
            }
        };
        Send(pack);
    }

    public void JoinRoom(int roomId)
    {
        var pack = new MainPack
        {
            RequestCode = RequestCode.Matching,
            ActionCode = ActionCode.JoinRoom,
            IntVal = roomId
        };
        Send(pack);
    }

    public void LeaveRoom()
    {
        var pack = new MainPack
        {
            RequestCode = RequestCode.Matching,
            ActionCode = ActionCode.LeaveRoom
        };
        Send(pack);
    }

    #endregion

    #region Response Handlers

    private void HandleLoginResult(MainPack pack)
    {
        bool success = pack.ReturnCode == ReturnCode.Success;
        IsLoggedIn = success;
        if (success)
        {
            // 使用服务端分配的唯一 userId（IntVal），确保每个客户端身份唯一
            if (pack.IntVal > 0)
            {
                userId = pack.IntVal;
            }
            Debug.Log($"[LobbyClient] Login result: {success} - {pack.Str} (assigned userId={userId})");
        }
        else
        {
            Debug.Log($"[LobbyClient] Login result: {success} - {pack.Str}");
        }
        OnLoginResult?.Invoke(success, pack.Str);
    }

    private void HandleJoinQueueResult(MainPack pack)
    {
        bool success = pack.ReturnCode == ReturnCode.Success;
        IsInQueue = success;
        string error = success ? "" : (pack.Str ?? "未知错误");
        if (!success) LastJoinQueueError = error;
        OnJoinQueueResult?.Invoke(success, error);
        Debug.Log($"[LobbyClient] Join queue result: {success} - {error}");
    }

    private void HandleLeaveQueueResult(MainPack pack)
    {
        IsInQueue = false;
        Debug.Log("[LobbyClient] Left queue");
    }

    private void HandleMatchFound(MainPack pack)
    {
        IsInQueue = false;
        IsMatchFound = true;
        MatchedBattleInfo = pack.BattleInfo;

        if (pack.BattleInfo == null)
        {
            Debug.LogError("[LobbyClient] MatchFound received but BattleInfo is null! Cannot transition to fight scene.");
            return;
        }

        Debug.Log($"[LobbyClient] Match found! BattleId={pack.BattleInfo.BattleId}, Players={pack.BattleInfo.BattlePlayers?.Count ?? 0}, SpawnPoints={pack.BattleInfo.SpawnPoints?.Count ?? 0}");
        Debug.Log($"[LobbyClient] Invoking OnMatchFound event (subscribers: {OnMatchFound?.GetInvocationList()?.Length ?? 0})");
        OnMatchFound?.Invoke(pack.BattleInfo);
    }

    private void HandleStartEnterBattle(MainPack pack)
    {
        Debug.Log($"[LobbyClient] Start enter battle: {pack.BattleInfo?.BattleId}");
        // 服务端全员确认后统一广播 → 直接触发场景加载，不依赖选角时序
        OnStartEnterBattle?.Invoke(pack.BattleInfo?.BattleId ?? 0);
    }

    private void HandleOnlinePlayers(MainPack pack)
    {
        if (int.TryParse(pack.Str, out int count))
        {
            Debug.Log($"[LobbyClient] Online players: {count}");
            OnOnlinePlayerCountChanged?.Invoke(count);
        }
    }

    private void HandleRoomList(MainPack pack)
    {
        RoomList.Clear();
        if (pack.RoomInfos != null)
        {
            RoomList.AddRange(pack.RoomInfos);
        }
        Debug.Log($"[LobbyClient] Room list received: {RoomList.Count} rooms");
        OnRoomListReceived?.Invoke(RoomList);
    }

    private void HandleCreateRoomResult(MainPack pack)
    {
        bool success = pack.ReturnCode == ReturnCode.Success;
        if (success)
        {
            IsInRoom = true;
            CurrentRoomId = pack.IntVal;
        }
        Debug.Log($"[LobbyClient] Create room result: {success} - {pack.Str}");
        OnRoomCreated?.Invoke(success, pack.Str, pack.RoomInfo);
    }

    private void HandleJoinRoomResult(MainPack pack)
    {
        bool success = pack.ReturnCode == ReturnCode.Success;
        if (success)
        {
            IsInRoom = true;
            CurrentRoomId = pack.IntVal;
        }
        Debug.Log($"[LobbyClient] Join room result: {success} - {pack.Str}");
        OnRoomJoined?.Invoke(success, pack.Str);
    }

    private void HandleLeaveRoomResult(MainPack pack)
    {
        bool success = pack.ReturnCode == ReturnCode.Success;
        if (success)
        {
            IsInRoom = false;
            CurrentRoomId = 0;
        }
        Debug.Log($"[LobbyClient] Leave room result: {success}");
        OnRoomLeft?.Invoke(success);
    }

    #endregion
}

/// <summary>
/// Helper for dispatching actions to Unity main thread.
/// </summary>
public class UnityMainThreadDispatcher : MonoBehaviour
{
    private static UnityMainThreadDispatcher _instance;
    private readonly System.Collections.Queue _executionQueue = new System.Collections.Queue();
    private readonly object _lock = new object();

    public static UnityMainThreadDispatcher Instance
    {
        get
        {
            if (_instance == null)
            {
                var go = new GameObject("UnityMainThreadDispatcher");
                _instance = go.AddComponent<UnityMainThreadDispatcher>();
                DontDestroyOnLoad(go);
            }
            return _instance;
        }
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
    }

    private void Update()
    {
        lock (_lock)
        {
            while (_executionQueue.Count > 0)
            {
                var action = _executionQueue.Dequeue() as Action;
                action?.Invoke();
            }
        }
    }

    public void Enqueue(Action action)
    {
        lock (_lock)
        {
            _executionQueue.Enqueue(action);
        }
    }
}