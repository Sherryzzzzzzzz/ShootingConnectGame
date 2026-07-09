using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using ShootingGame.Shared.Protocol;
using UnityEngine;

namespace ShootingGame.Network.Server
{
    /// <summary>
    /// 服务端 UDP 传输层。
    /// 管理多个客户端连接，收发 Protobuf 编码的消息。
    /// </summary>
    public class ServerTransport : IDisposable
    {
        public int Port { get; }

        private UdpClient _udp;
        private Thread _receiveThread;
        private volatile bool _running;

        /// <summary>客户端地址 → 客户端 ID 映射</summary>
        private readonly Dictionary<IPEndPoint, int> _endpointToClientId = new Dictionary<IPEndPoint, int>();
        private readonly Dictionary<int, IPEndPoint> _clientIdToEndpoint = new Dictionary<int, IPEndPoint>();
        private int _nextClientId = 1;

        /// <summary>收到完整消息时触发 (raw bytes, senderClientId)</summary>
        public event Action<byte[], int>? OnMessageReceived;

        public ServerTransport(int port = 7777)
        {
            Port = port;
        }

        /// <summary>
        /// 启动 UDP 监听。
        /// </summary>
        public void Start()
        {
            _udp = new UdpClient(Port, AddressFamily.InterNetwork);
            _running = true;

            _receiveThread = new Thread(ReceiveLoop)
            {
                IsBackground = true,
                Name = "ServerTransport_Receive"
            };
            _receiveThread.Start();

            Debug.Log($"[ServerTransport] Listening on port {Port}");
        }

        /// <summary>
        /// 发送数据到指定客户端。
        /// </summary>
        public void Send(int clientId, byte[] data)
        {
            if (!_running || _udp == null) return;
            if (!_clientIdToEndpoint.TryGetValue(clientId, out var endpoint)) return;

            try
            {
                _udp.Send(data, data.Length, endpoint);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ServerTransport] Send error to client {clientId}: {ex.Message}");
            }
        }

        /// <summary>
        /// 停止监听。
        /// </summary>
        public void Stop()
        {
            _running = false;
            try { _udp?.Close(); } catch { }
        }

        public void Dispose() => Stop();

        private void ReceiveLoop()
        {
            while (_running)
            {
                try
                {
                    IPEndPoint remote = null;
                    byte[] data = _udp.Receive(ref remote);

                    if (data.Length < 1) continue;

                    // 注册新客户端
                    if (!_endpointToClientId.TryGetValue(remote, out int clientId))
                    {
                        clientId = _nextClientId++;
                        _endpointToClientId[remote] = clientId;
                        _clientIdToEndpoint[clientId] = remote;
                        Debug.Log($"[ServerTransport] New client {clientId} from {remote}");
                    }

                    // 分发到主线程处理
                    UnityMainThreadDispatcher.Instance.Enqueue(() =>
                    {
                        OnMessageReceived?.Invoke(data, clientId);
                    });
                }
                catch (Exception ex) when (_running)
                {
                    Debug.LogError($"[ServerTransport] Receive error: {ex.Message}");
                }
            }
        }
    }
}
