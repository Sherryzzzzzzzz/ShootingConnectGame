using UnityEngine;
using ShootingGame.Shared.Protocol;

namespace ShootingGame.Network.Server
{
    /// <summary>
    /// 服务端启动引导。
    /// 创建并连接 ServerTransport、ServerTickLoop、RPC 分发。
    ///
    /// 挂载在场景中的 GameObject 上（或由 AutoInit 自动创建）。
    /// </summary>
    public class ServerBootstrap : MonoBehaviour
    {
        [Header("Server Config")]
        [SerializeField] private int _port = 7777;
        [SerializeField] private int _tickRate = 20;

        private ServerTransport _transport;
        private ServerTickLoop _tickLoop;

        private void Awake()
        {
            // 仅在 headless 或 Editor 的"Run Server"模式下启动
            if (!Application.isBatchMode && !Debug.isDebugBuild)
            {
                Destroy(gameObject);
                return;
            }
        }

        private void Start()
        {
            if (!Application.isBatchMode)
            {
                Debug.Log("[ServerBootstrap] Skipped — not in batch mode. Use -batchmode -nographics to run server.");
                return;
            }

            StartServer();
        }

        /// <summary>
        /// 手动启动服务端（Editor 测试用）。
        /// </summary>
        [ContextMenu("Start Server")]
        public void StartServer()
        {
            // 1. 创建传输层
            _transport = new ServerTransport(_port);
            _transport.OnMessageReceived += OnMessageReceived;
            _transport.Start();

            // 2. 创建 tick 循环
            _tickLoop = ServerTickLoop.Create(_tickRate);

            // 3. 连接发送回调：ServerTickLoop → ServerTransport
            _tickLoop.SetSendCallback((clientId, data) =>
            {
                _transport.Send(clientId, data);
            });

            // 4. 启动 tick
            _tickLoop.Start();

            Debug.Log($"[ServerBootstrap] Server fully started on port {_port}, tickRate={_tickRate}Hz");
        }

        /// <summary>
        /// 处理收到的消息（从 ServerTransport 转发）。
        /// </summary>
        private void OnMessageReceived(byte[] data, int senderClientId)
        {
            try
            {
                var gameMsg = ProtobufSerializer.DeserializeGameMessage(data);

                switch (gameMsg.MsgType)
                {
                    case GameMessageType.ConnectionRequest:
                        HandleConnectionRequest(gameMsg, senderClientId);
                        break;

                    case GameMessageType.InputMessage:
                        HandleInputMessage(gameMsg, senderClientId);
                        break;

                    case GameMessageType.RpcCall:
                        // 服务端收到客户端的 ServerRpc 调用
                        ServerRpcDispatcher.ProcessIncomingRpc(gameMsg.BinaryPayload, senderClientId);
                        break;

                    case GameMessageType.Heartbeat:
                        // 心跳——可用于 RTT 计算
                        break;

                    case GameMessageType.Disconnect:
                        HandleDisconnect(senderClientId);
                        break;
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[ServerBootstrap] Message processing error: {ex.Message}");
            }
        }

        private void HandleConnectionRequest(GameMessage msg, int clientId)
        {
            // 发送 ConnectionAccepted
            var acceptedMsg = new GameMessage
            {
                MsgType = GameMessageType.ConnectionAccepted,
                ConnectionAccepted = new ConnectionAcceptedMsg
                {
                    PlayerId = (byte)clientId,
                    TickRate = _tickRate,
                    ServerTick = _tickLoop.CurrentTick
                }
            };
            var bytes = ProtobufSerializer.SerializeGameMessage(acceptedMsg);
            _transport.Send(clientId, bytes);

            // 在服务器模拟中注册玩家
            var spawnPos = GetSpawnPosition(clientId);
            _tickLoop.AddPlayer(clientId, spawnPos);

            Debug.Log($"[ServerBootstrap] Client {clientId} connected, assigned PlayerId={clientId}");
        }

        private void HandleInputMessage(GameMessage msg, int clientId)
        {
            if (msg.InputBatch?.Frames == null) return;

            var frames = new ShootingGame.Shared.Simulation.InputFrame[msg.InputBatch.Frames.Count];
            for (int i = 0; i < frames.Length; i++)
                frames[i] = ProtobufSerializer.ToInputFrame(msg.InputBatch.Frames[i]);

            _tickLoop.ReceiveInput(clientId, frames);
        }

        private void HandleDisconnect(int clientId)
        {
            _tickLoop.RemovePlayer(clientId);
            Debug.Log($"[ServerBootstrap] Client {clientId} disconnected");
        }

        private void OnDestroy()
        {
            _transport?.Dispose();
            _tickLoop?.Stop();
        }

        private static ShootingGame.Shared.Math.Vec3 GetSpawnPosition(int clientId)
        {
            // 简易生成点分配
            float x = (clientId % 2 == 0) ? -3f : 3f;
            float z = (clientId / 2) * 2f - 2f;
            return new ShootingGame.Shared.Math.Vec3(x, 0.1f, z);
        }
    }
}
