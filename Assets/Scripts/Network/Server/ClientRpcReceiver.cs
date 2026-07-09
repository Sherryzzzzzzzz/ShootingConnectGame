using ShootingGame.Shared.Protocol;
using ShootingGame.Network;

namespace ShootingGame.Network.Server
{
    /// <summary>
    /// 客户端侧 RPC 接收器。
    ///
    /// 流程：
    /// 1. 服务端广播 ClientRpc 调用
    /// 2. 客户端收到 RpcCallMsg
    /// 3. 通过 NetId 找到本地 NetworkBehaviour
    /// 4. 调用 RpcMethodRegistry.Dispatch() 执行对应的 ClientRpc 方法体
    /// </summary>
    public static class ClientRpcReceiver
    {
        /// <summary>
        /// 处理来自服务端的 RPC 调用消息。
        /// </summary>
        public static void ProcessIncomingRpc(byte[] payload)
        {
            var reader = new PacketReader(payload);
            var rpcMsg = NetworkFrameSerializer.ReadRpcCall(reader);

            foreach (var entry in rpcMsg.Calls)
            {
                var behaviour = NetIdRegistry.GetBehaviour(entry.NetId);
                if (behaviour == null)
                {
                    UnityEngine.Debug.LogWarning($"[ClientRpcReceiver] No behaviour found for NetId={entry.NetId}");
                    continue;
                }

                var argsReader = new PacketReader(entry.Args);
                RpcMethodRegistry.Dispatch(behaviour, entry.MethodHash, argsReader);
            }
        }
    }
}
