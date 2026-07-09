using System.Collections.Generic;
using ShootingGame.Shared.Protocol;
using ShootingGame.Network;

namespace ShootingGame.Network.Server
{
    /// <summary>
    /// 服务端 RPC 分发器。
    ///
    /// 流程：
    /// 1. 客户端发送 RpcCallMsg（经 KCP/UDP 到达服务端）
    /// 2. 服务端解析每个 RpcEntry
    /// 3. 通过 NetId 找到目标 NetworkBehaviour（从 NetIdRegistry）
    /// 4. 调用 RpcMethodRegistry.Dispatch() 执行对应的 ServerRpc 方法体
    /// </summary>
    public static class ServerRpcDispatcher
    {
        /// <summary>
        /// 处理来自客户端的 RPC 调用消息。
        /// </summary>
        /// <param name="payload">RpcCallMsg 的序列化字节</param>
        /// <param name="senderClientId">发送方的客户端 ID（用于 ClientRpc 回播时排除）</param>
        /// <param name="broadcastAction">广播到其他客户端的回调</param>
        public static void ProcessIncomingRpc(byte[] payload, int senderClientId, System.Action<byte[], int>? broadcastAction = null)
        {
            var reader = new PacketReader(payload);
            var rpcMsg = NetworkFrameSerializer.ReadRpcCall(reader);

            foreach (var entry in rpcMsg.Calls)
            {
                var behaviour = NetIdRegistry.GetBehaviour(entry.NetId);
                if (behaviour == null)
                {
                    UnityEngine.Debug.LogWarning($"[ServerRpcDispatcher] No behaviour found for NetId={entry.NetId}");
                    continue;
                }

                // 分发到注册的 RPC 处理函数
                var argsReader = new PacketReader(entry.Args);
                RpcMethodRegistry.Dispatch(behaviour, entry.MethodHash, argsReader);
            }
        }

        /// <summary>
        /// 构建并序列化 ClientRpc 调用消息（服务端 → 客户端）。
        /// </summary>
        /// <param name="calls">RPC 调用列表（已由 Source Generator 生成的代理方法填充）</param>
        /// <returns>序列化后的 RpcCallMsg 字节</returns>
        public static byte[] BuildOutgoingRpc(List<RpcEntry> calls)
        {
            var msg = new RpcCallMsg { Calls = calls };
            var w = new PacketWriter();
            NetworkFrameSerializer.WriteRpcCall(w, msg);
            return w.ToArray();
        }
    }
}
