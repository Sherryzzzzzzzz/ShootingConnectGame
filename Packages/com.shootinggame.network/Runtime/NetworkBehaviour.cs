using System;
using System.Collections.Generic;
using ShootingGame.Shared.ECS;

namespace ShootingGame.Network
{
    /// <summary>
    /// 纯 C# 抽象基类 — 网络对象的逻辑入口。
    /// 不与 MonoBehaviour 绑定，服务端和客户端共用。
    ///
    /// 职责：
    /// - 持有 NetId 和 ECS Entity 引用
    /// - 提供 RPC 发送基础设施
    /// - 不持有同步数据（数据在 ECS Component 上）
    /// </summary>
    public abstract class NetworkBehaviour
    {
        /// <summary>全局唯一网络 ID，由 NetIdRegistry 分配。</summary>
        public uint NetId { get; internal set; }

        /// <summary>关联的 ECS Entity。</summary>
        public Entity Entity { get; internal set; }

        /// <summary>关联的 EntityManager，用于读写 ECS Component。</summary>
        protected EntityManager EntityManager { get; private set; }

        /// <summary>当前是否已绑定到 Entity。</summary>
        public bool IsBound => Entity.IsValid && NetId != 0;

        // ========== 传输钩子（由 Client/Server 端设置） ==========

        /// <summary>客户端 → 服务端的 RPC 发送回调。客户端在连接时设置。</summary>
        public static Action<byte[]> SendServerRpcTransport;

        /// <summary>服务端 → 客户端的 RPC 发送回调。第一个参数是 NetId，第二个是 payload。</summary>
        public static Action<uint, byte[], ClientRpcTarget> SendClientRpcTransport;

        // ========== Entity 绑定 ==========

        /// <summary>
        /// 将 NetworkBehaviour 绑定到指定 Entity（服务端或客户端调用）。
        /// </summary>
        public void Bind(Entity entity, EntityManager entityManager, NetObjectType objectType)
        {
            Entity = entity;
            EntityManager = entityManager;
            NetIdRegistry.Allocate(objectType, entity, this);
            OnBind();
        }

        /// <summary>
        /// 解绑并释放 NetId。
        /// </summary>
        public void Unbind()
        {
            if (NetId != 0)
            {
                NetIdRegistry.Release(NetId);
                NetId = 0;
            }
            Entity = Entity.Invalid;
            EntityManager = null;
            OnUnbind();
        }

        // ========== 生命周期回调 ==========

        /// <summary>绑定到 Entity 后调用。</summary>
        protected virtual void OnBind() { }

        /// <summary>解绑前调用。</summary>
        protected virtual void OnUnbind() { }

        // ========== RPC 发送 ==========

        /// <summary>
        /// 发送 ServerRPC。客户端调用 → 服务端执行。
        /// Payload 由 Source Generator 生成的方法填充。
        /// </summary>
        protected void SendServerRpc(byte[] rpcPayload)
        {
            if (!IsBound) return;
            SendServerRpcTransport?.Invoke(rpcPayload);
        }

        /// <summary>
        /// 发送 ClientRPC。服务端调用 → 客户端执行。
        /// </summary>
        protected void SendClientRpc(byte[] rpcPayload, ClientRpcTarget target = ClientRpcTarget.All)
        {
            if (!IsBound) return;
            SendClientRpcTransport?.Invoke(NetId, rpcPayload, target);
        }

        // ========== Component 访问辅助 ==========

        /// <summary>
        /// 获取本 Entity 的指定 Component。不存在则返回默认值。
        /// </summary>
        protected T GetComponent<T>() where T : struct
        {
            if (EntityManager == null || !Entity.IsValid)
                return default;
            if (EntityManager.TryGetComponent<T>(Entity, out var component))
                return component;
            return default;
        }

        /// <summary>
        /// 设置本 Entity 的指定 Component。
        /// </summary>
        protected void SetComponent<T>(T component) where T : struct
        {
            if (EntityManager == null || !Entity.IsValid) return;
            EntityManager.SetComponent(Entity, component);
        }

        /// <summary>
        /// 检查本 Entity 是否有指定 Component。
        /// </summary>
        protected bool HasComponent<T>() where T : struct
        {
            if (EntityManager == null || !Entity.IsValid) return false;
            return EntityManager.HasComponent<T>(Entity);
        }
    }
}
