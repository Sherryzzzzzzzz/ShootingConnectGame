using System;
using System.Collections.Generic;
using ShootingGame.Shared.ECS;

namespace ShootingGame.Network
{
    /// <summary>
    /// 网络对象类型枚举。高 16 位用于标识 Entity 类型。
    /// </summary>
    public enum NetObjectType : ushort
    {
        Invalid = 0,
        Player = 1,
        Projectile = 2,
        Pickup = 3,
        Structure = 4,
        // 留空间给后续类型 (5-65535)
    }

    /// <summary>
    /// 全局 NetId 分配器和注册表。
    /// NetId 格式：[类型:16bit][序列号:16bit]
    /// 服务端分配，客户端使用相同的 NetId 查找本地 Entity。
    /// </summary>
    public static class NetIdRegistry
    {
        private static readonly Dictionary<ushort, ushort> _typeCounters = new Dictionary<ushort, ushort>();
        private static readonly Dictionary<uint, Entity> _netIdToEntity = new Dictionary<uint, Entity>();
        private static readonly Dictionary<Entity, uint> _entityToNetId = new Dictionary<Entity, uint>();
        private static readonly Dictionary<uint, NetworkBehaviour> _netIdToBehaviour = new Dictionary<uint, NetworkBehaviour>();

        /// <summary>
        /// 为指定类型的 Entity 分配一个全局唯一 NetId。
        /// </summary>
        public static uint Allocate(NetObjectType type, Entity entity, NetworkBehaviour behaviour = null)
        {
            ushort typeId = (ushort)type;
            if (!_typeCounters.TryGetValue(typeId, out ushort counter))
                counter = 0;

            counter++;
            _typeCounters[typeId] = counter;

            uint netId = ((uint)typeId << 16) | counter;
            _netIdToEntity[netId] = entity;
            _entityToNetId[entity] = netId;

            if (behaviour != null)
            {
                behaviour.NetId = netId;
                _netIdToBehaviour[netId] = behaviour;
            }

            return netId;
        }

        /// <summary>
        /// 释放 NetId（Entity 销毁时调用）。
        /// </summary>
        public static void Release(uint netId)
        {
            if (_netIdToEntity.TryGetValue(netId, out var entity))
            {
                _entityToNetId.Remove(entity);
                _netIdToEntity.Remove(netId);
            }
            _netIdToBehaviour.Remove(netId);
        }

        /// <summary>
        /// 释放指定 Entity 关联的 NetId。
        /// </summary>
        public static void Release(Entity entity)
        {
            if (_entityToNetId.TryGetValue(entity, out uint netId))
                Release(netId);
        }

        /// <summary>
        /// 通过 NetId 查找 Entity。
        /// </summary>
        public static Entity GetEntity(uint netId)
        {
            return _netIdToEntity.TryGetValue(netId, out var entity) ? entity : Entity.Invalid;
        }

        /// <summary>
        /// 通过 Entity 查找 NetId。
        /// </summary>
        public static uint GetNetId(Entity entity)
        {
            return _entityToNetId.TryGetValue(entity, out uint netId) ? netId : 0;
        }

        /// <summary>
        /// 通过 NetId 查找 NetworkBehaviour。
        /// </summary>
        public static NetworkBehaviour GetBehaviour(uint netId)
        {
            return _netIdToBehaviour.TryGetValue(netId, out var behaviour) ? behaviour : null;
        }

        /// <summary>
        /// 获取 NetId 中的对象类型。
        /// </summary>
        public static NetObjectType GetType(uint netId) => (NetObjectType)(netId >> 16);

        /// <summary>
        /// 清空所有注册（仅在完全重置时使用）。
        /// </summary>
        public static void Clear()
        {
            _typeCounters.Clear();
            _netIdToEntity.Clear();
            _entityToNetId.Clear();
            _netIdToBehaviour.Clear();
        }
    }
}
