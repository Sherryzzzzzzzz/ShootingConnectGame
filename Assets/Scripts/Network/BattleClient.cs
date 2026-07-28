using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;
using ShootingGame.Shared.Ability;
using ShootingGame.Shared.Protocol;
using ShootingGame.Shared.Simulation;
using SharedVec3 = ShootingGame.Shared.Math.Vec3;

/// <summary>
/// UDP Battle Client for Unity.
/// Handles real-time battle communication with the server.
/// </summary>
public class BattleClient : MonoBehaviour
{
    [Header("Connection")]
    public string serverIP = "127.0.0.1";
    public int serverPort = 7777;

    // State
    public bool IsConnected { get; private set; }
    public bool IsInBattle { get; private set; }
    public int BattleId { get; private set; }
    public int BattlePlayerId { get; private set; }
    public int TeamId { get; private set; }
    public int ServerFrameId { get; private set; }
    public float SmoothedRtt { get; private set; } = 0.05f;

    // Network
    private UdpClient _udp;
    private IPEndPoint _serverEndpoint;
    private Thread _receiveThread;
    private volatile bool _running;
    private float _lastSendTime;
    private float _lastPingTime;

    // Input sending
    private int _clientFrameId;
    private int _serverAckedFrame;
    private int _attackIdCounter;

    // Frame history for prediction
    private readonly ConcurrentDictionary<int, AllPlayerOperation> _receivedFrames = new ConcurrentDictionary<int, AllPlayerOperation>();
    private int _lastReceivedFrame;

    // Pending attacks for retransmission
    private readonly ConcurrentDictionary<int, AttackOperation> _pendingAttacks = new ConcurrentDictionary<int, AttackOperation>();
    private const int MaxAttackAge = 10; // frames

    // Processed hit events (for deduplication)
    private readonly ConcurrentDictionary<int, long> _processedHitEvents = new ConcurrentDictionary<int, long>();

    // Events
    public event Action OnBattleStart;
    public event Action<AllPlayerOperation> OnFrameReceived;
    public event Action<HitEventMsg> OnHitEvent;
    public event Action<AbilityEventData> OnAbilityEvent;
    public event Action<int> OnGameOver; // winnerTeamId
    public event Action OnDisconnected;

    /// <summary>收到新框架 DeltaState（I帧/P帧的原始字节）</summary>
    public event Action<byte[]> OnDeltaStateReceived;

    // Singleton
    public static BattleClient Instance { get; private set; }

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

    private void Update()
    {
        if (!IsConnected) return;

        // Send Ping periodically
        if (Time.unscaledTime - _lastPingTime > 0.2f) // 200ms interval
        {
            SendPing();
            _lastPingTime = Time.unscaledTime;
        }

        // Resend pending attacks
        ResendPendingAttacks();
    }

    public void Connect()
    {
        if (IsConnected) return;

        try
        {
            // 绑定到 IPv4 任意端口（port=0 让 OS 分配），立即创建底层 Socket
            _udp = new UdpClient(0, AddressFamily.InterNetwork);
            _udp.Client.SendBufferSize = 65536;
            _udp.Client.ReceiveBufferSize = 65536;
            _udp.Client.EnableBroadcast = false;

            _serverEndpoint = new IPEndPoint(IPAddress.Parse(serverIP), serverPort);
            _running = true;
            IsConnected = true;

            _receiveThread = new Thread(ReceiveLoop)
            {
                IsBackground = true,
                Name = "BattleClient_Receive"
            };
            _receiveThread.Start();

            Debug.Log($"[BattleClient] Connected to {serverIP}:{serverPort} (local port: {((IPEndPoint)_udp.Client.LocalEndPoint).Port})");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[BattleClient] Connection failed: {ex.Message}");
            IsConnected = false;
        }
    }

    public void Disconnect()
    {
        if (!IsConnected) return;

        _running = false;
        IsConnected = false;
        IsInBattle = false;

        try
        {
            // Send disconnect
            var pack = new MainPack
            {
                RequestCode = RequestCode.Battle,
                ActionCode = ActionCode.Disconnect
            };
            Send(pack);
        }
        catch { }

        try
        {
            _udp?.Close();
        }
        catch { }

        Debug.Log("[BattleClient] Disconnected");
        OnDisconnected?.Invoke();
    }

    private void ReceiveLoop()
    {
        while (_running)
        {
            try
            {
                IPEndPoint remote = null;
                byte[] data = _udp.Receive(ref remote);

                if (data.Length < 1) continue;

                // 反序列化也必须在主线程（ProtopufSerializer 可能创建 Unity 对象）
                UnityMainThreadDispatcher.Instance.Enqueue(() =>
                {
                    try
                    {
                        var pack = ProtobufSerializer.DeserializeMainPack(data);
                        HandlePacket(pack);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[BattleClient] HandlePacket error: {ex.Message}");
                    }
                });
            }
            catch (Exception ex) when (_running)
            {
                Debug.LogError($"[BattleClient] Receive error: {ex.Message}");
            }
        }
    }

    public void Send(MainPack pack)
    {
        if (!IsConnected || _udp == null) return;

        try
        {
            byte[] body = ProtobufSerializer.SerializeMainPack(pack);
            _udp.Send(body, body.Length, _serverEndpoint);
            _lastSendTime = Time.unscaledTime;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[BattleClient] Send error: {ex.Message}");
        }
    }

    private void HandlePacket(MainPack pack)
    {
        switch (pack.ActionCode)
        {
            case ActionCode.BattleStart:
                HandleBattleStart(pack);
                break;

            case ActionCode.BattleFrame:
                HandleBattleFrame(pack);
                break;

            case ActionCode.Pong:
                HandlePong(pack);
                break;

            case ActionCode.GameOver:
                HandleGameOver(pack);
                break;

            case ActionCode.DeltaState:
                // 新框架 I/P帧
                if (pack.RpcPayload != null)
                    OnDeltaStateReceived?.Invoke(pack.RpcPayload);
                break;
        }
    }

    #region Public API

    /// <summary>
    /// Initialize battle with info from matchmaking.
    /// </summary>
    public void InitializeBattle(BattleInfo battleInfo, int localPlayerId)
    {
        // 清理上一场战斗的残留状态
        _receivedFrames.Clear();
        _processedHitEvents.Clear();
        _pendingAttacks.Clear();
        _clientFrameId = 0;
        _attackIdCounter = 0;
        _lastReceivedFrame = 0;
        _serverAckedFrame = 0;

        BattleId = battleInfo.BattleId;
        BattlePlayerId = localPlayerId;

        // 玩家名字表（记分板/击杀播报用）
        _playerNames.Clear();
        foreach (var player in battleInfo.BattlePlayers)
        {
            if (!string.IsNullOrEmpty(player.PlayerName))
                _playerNames[player.PlayerId] = player.PlayerName;
        }

        // Find our team ID
        foreach (var player in battleInfo.BattlePlayers)
        {
            if (player.PlayerId == localPlayerId)
            {
                TeamId = player.TeamId;
                break;
            }
        }

        Connect();

        // Send BattleReady
        var pack = new MainPack
        {
            RequestCode = RequestCode.Battle,
            ActionCode = ActionCode.BattleReady,
            BattleInfo = new BattleInfo
            {
                BattleId = BattleId,
                OperationId = BattlePlayerId // Reusing field
            }
        };
        Send(pack);

        Debug.Log($"[BattleClient] Sent BattleReady for battle {BattleId}, player {BattlePlayerId}");
    }

    /// <summary>
    /// Send player operation to server.
    /// </summary>
    public void SendOperation(PlayerOperation operation, int clientFrameId)
    {
        // Add pending attacks to retransmission buffer
        if (operation.AttackOperations != null)
        {
            foreach (var atk in operation.AttackOperations)
            {
                _pendingAttacks[atk.AttackId] = atk;
            }
        }

        // 诊断：序列化前打印 PlayerOperation 值
        if (clientFrameId <= 10 || clientFrameId % 30 == 0)
        {
            Debug.Log($"[BC-SEND] tick={clientFrameId} MoveX={operation.MoveX:F6} MoveY={operation.MoveY:F6} AimYaw={operation.AimYaw:F6} Fire={operation.Fire} Jump={operation.Jump} Run={operation.Run} Aim={operation.Aim} Reload={operation.Reload}");
        }

        var pack = new MainPack
        {
            RequestCode = RequestCode.Battle,
            ActionCode = ActionCode.BattleOperation,
            BattleInfo = new BattleInfo
            {
                BattleId = BattleId,
                OperationId = clientFrameId,
                ClientAckedFrame = _lastReceivedFrame,
                SelfOperation = operation
            }
        };
        Send(pack);
    }

    /// <summary>
    /// Create and track a new attack.
    /// </summary>
    public AttackOperation CreateAttack(float towardX, float towardY)
    {
        return new AttackOperation
        {
            AttackId = ++_attackIdCounter,
            TowardX = towardX,
            TowardY = towardY,
            ClientFrameId = _clientFrameId
        };
    }

    #endregion

    #region Response Handlers

    private void HandleBattleStart(MainPack pack)
    {
        IsInBattle = true;
        _clientFrameId = 1;
        ServerFrameId = 1;

        Debug.Log("[BattleClient] Battle started!");
        OnBattleStart?.Invoke();
    }

    private void HandleBattleFrame(MainPack pack)
    {
        if (pack.BattleInfo == null) return;

        ServerFrameId = pack.BattleInfo.OperationId;

        // Process all frames
        foreach (var frame in pack.BattleInfo.AllPlayerOperations)
        {
            if (frame.FrameId > _lastReceivedFrame)
            {
                _receivedFrames[frame.FrameId] = frame;
                _lastReceivedFrame = frame.FrameId;

                // Remove old frames
                while (_receivedFrames.Count > 64)
                {
                    int oldest = frame.FrameId - 64;
                    _receivedFrames.TryRemove(oldest, out _);
                }

                // Notify listeners
                OnFrameReceived?.Invoke(frame);
            }
        }

        // Process hit events
        foreach (var hit in pack.BattleInfo.HitEvents)
        {
            // Deduplicate
            int hitKey = hit.AttackId * 1000 + hit.VictimId;
            if (!_processedHitEvents.ContainsKey(hitKey))
            {
                _processedHitEvents[hitKey] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                OnHitEvent?.Invoke(hit);
                Debug.Log($"[BattleClient] Hit: attacker={hit.AttackerId} victim={hit.VictimId} damage={hit.Damage} isKill={hit.IsKill}");
            }
        }

        // Process ability events from all frames
        foreach (var frame in pack.BattleInfo.AllPlayerOperations)
        {
            if (frame.AbilityEvents != null)
            {
                foreach (var abEvt in frame.AbilityEvents)
                {
                    OnAbilityEvent?.Invoke(abEvt);
                }
            }
        }

        // Confirm attacks
        foreach (var hit in pack.BattleInfo.HitEvents)
        {
            _pendingAttacks.TryRemove(hit.AttackId, out _);
        }

        // Clean old processed hit events
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var oldKeys = new System.Collections.Generic.List<int>();
        foreach (var kvp in _processedHitEvents)
        {
            if (now - kvp.Value > 10000) // 10 seconds
                oldKeys.Add(kvp.Key);
        }
        foreach (var key in oldKeys)
            _processedHitEvents.TryRemove(key, out _);
    }

    private void HandlePong(MainPack pack)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long sent = pack.Timestamp;
        float rtt = (now - sent) / 1000f;

        // EWMA smoothing
        SmoothedRtt = 0.875f * SmoothedRtt + 0.125f * rtt;
    }

    private void HandleGameOver(MainPack pack)
    {
        int winnerId = int.TryParse(pack.Str, out int tid) ? tid : 0;

        // IntVal: 0=团队模式(winnerId=队伍ID) 1=死斗(winnerId=胜者 bpId)
        LastGameOverMode = pack.IntVal;
        LastScoreboard = pack.ScoreEntries ?? new System.Collections.Generic.List<ScoreEntryMsg>();

        Debug.Log($"[BattleClient] Game Over! Mode: {pack.IntVal}, Winner: {winnerId}, Scoreboard: {LastScoreboard.Count} entries");
        OnGameOver?.Invoke(winnerId);
        IsInBattle = false;
    }

    /// <summary>上一场对局模式（0=团队 1=死斗），HandleGameOver 时写入</summary>
    public int LastGameOverMode { get; private set; }
    /// <summary>上一场记分板（服务器权威，已按击杀降序），HandleGameOver 时写入</summary>
    public System.Collections.Generic.List<ScoreEntryMsg> LastScoreboard { get; private set; } = new System.Collections.Generic.List<ScoreEntryMsg>();

    // 玩家名字表（bpId → 名字），InitializeBattle 时从 BattleInfo 构建
    private readonly System.Collections.Generic.Dictionary<int, string> _playerNames = new System.Collections.Generic.Dictionary<int, string>();

    /// <summary>获取玩家显示名（记分板/击杀播报用）</summary>
    public string GetPlayerName(int playerId)
    {
        if (playerId == BattlePlayerId) return "你";
        return _playerNames.TryGetValue(playerId, out var name) ? name : $"玩家{playerId}";
    }

    #endregion

    #region Helper Methods

    private void SendPing()
    {
        var pack = new MainPack
        {
            RequestCode = RequestCode.Battle,
            ActionCode = ActionCode.Ping,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        Send(pack);
    }

    private void ResendPendingAttacks()
    {
        // Remove old pending attacks
        var oldAttacks = new System.Collections.Generic.List<int>();
        foreach (var kvp in _pendingAttacks)
        {
            if (_clientFrameId - kvp.Value.ClientFrameId > MaxAttackAge)
                oldAttacks.Add(kvp.Key);
        }
        foreach (var key in oldAttacks)
            _pendingAttacks.TryRemove(key, out _);
    }

    /// <summary>
    /// Get a received frame by ID.
    /// </summary>
    public bool TryGetFrame(int frameId, out AllPlayerOperation frame)
    {
        return _receivedFrames.TryGetValue(frameId, out frame);
    }

    /// <summary>
    /// Get the latest received frame.
    /// </summary>
    public AllPlayerOperation GetLatestFrame()
    {
        if (_lastReceivedFrame > 0 && _receivedFrames.TryGetValue(_lastReceivedFrame, out var frame))
            return frame;
        return null;
    }

    /// <summary>
    /// Get all frames from startFrame to endFrame.
    /// </summary>
    public System.Collections.Generic.List<AllPlayerOperation> GetFrameRange(int startFrame, int endFrame)
    {
        var frames = new System.Collections.Generic.List<AllPlayerOperation>();
        for (int f = startFrame; f <= endFrame; f++)
        {
            if (_receivedFrames.TryGetValue(f, out var frame))
                frames.Add(frame);
        }
        return frames;
    }

    public int ClientFrameId => _clientFrameId;

    public void IncrementClientFrame()
    {
        _clientFrameId++;
    }

    #endregion
}