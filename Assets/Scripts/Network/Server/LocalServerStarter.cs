using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using ShootingGame.Shared.Protocol;
using UnityEngine;

namespace ShootingGame.Network.Server
{
    /// <summary>
    /// 本地服务端一键启动器。
    /// 在 Unity Editor 内启动 TCP 大厅 + UDP 战斗服务端，实现 "Host & Play"。
    /// </summary>
    public class LocalServerStarter : MonoBehaviour
    {
        [Header("端口配置")]
        [SerializeField] private int _battlePort = 7777;
        [SerializeField] private int _lobbyPort = 7778;

        /// <summary>UDP 战斗端口</summary>
        public int BattlePort => _battlePort;
        /// <summary>TCP 大厅端口</summary>
        public int LobbyPort => _lobbyPort;
        /// <summary>是否正在运行</summary>
        public bool IsRunning { get; private set; }

        /// <summary>服务端是否已启动</summary>
        public bool ServerReady { get; private set; }

        // -- 内部组件 --
        private ServerTransport _battleTransport;
        private HostBattleServer _battleServer;
        private TcpListener _lobbyListener;
        private Thread _lobbyAcceptThread;
        private CancellationTokenSource _cts;

        /// <summary>单例</summary>
        public static LocalServerStarter Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning($"[LocalServerStarter] 重复实例被销毁: existing={Instance.name} new={name}");
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Debug.Log($"[LocalServerStarter] Awake: GameObject={name}, DontDestroyOnLoad 已设置");
        }

        private void OnDestroy()
        {
            Debug.Log($"[LocalServerStarter] OnDestroy: 服务端正在停止... stackTrace={StackTraceUtility.ExtractStackTrace()}");
            StopServer();
        }

        /// <summary>
        /// 启动本地服务端（TCP 大厅 + UDP 战斗）。
        /// </summary>
        [ContextMenu("Start Local Server")]
        public void StartServer()
        {
            if (IsRunning)
            {
                Debug.LogWarning("[LocalServerStarter] 服务端已在运行，跳过重复启动");
                return;
            }

            _cts = new CancellationTokenSource();
            IsRunning = true;
            ServerReady = false;

            // 接通新框架 RPC 传输：服务端 → 客户端广播（通过 TCP）
            NetworkBehaviour.SendClientRpcTransport = (netId, rpcPayload, target) =>
            {
                var rpcPack = new MainPack
                {
                    RequestCode = RequestCode.Battle,
                    ActionCode = ActionCode.RpcCall,
                    RpcPayload = rpcPayload
                };
                byte[] body = ProtobufSerializer.SerializeMainPack(rpcPack);
                foreach (var kvp in _lobbyClients)
                {
                    try { SendLobbyResponse(kvp.Value.Stream, rpcPack); }
                    catch { /* 客户端可能已断开 */ }
                }
            };

            // 1. 先启动 UDP 战斗服（大厅 BroadcastMatchFound 需要 _battleServer 引用）
            StartBattleServer();

            // 2. 再启动 TCP 大厅
            StartMinimalLobby();

            Debug.Log($"[LocalServerStarter] 本地服务端已启动: lobbyPort={_lobbyPort}, battlePort={_battlePort}");
        }

        /// <summary>
        /// 停止本地服务端。
        /// </summary>
        [ContextMenu("Stop Server")]
        public void StopServer()
        {
            if (!IsRunning) return;

            IsRunning = false;
            ServerReady = false;

            _cts?.Cancel();

            try { _lobbyListener?.Stop(); } catch { }
            _battleServer?.StopServer();
            _battleTransport?.Dispose();
            if (_battleServer != null) { Destroy(_battleServer); _battleServer = null; }

            Debug.Log("[LocalServerStarter] 本地服务端已停止");
        }

        // ==================== TCP 大厅 ====================

        private void StartMinimalLobby()
        {
            try
            {
                _lobbyListener = new TcpListener(IPAddress.Loopback, _lobbyPort);
                _lobbyListener.Start();
            }
            catch (System.Net.Sockets.SocketException ex)
            {
                Debug.LogError($"[LocalServerStarter] 端口 {_lobbyPort} 已被占用！请先关闭已有服务端或更换端口。 ({ex.Message})");
                IsRunning = false;
                return;
            }

            _lobbyAcceptThread = new Thread(AcceptLobbyClients)
            {
                IsBackground = true,
                Name = "MiniLobby_Accept"
            };
            _lobbyAcceptThread.Start();
        }

        private void AcceptLobbyClients()
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    var tcpClient = _lobbyListener.AcceptTcpClient();
                    var handlerThread = new Thread(() => HandleLobbyClient(tcpClient))
                    {
                        IsBackground = true,
                        Name = "MiniLobby_Handler"
                    };
                    handlerThread.Start();
                }
                catch (Exception) when (_cts.Token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[LocalServerStarter] Lobby accept error: {ex.Message}");
                }
            }
        }

        private void HandleLobbyClient(TcpClient tcpClient)
        {
            NetworkStream stream = null;
            int clientId = -1;
            try
            {
                stream = tcpClient.GetStream();
                var lengthBuf = new byte[4];
                var receiveBuf = new byte[65536];

                Debug.Log("[LocalServerStarter] Lobby TCP client connected, waiting for requests...");
                while (!_cts.Token.IsCancellationRequested)
                {
                    if (!ReadExact(stream, lengthBuf, 4)) { Debug.Log("[LocalServerStarter] Lobby TCP: client disconnected (EOF on length)"); break; }
                    int frameLen = (lengthBuf[0] << 24) | (lengthBuf[1] << 16) | (lengthBuf[2] << 8) | lengthBuf[3];
                    if (frameLen <= 0 || frameLen > receiveBuf.Length) { Debug.LogWarning($"[LocalServerStarter] Lobby TCP: invalid frameLen={frameLen}"); break; }

                    if (!ReadExact(stream, receiveBuf, frameLen)) { Debug.Log("[LocalServerStarter] Lobby TCP: client disconnected (EOF on body)"); break; }

                    var requestData = new byte[frameLen];
                    Buffer.BlockCopy(receiveBuf, 0, requestData, 0, frameLen);
                    var request = ProtobufSerializer.DeserializeMainPack(requestData);
                    Debug.Log($"[LocalServerStarter] Lobby TCP: received {frameLen} bytes, ActionCode={request.ActionCode}");

                    var response = ProcessLobbyRequest(request, ref clientId, stream);
                    if (response == null) continue; // BroadcastMatchFound 已群发

                    SendLobbyResponse(stream, response);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LocalServerStarter] Lobby client handler CRASH: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                if (clientId >= 0)
                {
                    _matchQueue.Remove(clientId);
                    _lobbyClients.Remove(clientId);
                    Debug.Log($"[LocalServerStarter] Lobby: client {clientId} disconnected. Queue={_matchQueue.Count}");
                }
                try { tcpClient.Close(); } catch { }
            }
        }

        private void SendLobbyResponse(NetworkStream stream, MainPack response)
        {
            byte[] body = ProtobufSerializer.SerializeMainPack(response);
            var frameBytes = new byte[4 + body.Length];
            frameBytes[0] = (byte)(body.Length >> 24);
            frameBytes[1] = (byte)(body.Length >> 16);
            frameBytes[2] = (byte)(body.Length >> 8);
            frameBytes[3] = (byte)(body.Length);
            Buffer.BlockCopy(body, 0, frameBytes, 4, body.Length);
            stream.Write(frameBytes, 0, frameBytes.Length);
        }

        /// <summary>
        /// 广播 MatchFound 给所有排队玩家。
        /// </summary>
        private void BroadcastMatchFound()
        {
            var battlePlayers = new System.Collections.Generic.List<BattlePlayerInfo>();
            var spawnPoints = new System.Collections.Generic.List<SpawnPointMsg>();
            int pid = 0;

            foreach (var clientId in _matchQueue)
            {
                if (!_lobbyClients.TryGetValue(clientId, out var st)) continue;
                pid++;
                float spawnX = (pid - 1) * 5f - 2.5f; // 各玩家分散生成
                battlePlayers.Add(new BattlePlayerInfo
                {
                    PlayerId = pid, TeamId = pid % 2 == 1 ? 1 : 2, UserId = st.ClientId,
                    PlayerName = st.Username,
                    SpawnPosition = new ShootingGame.Shared.Math.Vec3(spawnX, 0.1f, 0f),
                    HeroId = 1
                });
                spawnPoints.Add(new SpawnPointMsg
                {
                    Position = new ShootingGame.Shared.Math.Vec3(spawnX, 0.1f, 0f),
                    Yaw = 0f, TeamId = 1
                });
            }

            var battleInfo = new BattleInfo
            {
                BattleId = _rng.Next(1, int.MaxValue),
                RandSeed = _rng.Next(1, int.MaxValue),
                BattlePlayers = battlePlayers,
                SpawnPoints = spawnPoints
            };

            foreach (var clientId in _matchQueue)
            {
                if (!_lobbyClients.TryGetValue(clientId, out var st)) continue;
                if (st.Stream == null) continue;

                var matchMsg = new MainPack
                {
                    RequestCode = RequestCode.Matching,
                    ActionCode = ActionCode.MatchFound,
                    ReturnCode = ReturnCode.Success,
                    BattleInfo = battleInfo
                };
                try { SendLobbyResponse(st.Stream, matchMsg); }
                catch (System.Exception ex) { Debug.LogError($"[LocalServerStarter] BroadcastMatchFound to client {clientId} failed: {ex.Message}"); }
            }

            Debug.Log($"[LocalServerStarter] MatchFound broadcast to {_matchQueue.Count} players, BattleId={battleInfo.BattleId}");

            // 记录已匹配的客户端（用于选角阶段转发消息）
            _matchedClients.Clear();
            _matchedClients.AddRange(_matchQueue);

            // 设置战斗服的预期玩家人数
            if (_battleServer != null)
                _battleServer.SetExpectedPlayerCount(_matchQueue.Count);
        }

        private int _nextPlayerId = 1;
        private readonly System.Random _rng = new System.Random();
        private readonly System.Collections.Generic.Dictionary<int, LobbyClientState> _lobbyClients = new System.Collections.Generic.Dictionary<int, LobbyClientState>();
        private readonly System.Collections.Generic.List<int> _matchQueue = new System.Collections.Generic.List<int>();
        private readonly System.Collections.Generic.List<int> _matchedClients = new System.Collections.Generic.List<int>();
        private const int MinPlayersToStart = 2; // 至少 2 人开始匹配

        private class LobbyClientState
        {
            public int ClientId;
            public int UserId;
            public string Username;
            public NetworkStream Stream;
            public bool InQueue;
        }

        /// <summary>
        /// 处理大厅请求。支持多客户端匹配。
        /// </summary>
        private MainPack ProcessLobbyRequest(MainPack request, ref int clientId, NetworkStream stream)
        {
            switch (request.ActionCode)
            {
                case ActionCode.Login:
                {
                    int userId = request.UserInfo?.UserId ?? 1;
                    string username = request.UserInfo?.Username ?? "Player";
                    clientId = _nextPlayerId++;
                    _lobbyClients[clientId] = new LobbyClientState
                    {
                        ClientId = clientId, UserId = userId, Username = username,
                        Stream = stream, InQueue = false
                    };

                    // 为新客户端创建 NetworkBehaviour + 分配 NetId（用于 RPC 路由）
                    var behaviour = new PlayerCombatBehaviour();
                    var entity = new ShootingGame.Shared.ECS.Entity(clientId, 1);
                    behaviour.Bind(entity, null, NetObjectType.Player);
                    Debug.Log($"[LocalServerStarter] RPC: bound behaviour NetId={behaviour.NetId} for clientId={clientId}");

                    Debug.Log($"[LocalServerStarter] Lobby: Login clientId={clientId} user={username} (total: {_lobbyClients.Count})");
                    return new MainPack
                    {
                        RequestCode = RequestCode.User, ActionCode = ActionCode.LoginResult,
                        ReturnCode = ReturnCode.Success,
                        Str = $"Welcome! ({_lobbyClients.Count} online)", IntVal = clientId
                    };
                }

                case ActionCode.JoinQueue:
                {
                    if (clientId < 0 || !_lobbyClients.ContainsKey(clientId))
                        return new MainPack { RequestCode = RequestCode.Matching, ActionCode = ActionCode.JoinQueue, ReturnCode = ReturnCode.Fail, Str = "Not logged in" };

                    if (!_matchQueue.Contains(clientId)) _matchQueue.Add(clientId);
                    _lobbyClients[clientId].InQueue = true;

                    int qCount = _matchQueue.Count;
                    Debug.Log($"[LocalServerStarter] Lobby: JoinQueue clientId={clientId}, queue={qCount}/{MinPlayersToStart}");

                    // 人够了——广播 MatchFound 给所有排队玩家
                    if (qCount >= MinPlayersToStart)
                    {
                        Debug.Log($"[LocalServerStarter] Match ready! Broadcasting MatchFound to {qCount} players...");
                        BroadcastMatchFound();
                        _matchQueue.Clear();
                        return null; // 已广播
                    }

                    return new MainPack
                    {
                        RequestCode = RequestCode.Matching, ActionCode = ActionCode.JoinQueue,
                        ReturnCode = ReturnCode.Success,
                        Str = $"Matching... ({qCount}/{MinPlayersToStart})"
                    };
                }

                case ActionCode.CreateRoom:
                case ActionCode.JoinRoom:
                    Debug.Log("[LocalServerStarter] Lobby: Room → auto accept");
                    return new MainPack
                    {
                        RequestCode = RequestCode.Matching,
                        ActionCode = request.ActionCode,
                        ReturnCode = ReturnCode.Success,
                        IntVal = 1,
                        Str = "OK"
                    };

                case ActionCode.RoomList:
                    return new MainPack
                    {
                        RequestCode = RequestCode.Matching,
                        ActionCode = ActionCode.RoomList,
                        ReturnCode = ReturnCode.Success,
                        RoomInfos = new System.Collections.Generic.List<RoomInfo>
                        {
                            new RoomInfo
                            {
                                RoomId = 1,
                                RoomName = "Local Room",
                                CreatorName = "Host",
                                PlayerCount = 1,
                                MaxPlayers = 4,
                                Status = 0
                            }
                        }
                    };

                case ActionCode.BattleReady:
                    ServerReady = true;
                    Debug.Log("[LocalServerStarter] Client is BattleReady — server ready for tick loop");
                    return new MainPack
                    {
                        RequestCode = RequestCode.Battle,
                        ActionCode = ActionCode.BattleStart,
                        ReturnCode = ReturnCode.Success
                    };

                case ActionCode.Ping:
                    return new MainPack
                    {
                        RequestCode = RequestCode.Battle,
                        ActionCode = ActionCode.Pong,
                        Timestamp = request.Timestamp
                    };

                case ActionCode.HeroSelected:
                case ActionCode.HeroConfirmed:
                    // 转发给其他客户端
                    BroadcastToOtherClients(clientId, request);
                    // 不回显 HeroConfirmed——避免发送方误以为对手已确认
                    return new MainPack
                    {
                        RequestCode = RequestCode.Battle,
                        ActionCode = ActionCode.Ping,  // 用 Ping 做 ack，不触发 HeroConfirmed
                        ReturnCode = ReturnCode.Success
                    };

                case ActionCode.RpcCall:
                    // 分发客户端 RPC（新框架）
                    if (request.RpcPayload != null)
                    {
                        try
                        {
                            var rpcReader = new PacketReader(request.RpcPayload);
                            uint netId = rpcReader.ReadUInt32();
                            long methodHash = rpcReader.ReadInt64();
                            rpcReader.ReadUInt32(); // reqId

                            var behaviour = NetIdRegistry.GetBehaviour(netId);
                            if (behaviour != null)
                            {
                                var argsReader = new PacketReader(rpcReader.ReadBytes(rpcReader.Remaining));
                                RpcMethodRegistry.Dispatch(behaviour, methodHash, argsReader);
                            }
                        }
                        catch (System.Exception ex)
                        {
                            Debug.LogError($"[LocalServerStarter] RPC dispatch error: {ex.Message}");
                        }
                    }
                    return null; // RPC 不需要响应

                default:
                    Debug.Log($"[LocalServerStarter] Lobby: unhandled action={request.ActionCode}");
                    return new MainPack
                    {
                        RequestCode = request.RequestCode,
                        ActionCode = request.ActionCode,
                        ReturnCode = ReturnCode.Success,
                        Str = "OK"
                    };
            }
        }

        // ==================== UDP 战斗 ====================

        private void StartBattleServer()
        {
            _battleTransport = new ServerTransport(_battlePort);
            _battleServer = gameObject.AddComponent<HostBattleServer>();
            _battleServer.StartServer(_battleTransport);
            _battleTransport.Start();
            Debug.Log("[LocalServerStarter] Battle UDP server started (HostBattleServer)");
        }

        /// <summary>
        /// 转发消息给匹配队列中的其他客户端（用于选角同步）。
        /// </summary>
        private void BroadcastToOtherClients(int senderClientId, MainPack pack)
        {
            var targets = _matchedClients.Count > 0 ? _matchedClients : _matchQueue;
            foreach (var clientId in targets)
            {
                if (clientId == senderClientId) continue;
                if (!_lobbyClients.TryGetValue(clientId, out var st)) continue;
                try { SendLobbyResponse(st.Stream, pack); }
                catch { /* 忽略断开 */ }
            }
        }

        // ==================== 辅助 ====================

        private static bool ReadExact(NetworkStream stream, byte[] buffer, int count)
        {
            int read = 0;
            while (read < count)
            {
                int n = stream.Read(buffer, read, count - read);
                if (n == 0) return false;
                read += n;
            }
            return true;
        }
    }
}
