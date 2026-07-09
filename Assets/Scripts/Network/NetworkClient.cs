using System;
using System.Net;
using UnityEngine;
using ShootingGame.Shared.Protocol;
using ShootingGame.Shared.Protocol.Kcp;
using ShootingGame.Shared.Simulation;
using SharedVec3 = ShootingGame.Shared.Math.Vec3;


/// <summary>
/// Unity-side network client. Connects to the dedicated server via UDP.
/// Uses KcpSession for transport lifecycle (heartbeat, timeout, RTT).
/// </summary>
public class NetworkClient : MonoBehaviour
{
    [Header("Connection")]
    public string serverIP = "127.0.0.1";
    public int serverPort = 7777;

    // Public state
    public bool IsConnected => _fsm?.CurrentState == ClientConnectionFSM.State.Connected;
    public byte LocalPlayerId => _fsm?.AssignedPlayerId ?? 255;
    public int ServerTick { get; private set; }
    public float Rtt => _session?.SmoothedRtt ?? 0f;
    public ClientConnectionFSM.State ConnectionState => _fsm?.CurrentState ?? ClientConnectionFSM.State.Disconnected;

    // Events
    public event Action<WorldStateMessage> OnWorldState;
    public event Action<DamageEventData> OnDamage;
    public event Action OnConnected;
    public event Action OnDisconnected;

    /// <summary>收到 I帧或 P帧（新框架）</summary>
    public event Action<byte[]> OnDeltaState;

    /// <summary>收到 RPC 调用（新框架）</summary>
    public event Action<byte[]> OnRpcCall;

    // Internals
    private UdpTransport _transport;
    private KcpSession _session;
    private ClientConnectionFSM _fsm;
    private uint _conv;
    private IPEndPoint _serverEndPoint;

    public struct WorldStateMessage
    {
        public int ServerTick;
        public PlayerSnapshot[] Players;
        public int[] LastProcessedInputTicks;
    }

    public struct DamageEventData
    {
        public byte TargetId;
        public byte ShooterId;
        public byte Damage;
        public byte NewHealth;
        public SharedVec3 HitPoint;
    }

    private void Awake()
    {
        _transport = new UdpTransport();
        _transport.Start(0);
        _serverEndPoint = new IPEndPoint(IPAddress.Parse(serverIP), serverPort);

        // Generate a random conversation ID for this client
        _conv = (uint)UnityEngine.Random.Range(1, int.MaxValue);
        _session = new KcpSession(_conv, (buf, len) =>
        {
            _transport.Send(buf, len, _serverEndPoint);
        });

        // Wire KcpSession lifecycle events
        _session.OnTimeout += HandleSessionTimeout;
        _session.OnHeartbeatResponse += OnHeartbeatRttSample;

        // Create connection state machine
        _fsm = new ClientConnectionFSM();
        _fsm.SendHandshakeRequest = () =>
        {
            SendReliable(s_connectReqPayload);
            return true;
        };
        _fsm.OnConnected += () =>
        {
            _session.MarkConnected(Time.unscaledTime);
            OnConnected?.Invoke();
        };
        _fsm.OnDisconnected += () =>
        {
            OnDisconnected?.Invoke();
        };
        _fsm.OnReconnecting += () =>
        {
            Debug.Log("[NetworkClient] Connection lost, attempting to reconnect...");
        };
    }

    private static readonly GameMessage s_disconnectMsg = new GameMessage
    {
        MsgType = GameMessageType.Disconnect,
        Disconnect = new DisconnectMsg { Reason = 0 }
    };
    private static readonly GameMessage s_connectReqMsg = new GameMessage
    {
        MsgType = GameMessageType.ConnectionRequest,
        ConnectionRequest = new ConnectionRequestMsg { ProtocolVersion = 1 }
    };
    private static readonly byte[] s_disconnectPayload = ProtobufSerializer.SerializeGameMessage(s_disconnectMsg);
    private static readonly byte[] s_connectReqPayload = ProtobufSerializer.SerializeGameMessage(s_connectReqMsg);

    private void OnDestroy()
    {
        if (IsConnected)
        {
            SendReliable(s_disconnectPayload);
        }
        _transport?.Stop();
    }

    public void Connect()
    {
        Debug.Log($"[NetworkClient] Starting connection to {serverIP}:{serverPort}...");
        _fsm.StartHandshake();
    }

    private void Update()
    {
        float currentTime = Time.unscaledTime;

        // Drive connection state machine (retries, reconnection timeout)
        _fsm.Update(currentTime);

        // Drive KcpSession (KCP update + heartbeat + timeout check)
        DrainReceiveQueue(currentTime);
    }

    /// <summary>
    /// Send input to server (unreliable). Called by PlayerController each tick.
    /// </summary>
    public void SendInput(InputFrame[] frames, int count)
    {
        if (!IsConnected) return;

        var batch = new InputBatchMsg();
        for (int i = 0; i < count; i++)
            batch.Frames.Add(ProtobufSerializer.ToInputFrameMsg(frames[i]));
        var msg = new GameMessage
        {
            MsgType = GameMessageType.InputMessage,
            InputBatch = batch
        };
        SendUnreliable(ProtobufSerializer.SerializeGameMessage(msg));
    }

    private void DrainReceiveQueue(float currentTime)
    {
        while (_transport.TryReceive(out var packet))
        {
            if (_session.Input(packet.Data, packet.Length, out byte[] unreliablePayload))
            {
                // Unreliable message — deserialize and dispatch
                var gameMsg = ProtobufSerializer.DeserializeGameMessage(unreliablePayload);
                DispatchUnreliableMessage(gameMsg);
            }
            // KCP data is auto-fed by KcpSession
        }

        // Drive KcpSession (KCP update + message drain + heartbeat)
        _session.Update(currentTime);

        // Drain reliable messages
        while (true)
        {
            byte[] reliableMsg = _session.TryRecv();
            if (reliableMsg == null) break;
            var gameMsg = ProtobufSerializer.DeserializeGameMessage(reliableMsg);
            DispatchReliableMessage(gameMsg);
        }
    }

    private void DispatchUnreliableMessage(GameMessage gameMsg)
    {
        switch (gameMsg.MsgType)
        {
            case GameMessageType.WorldStateMessage:
                HandleWorldState(gameMsg.WorldState);
                break;
            case GameMessageType.DeltaState:
                if (gameMsg.BinaryPayload != null)
                    OnDeltaState?.Invoke(gameMsg.BinaryPayload);
                break;
            case GameMessageType.RpcCall:
                if (gameMsg.BinaryPayload != null)
                    OnRpcCall?.Invoke(gameMsg.BinaryPayload);
                break;
            case GameMessageType.Heartbeat:
                // Pong response: RTT handled by KcpSession via OnPongReceived
                if (gameMsg.Heartbeat != null)
                {
                    float rttSample = (Time.unscaledTime * 1000f - gameMsg.Heartbeat.Timestamp) / 1000f;
                    if (rttSample > 0f && rttSample < 10f)
                        _session.OnPongReceived(rttSample);
                }
                break;
        }
    }

    private void DispatchReliableMessage(GameMessage gameMsg)
    {
        switch (gameMsg.MsgType)
        {
            case GameMessageType.ConnectionAccepted:
                HandleConnectionAccepted(gameMsg.ConnectionAccepted);
                break;

            case GameMessageType.DamageEvent:
                HandleDamageEvent(gameMsg.DamageEvent);
                break;

            case GameMessageType.PlayerJoined:
                Debug.Log($"Player {gameMsg.PlayerJoined.PlayerId} joined");
                break;

            case GameMessageType.PlayerLeft:
                Debug.Log($"Player {gameMsg.PlayerLeft.PlayerId} left");
                OnDisconnected?.Invoke();
                break;

            case GameMessageType.RpcCall:
                if (gameMsg.BinaryPayload != null)
                    OnRpcCall?.Invoke(gameMsg.BinaryPayload);
                break;
        }
    }

    private void HandleConnectionAccepted(ConnectionAcceptedMsg ca)
    {
        ServerTick = ca.ServerTick;
        // Generate session token from playerId + conv for reconnection support
        string token = $"tok_{ca.PlayerId}_{_conv}";

        _fsm.OnHandshakeAccepted(ca.PlayerId, token);

        Debug.Log($"[NetworkClient] Connected as Player {ca.PlayerId}, server tick {ca.ServerTick}");
    }

    private void HandleWorldState(WorldStateMsg ws)
    {
        ServerTick = ws.ServerTick;

        OnWorldState?.Invoke(new WorldStateMessage
        {
            ServerTick = ws.ServerTick,
            Players = ws.Players.ConvertAll(s => ProtobufSerializer.ToPlayerSnapshot(s)).ToArray(),
            LastProcessedInputTicks = ws.LastProcessedInputTicks
        });
    }

    private void HandleDamageEvent(DamageEventMsg de)
    {
        OnDamage?.Invoke(new DamageEventData
        {
            TargetId = de.TargetId,
            ShooterId = de.ShooterId,
            Damage = de.Damage,
            NewHealth = de.NewHealth,
            HitPoint = de.HitPoint
        });
    }

    /// <summary>
    /// Called by KcpSession when 10s timeout is detected.
    /// Triggers reconnection attempt.
    /// </summary>
    private void HandleSessionTimeout()
    {
        Debug.LogWarning($"[NetworkClient] Connection timeout (> {_session.TimeoutDurationSec}s). Entering reconnection...");
        _fsm.OnConnectionLost();
    }

    /// <summary>
    /// Log RTT samples for debugging (KcpSession handles smoothing internally).
    /// </summary>
    private void OnHeartbeatRttSample(float rttSec)
    {
        // RTT smoothed automatically by KcpSession
        // Add debug overlay update here if needed
    }

    private void SendReliable(byte[] payload)
    {
        _session.SendReliable(payload);
    }

    private void SendUnreliable(byte[] payload)
    {
        byte[] packet = _session.Channel.WrapUnreliable(payload);
        _transport.Send(packet, packet.Length, _serverEndPoint);
        _session.MarkUnreliableSent(packet);
    }

    /// <summary>
    /// 发送原始二进制消息（新框架使用）。
    /// DeltaState 和 RpcCall 消息发到这里。
    /// </summary>
    public void SendRawMessage(GameMessageType msgType, byte[] binaryPayload, bool reliable = false)
    {
        var gameMsg = new GameMessage
        {
            MsgType = msgType,
            BinaryPayload = binaryPayload
        };
        byte[] bytes = ProtobufSerializer.SerializeGameMessage(gameMsg);
        if (reliable)
            SendReliable(bytes);
        else
            SendUnreliable(bytes);
    }
}
