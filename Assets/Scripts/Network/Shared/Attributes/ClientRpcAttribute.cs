using System;

namespace ShootingGame.Network
{
    /// <summary>
    /// 标记一个方法为 ClientRPC：服务端调用，客户端执行。
    /// 方法必须在 NetworkBehaviour 子类中声明。
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class ClientRpcAttribute : Attribute
    {
        /// <summary>
        /// RPC 投递可靠性。默认走不可靠通道。
        /// </summary>
        public RpcDelivery Delivery { get; set; } = RpcDelivery.Unreliable;

        /// <summary>
        /// 目标客户端。不指定则广播给所有客户端。
        /// </summary>
        public ClientRpcTarget Target { get; set; } = ClientRpcTarget.All;
    }

    /// <summary>
    /// ClientRPC 的目标范围
    /// </summary>
    public enum ClientRpcTarget
    {
        /// <summary>广播给所有客户端</summary>
        All,
        /// <summary>仅发给拥有该 NetworkBehaviour 的客户端</summary>
        Owner,
        /// <summary>发给除 Owner 以外的所有客户端</summary>
        Others
    }
}
