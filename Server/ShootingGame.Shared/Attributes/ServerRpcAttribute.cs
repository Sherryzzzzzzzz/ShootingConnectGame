using System;

namespace ShootingGame.Network
{
    /// <summary>
    /// 标记一个方法为 ServerRPC：客户端调用，服务端执行。
    /// 方法必须在 NetworkBehaviour 子类中声明。
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class ServerRpcAttribute : Attribute
    {
        /// <summary>
        /// RPC 投递可靠性。默认走不可靠通道（配合 I/P帧兜底修复）。
        /// </summary>
        public RpcDelivery Delivery { get; set; } = RpcDelivery.Unreliable;
    }

    /// <summary>
    /// RPC 投递模式
    /// </summary>
    public enum RpcDelivery
    {
        /// <summary>走 KCP 不可靠通道，低延迟，丢包不重传</summary>
        Unreliable,
        /// <summary>走 KCP 可靠通道，有序保证，有队头阻塞风险</summary>
        Reliable
    }
}
