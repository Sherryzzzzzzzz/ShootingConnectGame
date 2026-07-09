using System;
using UnityEngine;
using ShootingGame.Shared.ECS;
using ShootingGame.Shared.Protocol;
using ShootingGame.Network;
using ShootingGame.Network.Server;

/// <summary>
/// 网络集成桥接层。
///
/// 负责连接新网络框架（NetworkBehaviour / ClientDeltaReceiver / RPC）
/// 和现有传输层（NetworkClient）。
///
/// 挂载在 NetworkClient 所在的 GameObject 上。
/// </summary>
public class NetworkIntegrationBridge : MonoBehaviour
{
    [Header("引用")]
    [SerializeField] private NetworkClient _networkClient;

    // 新框架核心组件
    private ClientDeltaReceiver _deltaReceiver;
    private EntityManager _entityManager;

    // ClientECSWorld 引用
    private ClientECSWorld _ecsWorld;

    private void Awake()
    {
        // 获取或创建 NetworkClient
        if (_networkClient == null)
            _networkClient = GetComponent<NetworkClient>();
        if (_networkClient == null)
            _networkClient = FindFirstObjectByType<NetworkClient>();

        // 获取 ClientECSWorld
        _ecsWorld = FindFirstObjectByType<ClientECSWorld>();
        if (_ecsWorld == null)
        {
            var go = new GameObject("ClientECSWorld");
            _ecsWorld = go.AddComponent<ClientECSWorld>();
        }

        _entityManager = _ecsWorld.EntityManager;
        _deltaReceiver = new ClientDeltaReceiver(_entityManager);

        // 设置 I帧请求回调
        _deltaReceiver.SetIFrameRequestCallback(RequestIFrame);
    }

    private void OnEnable()
    {
        if (_networkClient == null) return;

        // 1. 订阅新消息事件
        _networkClient.OnDeltaState += OnDeltaStateReceived;
        _networkClient.OnRpcCall += OnRpcCallReceived;

        // 2. 连接后设置 RPC 传输回调
        _networkClient.OnConnected += OnNetworkConnected;

        if (_networkClient.IsConnected)
            OnNetworkConnected();
    }

    private void OnDisable()
    {
        if (_networkClient == null) return;

        _networkClient.OnDeltaState -= OnDeltaStateReceived;
        _networkClient.OnRpcCall -= OnRpcCallReceived;
        _networkClient.OnConnected -= OnNetworkConnected;
    }

    /// <summary>
    /// 网络连接就绪时，设置 NetworkBehaviour 的传输回调。
    /// </summary>
    private void OnNetworkConnected()
    {
        // 客户端 → 服务端：RPC 经由 NetworkClient 发送
        NetworkBehaviour.SendServerRpcTransport = (payload) =>
        {
            _networkClient.SendRawMessage(GameMessageType.RpcCall, payload, reliable: false);
        };

        Debug.Log("[NetworkIntegrationBridge] RPC transport wired to NetworkClient");
    }

    /// <summary>
    /// 处理收到的 DeltaState（I帧或P帧）。
    /// </summary>
    private void OnDeltaStateReceived(byte[] binaryPayload)
    {
        var reader = new PacketReader(binaryPayload);
        var deltaState = NetworkFrameSerializer.ReadDeltaState(reader);
        _deltaReceiver.OnDeltaStateReceived(deltaState);

        Debug.Log($"[NetworkIntegrationBridge] Received {(deltaState.IsFull ? "I-frame" : "P-frame")} tick={deltaState.ServerTick}, entities={deltaState.Entities?.Count ?? 0}");
    }

    /// <summary>
    /// 处理收到的 RPC 调用（ClientRpc）。
    /// </summary>
    private void OnRpcCallReceived(byte[] binaryPayload)
    {
        ClientRpcReceiver.ProcessIncomingRpc(binaryPayload);
    }

    /// <summary>
    /// 请求服务端发送 I帧（P帧 mismatch 累积超阈值时自动触发）。
    /// </summary>
    private void RequestIFrame()
    {
        // 发送 I帧请求（使用可靠通道）
        byte[] requestPayload = new byte[] { 0x01 }; // 1 = request I-frame
        _networkClient.SendRawMessage(GameMessageType.RpcCall, requestPayload, reliable: true);
        Debug.Log("[NetworkIntegrationBridge] Requested I-frame from server");
    }

    // ==================== 公开 API ====================

    /// <summary>
    /// 注册本地玩家的 ECS Entity → NetId 映射。
    /// 在 BattleManager.SpawnLocalPlayer 后调用。
    /// </summary>
    public void RegisterLocalEntity(Entity entity, uint netId)
    {
        _deltaReceiver.RegisterEntity(entity, netId);

        // 同时分配本地 NetId（用于 RPC 路由）
        NetIdRegistry.Allocate(NetObjectType.Player, entity);
    }

    /// <summary>
    /// 注册远程玩家的 ECS Entity → NetId 映射。
    /// </summary>
    public void RegisterRemoteEntity(Entity entity, uint netId)
    {
        _deltaReceiver.RegisterEntity(entity, netId);
        NetIdRegistry.Allocate(NetObjectType.Player, entity);
    }

    /// <summary>
    /// 通过 NetId 获取 Entity。
    /// </summary>
    public Entity GetEntity(uint netId) => NetIdRegistry.GetEntity(netId);

    /// <summary>
    /// ClientDeltaReceiver 引用（供调试用）。
    /// </summary>
    public ClientDeltaReceiver DeltaReceiver => _deltaReceiver;

    /// <summary>
    /// 重置状态（战斗结束后调用）。
    /// </summary>
    public void Reset()
    {
        _deltaReceiver.Reset();
        NetIdRegistry.Clear();
    }
}
