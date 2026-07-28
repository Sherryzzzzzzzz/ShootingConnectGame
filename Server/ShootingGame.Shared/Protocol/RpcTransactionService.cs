// ============================================================
// RpcTransactionService.cs — 异步 RPC Request/Response
//
// 参考 SpaceBuilder RpcTransactionService（UniTask 版），
// 适配 ShootingConnectGame 使用 System.Threading.Tasks。
//
// 模式：
//   Fire-and-Forget:    reqId=0, 不等待回复
//   Request/Response:   reqId>0, 等待服务端/客户端回复
//
// 使用示例 (客户端 → 服务端):
//   var result = await RpcTransactionService.Instance
//       .SendServerRequestAsync<int>(netId, methodHash, args, timeout: 5f);
//
// 使用示例 (服务端 → 客户端):
//   var result = await RpcTransactionService.Instance
//       .SendClientRequestAsync<int>(netId, methodHash, args, targetClient, timeout: 5f);
// ============================================================

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace ShootingGame.Shared.Protocol
{
    /// <summary>
    /// RPC 事务服务 — 管理异步 Request/Response 的生命周期。
    /// 单例，在客户端和服务端各自初始化。
    /// </summary>
    public class RpcTransactionService
    {
        public static RpcTransactionService Instance { get; } = new RpcTransactionService();

        private uint _idCounter;
        private readonly ConcurrentDictionary<uint, IPendingRequest> _pendingRequests = new();

        // ========== 传输回调（由客户端或服务端注入） ==========

        /// <summary>发送 RPC 消息到服务端。</summary>
        public Action<byte[]> SendToServer { get; set; }

        /// <summary>发送 RPC 消息到指定客户端。参数: (netId, payload, targetPlayerId)</summary>
        public Action<uint, byte[], int> SendToClient { get; set; }

        // ========== RPC 响应接收入口 ==========

        /// <summary>
        /// 收到 RpcResponseMsg 时调用。由客户端或服务端的消息分发层调用。
        /// </summary>
        public void OnResponseReceived(uint reqId, byte[] returnValueBytes)
        {
            if (_pendingRequests.TryRemove(reqId, out var waiter))
            {
                waiter.Complete(returnValueBytes);
            }
        }

        // ========== 客户端 → 服务端 异步调用 ==========

        /// <summary>
        /// 向服务端发送带返回值的 RPC 请求。
        /// </summary>
        /// <param name="netId">目标 NetworkBehaviour 的 NetId（0=静态RPC）</param>
        /// <param name="methodHash">RPC 方法的 SHA256 前8字节 Hash</param>
        /// <param name="payload">序列化后的参数</param>
        /// <param name="timeoutSec">超时时间（秒）</param>
        /// <returns>服务端返回的字节数组</returns>
        public async Task<byte[]> SendServerRequestAsync(
            uint netId, long methodHash, byte[] payload, float timeoutSec = 10f)
        {
            if (SendToServer == null)
                throw new InvalidOperationException("RpcTransactionService.SendToServer 未设置");

            uint reqId = ++_idCounter;
            if (reqId == 0) reqId = ++_idCounter; // 0 保留给 Fire-and-Forget

            var tcs = new TaskCompletionSource<byte[]>();
            var waiter = new PendingRequest(tcs);
            _pendingRequests[reqId] = waiter;

            // 构造 RPC 消息并发送
            var rpcPayload = BuildRpcPayload(netId, methodHash, reqId, payload);
            SendToServer(rpcPayload);

            // 等待结果（带超时）
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSec)))
            {
                cts.Token.Register(() =>
                {
                    if (_pendingRequests.TryRemove(reqId, out _))
                        tcs.TrySetException(new TimeoutException(
                            $"RPC 超时: NetId={netId}, MethodHash=0x{methodHash:X}, ReqId={reqId}"));
                });

                try
                {
                    return await tcs.Task;
                }
                catch (TimeoutException)
                {
                    throw;
                }
            }
        }

        /// <summary>
        /// 向服务端发送带返回值的 RPC 请求（泛型版本，自动反序列化）。
        /// </summary>
        public async Task<T> SendServerRequestAsync<T>(
            uint netId, long methodHash, byte[] payload,
            Func<byte[], T> deserializer, float timeoutSec = 10f)
        {
            var result = await SendServerRequestAsync(netId, methodHash, payload, timeoutSec);
            return deserializer(result);
        }

        // ========== 服务端 → 客户端 异步调用 ==========

        /// <summary>
        /// 向指定客户端发送带返回值的 RPC 请求。
        /// </summary>
        public async Task<byte[]> SendClientRequestAsync(
            uint netId, long methodHash, byte[] payload,
            int targetPlayerId, float timeoutSec = 10f)
        {
            if (SendToClient == null)
                throw new InvalidOperationException("RpcTransactionService.SendToClient 未设置");

            uint reqId = ++_idCounter;
            if (reqId == 0) reqId = ++_idCounter;

            var tcs = new TaskCompletionSource<byte[]>();
            var waiter = new PendingRequest(tcs);
            _pendingRequests[reqId] = waiter;

            var rpcPayload = BuildRpcPayload(netId, methodHash, reqId, payload);
            SendToClient(netId, rpcPayload, targetPlayerId);

            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSec)))
            {
                cts.Token.Register(() =>
                {
                    if (_pendingRequests.TryRemove(reqId, out _))
                        tcs.TrySetException(new TimeoutException(
                            $"RPC 超时(Client): NetId={netId}, MethodHash=0x{methodHash:X}, ReqId={reqId}"));
                });

                return await tcs.Task;
            }
        }

        /// <summary>
        /// 向指定客户端发送带返回值的 RPC 请求（泛型版本）。
        /// </summary>
        public async Task<T> SendClientRequestAsync<T>(
            uint netId, long methodHash, byte[] payload,
            int targetPlayerId, Func<byte[], T> deserializer, float timeoutSec = 10f)
        {
            var result = await SendClientRequestAsync(netId, methodHash, payload, targetPlayerId, timeoutSec);
            return deserializer(result);
        }

        // ========== Fire-and-Forget ==========

        /// <summary>
        /// 客户端 → 服务端 Fire-and-Forget RPC。
        /// </summary>
        public void SendServerFireAndForget(uint netId, long methodHash, byte[] payload)
        {
            if (SendToServer == null) return;
            var rpcPayload = BuildRpcPayload(netId, methodHash, 0, payload);
            SendToServer(rpcPayload);
        }

        /// <summary>
        /// 服务端 → 客户端 Fire-and-Forget RPC。
        /// </summary>
        public void SendClientFireAndForget(uint netId, long methodHash, byte[] payload, int targetPlayerId)
        {
            if (SendToClient == null) return;
            var rpcPayload = BuildRpcPayload(netId, methodHash, 0, payload);
            SendToClient(netId, rpcPayload, targetPlayerId);
        }

        // ========== 工具方法 ==========

        /// <summary>
        /// 获取当前待处理的事务数量（调试用）。
        /// </summary>
        public int PendingCount => _pendingRequests.Count;

        /// <summary>
        /// 取消所有待处理事务。
        /// </summary>
        public void CancelAll()
        {
            foreach (var kvp in _pendingRequests)
            {
                if (_pendingRequests.TryRemove(kvp.Key, out var waiter))
                    waiter.Cancel();
            }
        }

        private static byte[] BuildRpcPayload(uint netId, long methodHash, uint reqId, byte[] args)
        {
            var w = new PacketWriter();
            w.WriteUInt32(netId);
            w.WriteInt64(methodHash);
            w.WriteUInt32(reqId);
            if (args != null && args.Length > 0)
                w.WriteBytes(args);
            return w.ToArray();
        }

        // ========== 内部类型 ==========

        private interface IPendingRequest
        {
            void Complete(byte[] data);
            void Cancel();
        }

        private class PendingRequest : IPendingRequest
        {
            private readonly TaskCompletionSource<byte[]> _tcs;
            public PendingRequest(TaskCompletionSource<byte[]> tcs) => _tcs = tcs;
            public void Complete(byte[] data) => _tcs.TrySetResult(data);
            public void Cancel() => _tcs.TrySetCanceled();
        }
    }

    // ============================================================
    // RpcBatchExtensions.cs — 批量 RPC 调用辅助（参考 SpaceBuilder）
    // ============================================================

    /// <summary>
    /// 批量 RPC 操作扩展 — 用于服务端向多个客户端广播并收集结果。
    /// 参考 SpaceBuilder RpcBatchExtensions（UniTask 版）。
    /// </summary>
    public static class RpcBatchExtensions
    {
        /// <summary>
        /// 向所有客户端广播 Fire-and-Forget RPC。
        /// </summary>
        public static void BroadcastFireAndForget(
            this RpcTransactionService service,
            uint netId, long methodHash, byte[] payload,
            System.Collections.Generic.IEnumerable<int> playerIds)
        {
            if (service.SendToClient == null) return;

            foreach (var playerId in playerIds)
            {
                service.SendClientFireAndForget(netId, methodHash, payload, playerId);
            }
        }

        /// <summary>
        /// 向所有客户端发送 Request 并等待所有结果。
        /// </summary>
        public static async Task<System.Collections.Generic.Dictionary<int, T>> BroadcastAndWait<T>(
            this RpcTransactionService service,
            uint netId, long methodHash, byte[] payload,
            System.Collections.Generic.IEnumerable<int> playerIds,
            Func<byte[], T> deserializer,
            float timeoutSec = 10f)
        {
            var results = new System.Collections.Generic.Dictionary<int, T>();
            var tasks = new System.Collections.Generic.List<Task<(int playerId, T result, bool success)>>();

            foreach (var playerId in playerIds)
            {
                tasks.Add(SafeCall(service, netId, methodHash, payload, playerId, deserializer, timeoutSec));
            }

            var taskResults = await Task.WhenAll(tasks);

            foreach (var (playerId, result, success) in taskResults)
            {
                if (success)
                    results[playerId] = result;
            }

            return results;
        }

        private static async Task<(int playerId, T result, bool success)> SafeCall<T>(
            RpcTransactionService service,
            uint netId, long methodHash, byte[] payload,
            int playerId, Func<byte[], T> deserializer, float timeoutSec)
        {
            try
            {
                var result = await service.SendClientRequestAsync(
                    netId, methodHash, payload, playerId, timeoutSec);
                return (playerId, deserializer(result), true);
            }
            catch (Exception)
            {
                return (playerId, default, false);
            }
        }
    }
}
