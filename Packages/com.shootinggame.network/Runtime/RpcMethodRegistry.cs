using System;
using System.Collections.Generic;
using ShootingGame.Shared.Protocol;

namespace ShootingGame.Network
{
    /// <summary>
    /// RPC 方法分发委托。
    /// </summary>
    /// <param name="target">RPC 目标 NetworkBehaviour</param>
    /// <param name="reader">参数读取器</param>
    public delegate void RpcInvokeDelegate(NetworkBehaviour target, PacketReader reader);

    /// <summary>
    /// 全局 RPC 方法注册表。
    /// Source Generator 为每个 [ServerRpc]/[ClientRpc] 方法自动生成注册代码。
    /// </summary>
    public static class RpcMethodRegistry
    {
        /// <summary>
        /// MethodHash → 分发逻辑 的映射。
        /// </summary>
        private static readonly Dictionary<long, RpcInvokeDelegate> _handlers =
            new Dictionary<long, RpcInvokeDelegate>();

        /// <summary>
        /// 注册一个 RPC 方法处理函数（由 Source Generator 在静态构造函数中调用）。
        /// </summary>
        public static void Register(long methodHash, RpcInvokeDelegate handler)
        {
            if (!_handlers.ContainsKey(methodHash))
            {
                _handlers[methodHash] = handler;
            }
            else
            {
                // Hash 冲突警告（概率极低，但需要知道）
                UnityEngine.Debug.LogWarning($"[RPC] Duplicate method hash: 0x{methodHash:X}. Possible hash collision!");
            }
        }

        /// <summary>
        /// 分发 RPC 调用到对应的方法处理函数。
        /// </summary>
        /// <param name="target">目标 NetworkBehaviour 实例</param>
        /// <param name="methodHash">方法标识</param>
        /// <param name="reader">参数读取器（位置已跳过 NetId + MethodHash + ReqId）</param>
        public static void Dispatch(NetworkBehaviour target, long methodHash, PacketReader reader)
        {
            if (_handlers.TryGetValue(methodHash, out var handler))
            {
                handler(target, reader);
            }
            else
            {
                UnityEngine.Debug.LogWarning($"[RPC] No handler registered for method hash: 0x{methodHash:X}");
            }
        }

        /// <summary>
        /// 获取已注册的 RPC 处理函数数量（调试用）。
        /// </summary>
        public static int HandlerCount => _handlers.Count;

        /// <summary>
        /// 清空所有注册（仅在完全重置时使用）。
        /// </summary>
        public static void Clear()
        {
            _handlers.Clear();
        }
    }
}
