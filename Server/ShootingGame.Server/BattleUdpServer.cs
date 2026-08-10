using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using ShootingGame.Shared.Protocol;

namespace ShootingGame.Server
{
    /// <summary>
    /// UDP Battle Server handling real-time battle communication.
    /// Listens on port 7777 for UDP packets.
    /// </summary>
    public class BattleUdpServer
    {
        private readonly int _port;
        private int _recvErrorCount;
        private UdpClient _udp;
        private volatile bool _running;
        private Thread _receiveThread;

        // Battle routing: endpoint -> battlePlayerId
        private readonly ConcurrentDictionary<string, int> _endpointToBattlePlayerId = new ConcurrentDictionary<string, int>();
        // Battle routing: battlePlayerId -> (battleId, endpoint)
        private readonly ConcurrentDictionary<int, (int battleId, string endpoint)> _battlePlayerRouting = new ConcurrentDictionary<int, (int, string)>();

        // Battle rooms
        private readonly ConcurrentDictionary<int, BattleRoom> _battleRooms = new ConcurrentDictionary<int, BattleRoom>();

        // KCP 可靠通道会话（endpoint → 会话，conv = BattleId）
        private readonly ConcurrentDictionary<string, KcpChannel> _kcpChannels = new ConcurrentDictionary<string, KcpChannel>();
        private Thread _kcpUpdateThread;

        // RTT tracking per player (smoothed, in seconds)
        private readonly ConcurrentDictionary<int, float> _playerRtt = new ConcurrentDictionary<int, float>();
        // Track last ping send time per player (in ms)
        private readonly ConcurrentDictionary<int, long> _pendingPings = new ConcurrentDictionary<int, long>();

        // Network simulation
        private NetSimulator _netSim;

        // Events
        public event Action<int, IPEndPoint, MainPack> OnPacketReceived;

        public BattleUdpServer(int port = 7777)
        {
            _port = port;
        }

        public void Start()
        {
            _udp = new UdpClient(_port);
            _running = true;

            _receiveThread = new Thread(ReceiveLoop)
            {
                IsBackground = true,
                Name = "BattleUdpServer_Receive"
            };
            _receiveThread.Start();

            StartKcpUpdateLoop();

            Log($"BattleUdpServer started on port {_port}");
        }

        /// <summary>KCP 定时驱动（ACK/重传/收包收集），20ms 一次</summary>
        private void StartKcpUpdateLoop()
        {
            _kcpUpdateThread = new Thread(() =>
            {
                while (_running)
                {
                    uint now = (uint)Environment.TickCount;
                    foreach (var kcp in _kcpChannels.Values)
                        kcp.Update(now);
                    Thread.Sleep(20);
                }
            })
            {
                IsBackground = true,
                Name = "BattleUdpServer_KcpUpdate"
            };
            _kcpUpdateThread.Start();
        }

        public void Stop()
        {
            _running = false;
            _netSim?.Stop();
            _udp?.Close();
            Log("BattleUdpServer stopped");
        }

        /// <summary>
        /// Configure network simulation for weak network testing.
        /// </summary>
        public void ConfigureNetSim(float dropRate, int delayMinMs, int delayMaxMs)
        {
            if (_netSim == null)
            {
                _netSim = new NetSimulator((data, len, ep) =>
                {
                    try { _udp.Send(data, len, ep); } catch { }
                });
            }
            _netSim.Start(dropRate, delayMinMs, delayMaxMs);
        }

        /// <summary>
        /// Register a battle room.
        /// </summary>
        public void RegisterBattle(BattleRoom room)
        {
            _battleRooms[room.BattleId] = room;

            // Set up callbacks
            room.OnSendPacket += (endpoint, pack) =>
            {
                Send(pack, endpoint);
            };

            room.OnSendBattleStart += (battlePlayerId, endpoint) =>
            {
                var pack = new MainPack
                {
                    RequestCode = RequestCode.Battle,
                    ActionCode = ActionCode.BattleStart,
                    Str = "1"
                };
                Send(pack, endpoint);
            };

            // 服务器 → 客户端 RPC 回程：按 bpId 定向发送（走 KCP 可靠通道）
            room.OnSendClientRpc += (battlePlayerId, payload) =>
            {
                if (_battlePlayerRouting.TryGetValue(battlePlayerId, out var routing))
                {
                    var pack = new MainPack
                    {
                        RequestCode = RequestCode.Battle,
                        ActionCode = ActionCode.RpcCall,
                        RpcPayload = payload
                    };
                    SendReliable(pack, routing.endpoint);
                }
            };

            Log($"Registered battle {room.BattleId}");
        }

        /// <summary>
        /// Unregister a battle room.
        /// </summary>
        public void UnregisterBattle(int battleId)
        {
            _battleRooms.TryRemove(battleId, out _);

            // Clean up player routing for this battle
            var toRemove = new List<int>();
            foreach (var kvp in _battlePlayerRouting)
            {
                if (kvp.Value.battleId == battleId)
                    toRemove.Add(kvp.Key);
            }
            foreach (var bpId in toRemove)
            {
                UnregisterPlayer(bpId);
            }

            Log($"Unregistered battle {battleId}");
        }

        /// <summary>
        /// Register a player's endpoint for routing.
        /// </summary>
        public void RegisterPlayer(int battleId, int battlePlayerId, IPEndPoint endpoint)
        {
            string endpointStr = endpoint.ToString();
            _endpointToBattlePlayerId[endpointStr] = battlePlayerId;
            _battlePlayerRouting[battlePlayerId] = (battleId, endpointStr);
            Log($"Registered player {battlePlayerId} from {endpointStr} in battle {battleId}");
        }

        /// <summary>
        /// Unregister a player.
        /// </summary>
        public void UnregisterPlayer(int battlePlayerId)
        {
            if (_battlePlayerRouting.TryRemove(battlePlayerId, out var routing))
            {
                _endpointToBattlePlayerId.TryRemove(routing.endpoint, out _);
                _kcpChannels.TryRemove(routing.endpoint, out _);
            }
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

                    // ── KCP 可靠通道：endpoint 有会话且 conv 匹配 → 可靠消息 ──
                    string endpointStr = remote.ToString();
                    if (_kcpChannels.TryGetValue(endpointStr, out var kcp) &&
                        data.Length >= KcpChannel.KcpMinHeaderSize &&
                        KcpChannel.ExtractConv(data, 0) == kcp.Conv)
                    {
                        kcp.Input(data, data.Length, out _);
                        kcp.Update((uint)Environment.TickCount);
                        foreach (var reliableMsg in kcp.DrainRecv())
                        {
                            var kcpPack = ProtobufSerializer.DeserializeMainPack(reliableMsg);
                            HandlePacket(kcpPack, remote);
                        }
                        continue;
                    }

                    var pack = ProtobufSerializer.DeserializeMainPack(data);

                    // ── NetSim Uplink interception (参考 HYLD LZJUDP.RecvThread) ──
                    if (_netSim != null && _netSim.Enabled)
                    {
                        var strategy = GetPacketStrategy(pack);
                        var capturedPack = pack;
                        var capturedRemote = remote;
                        if (_netSim.ShouldDelayOrDrop(strategy, isUplink: true,
                            onDelayed: () => HandlePacket(capturedPack, capturedRemote),
                            onDropped: () => { /* silently dropped */ }))
                        {
                            continue; // 被 NetSim 拦截 (延迟或丢弃)
                        }
                    }

                    HandlePacket(pack, remote);
                }
                catch (Exception ex)
                {
                    if (!_running) break; // 正常关闭
                    // 客户端断开导致的 SocketException，静默处理（不刷屏）
                    if (ex is SocketException && !_running) break;
                    // 仅对非预期的错误打印（限制频率）
                    if (++_recvErrorCount < 5)
                        Log($"Receive error: {ex.Message}");
                }
            }
        }

        private void HandlePacket(MainPack pack, IPEndPoint remote)
        {
            string endpointStr = remote.ToString();

            switch (pack.ActionCode)
            {
                case ActionCode.BattleReady:
                    HandleBattleReady(pack, remote);
                    break;

                case ActionCode.BattleOperation:
                    HandleBattleOperation(pack, remote);
                    break;

                case ActionCode.Ping:
                    HandlePing(pack, remote);
                    break;

                case ActionCode.Disconnect:
                    HandleDisconnect(pack, remote);
                    break;

                case ActionCode.RpcCall:
                    HandleRpcCall(pack, remote);
                    break;

                case ActionCode.GameOver:
                    // Client confirms game over
                    break;
            }

            OnPacketReceived?.Invoke(_endpointToBattlePlayerId.GetValueOrDefault(endpointStr, -1), remote, pack);
        }

        private void HandleBattleReady(MainPack pack, IPEndPoint remote)
        {
            if (pack.BattleInfo == null) return;

            int battleId = pack.BattleInfo.BattleId;
            int battlePlayerId = pack.BattleInfo.OperationId; // Reusing field

            // Register routing
            RegisterPlayer(battleId, battlePlayerId, remote);

            // 建立 KCP 可靠通道会话（conv = BattleId，免协商）
            string endpointStr = remote.ToString();
            _kcpChannels[endpointStr] = new KcpChannel((uint)battleId, (buf, len) =>
            {
                try { _udp.Send(buf, len, remote); } catch { /* socket closed */ }
            });

            // Forward to battle room
            if (_battleRooms.TryGetValue(battleId, out var room))
            {
                room.HandleBattleReady(battlePlayerId, remote.ToString());
            }
        }

        /// <summary>可靠发送：走 KCP 通道（无会话时回退原始 UDP）</summary>
        public void SendReliable(MainPack pack, string endpointStr)
        {
            if (_kcpChannels.TryGetValue(endpointStr, out var kcp))
                kcp.SendReliable(ProtobufSerializer.SerializeMainPack(pack));
            else
                Send(pack, endpointStr);
        }

        private void HandleBattleOperation(MainPack pack, IPEndPoint remote)
        {
            if (pack.BattleInfo == null) return;

            string endpointStr = remote.ToString();
            if (!_endpointToBattlePlayerId.TryGetValue(endpointStr, out int battlePlayerId))
                return;

            if (!_battlePlayerRouting.TryGetValue(battlePlayerId, out var routing))
                return;

            if (_battleRooms.TryGetValue(routing.battleId, out var room))
            {
                var selfOp = pack.BattleInfo.SelfOperation;
                if (selfOp != null)
                {
                    // 诊断：反序列化后立即打印原始值（前30帧每帧打印）
                    int opId = pack.BattleInfo.OperationId;
                    if (opId <= 30 || opId % 30 == 0)
                    {
                    }

                    room.HandlePlayerOperation(
                        battlePlayerId,
                        selfOp,
                        pack.BattleInfo.OperationId,
                        pack.BattleInfo.ClientAckedFrame
                    );
                }
            }
        }

        private void HandleRpcCall(MainPack pack, IPEndPoint remote)
        {
            if (pack.RpcPayload == null) return;

            string endpointStr = remote.ToString();
            if (!_endpointToBattlePlayerId.TryGetValue(endpointStr, out int battlePlayerId))
                return;
            if (!_battlePlayerRouting.TryGetValue(battlePlayerId, out var routing))
                return;
            if (_battleRooms.TryGetValue(routing.battleId, out var room))
            {
                room.HandleRpcCall(battlePlayerId, pack.RpcPayload);
            }
        }

        private void HandlePing(MainPack pack, IPEndPoint remote)
        {
            string endpointStr = remote.ToString();
            if (_endpointToBattlePlayerId.TryGetValue(endpointStr, out int battlePlayerId))
            {
                long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                long clientTime = pack.Timestamp;
                long rttMs = now - clientTime;

                // EWMA smoothing for RTT
                float smoothedRtt;
                if (_playerRtt.TryGetValue(battlePlayerId, out float oldRtt))
                {
                    smoothedRtt = 0.875f * oldRtt + 0.125f * (rttMs / 1000f);
                }
                else
                {
                    smoothedRtt = rttMs / 1000f;
                }
                _playerRtt[battlePlayerId] = smoothedRtt;
            }

            // Send Pong with echo timestamp
            var pong = new MainPack
            {
                RequestCode = RequestCode.Battle,
                ActionCode = ActionCode.Pong,
                Timestamp = pack.Timestamp
            };
            Send(pong, endpointStr);
        }

        private void HandleDisconnect(MainPack pack, IPEndPoint remote)
        {
            string endpointStr = remote.ToString();
            if (_endpointToBattlePlayerId.TryRemove(endpointStr, out int battlePlayerId))
            {
                if (_battlePlayerRouting.TryRemove(battlePlayerId, out var routing))
                {
                    if (_battleRooms.TryGetValue(routing.battleId, out var room))
                    {
                        // Room will handle disconnect
                    }
                }
            }
        }

        public void Send(MainPack pack, string endpointStr)
        {
            if (!_running || string.IsNullOrEmpty(endpointStr)) return;

            try
            {
                // Parse endpoint
                var parts = endpointStr.Split(':');
                if (parts.Length != 2) return;

                string ip = parts[0];
                if (!int.TryParse(parts[1], out int port)) return;

                var endpoint = new IPEndPoint(IPAddress.Parse(ip), port);
                Send(pack, endpoint);
            }
            catch (Exception ex)
            {
                Log($"Send error to {endpointStr}: {ex.Message}");
            }
        }

        public void Send(MainPack pack, IPEndPoint endpoint)
        {
            if (!_running) return;

            try
            {
                byte[] body = ProtobufSerializer.SerializeMainPack(pack);
                if (_netSim != null && _netSim.Enabled)
                {
                    _netSim.ProcessOutgoing(body, body.Length, endpoint, GetPacketStrategy(pack));
                }
                else
                {
                    _udp.Send(body, body.Length, endpoint);
                }
            }
            catch (Exception ex) when (_running)
            {
                Log($"Send error: {ex.Message}");
            }
        }

        private static PacketStrategy GetPacketStrategy(MainPack pack)
        {
            switch (pack.ActionCode)
            {
                case ActionCode.BattleReady:
                    return PacketStrategy.RouteSetup;
                case ActionCode.Ping:
                case ActionCode.Pong:
                case ActionCode.BattleStart:
                case ActionCode.GameOver:
                    return PacketStrategy.Control;
                default:
                    return PacketStrategy.Data;
            }
        }

        public void SendToBattle(int battleId, MainPack pack, int excludeBattlePlayerId = -1)
        {
            if (!_battleRooms.TryGetValue(battleId, out _)) return;

            foreach (var kvp in _battlePlayerRouting.Values)
            {
                if (kvp.battleId == battleId)
                {
                    // Check exclude
                    foreach (var routing in _battlePlayerRouting)
                    {
                        if (routing.Value.battleId == battleId && routing.Key != excludeBattlePlayerId)
                        {
                            Send(pack, routing.Value.endpoint);
                        }
                    }
                    break;
                }
            }
        }

        /// <summary>
        /// Get RTT for a player (in milliseconds).
        /// </summary>
        public float GetPlayerRtt(int battlePlayerId)
        {
            if (_playerRtt.TryGetValue(battlePlayerId, out float rtt))
            {
                return rtt * 1000f; // Return in milliseconds
            }
            return 50f; // Default 50ms if not yet measured
        }

        /// <summary>
        /// Get smoothed RTT in seconds (for lag compensation calculations).
        /// </summary>
        public float GetPlayerRttSeconds(int battlePlayerId)
        {
            if (_playerRtt.TryGetValue(battlePlayerId, out float rtt))
            {
                return rtt;
            }
            return 0.05f; // Default 50ms
        }

        private void Log(string message)
        {
            Console.WriteLine($"[BattleUdpServer] {DateTime.Now:HH:mm:ss.fff} {message}");
        }
    }
}