using System;
using System.Collections.Generic;

namespace ShootingGame.Shared.Protocol
{
    /// <summary>
    /// I/P帧消息类型。新增到 GameMessageType 枚举中。
    /// 传输层不解析内部格式——NetVar 和 RPC 数据由 Source Generator 在 Network.Core 层序列化。
    /// </summary>

    // ==================== Delta State (I/P帧) ====================

    /// <summary>
    /// 增量状态消息。服务端 → 客户端。
    /// IsFull=true 时为 I帧（全量快照），IsFull=false 时为 P帧（增量变更）。
    /// </summary>
    public class DeltaStateMsg
    {
        /// <summary>服务端 tick 号</summary>
        public int ServerTick;

        /// <summary>true = I帧（全量），false = P帧（增量）</summary>
        public bool IsFull;

        /// <summary>P帧的基准 I帧 tick（I帧时设为 ServerTick）</summary>
        public int BaseFrameId;

        /// <summary>所有有变更的 Entity 的增量数据</summary>
        public List<EntityDelta> Entities = new List<EntityDelta>();
    }

    /// <summary>
    /// 单个 Entity 的增量数据。
    /// </summary>
    public class EntityDelta
    {
        /// <summary>全局唯一网络 ID</summary>
        public uint NetId;

        /// <summary>该 Entity 上所有有变更的 Component</summary>
        public List<ComponentDelta> Components = new List<ComponentDelta>();
    }

    /// <summary>
    /// 单个 Component 的增量数据。
    /// </summary>
    public class ComponentDelta
    {
        /// <summary>ECS ComponentTypeId（由 Source Generator 分配）</summary>
        public byte ComponentTypeId;

        /// <summary>true = 全量字段（I帧或首次同步），false = 仅变更字段（P帧）</summary>
        public bool IsFull;

        /// <summary>序列化后的字段数据。格式由对应 Component 的 Source Generator 定义。</summary>
        public byte[] Data;
    }

    // ==================== RPC Call ====================

    /// <summary>
    /// RPC 调用消息。客户端 ↔ 服务端。
    /// </summary>
    public class RpcCallMsg
    {
        /// <summary>同一 tick 内的所有 RPC 调用</summary>
        public List<RpcEntry> Calls = new List<RpcEntry>();
    }

    /// <summary>
    /// 单条 RPC 调用。
    /// </summary>
    public class RpcEntry
    {
        /// <summary>目标网络对象的 NetId</summary>
        public uint NetId;

        /// <summary>方法标识（由 Source Generator 从方法签名计算）</summary>
        public long MethodHash;

        /// <summary>RPC 请求 ID（用于可靠投递的 ACK，不可靠时可为 0）</summary>
        public uint ReqId;

        /// <summary>序列化后的参数。格式由对应 RPC 方法的 Source Generator 定义。</summary>
        public byte[] Args;
    }

    // ==================== Delta State Serialization (PacketWriter/Reader) ====================

    public static class NetworkFrameSerializer
    {
        public static void WriteDeltaState(PacketWriter w, DeltaStateMsg msg)
        {
            w.WriteInt32(msg.ServerTick);
            w.WriteBool(msg.IsFull);
            w.WriteInt32(msg.BaseFrameId);

            int entityCount = msg.Entities?.Count ?? 0;
            w.WriteByte((byte)entityCount);
            if (msg.Entities != null)
            {
                foreach (var entity in msg.Entities)
                    WriteEntityDelta(w, entity);
            }
        }

        public static DeltaStateMsg ReadDeltaState(PacketReader r)
        {
            var msg = new DeltaStateMsg
            {
                ServerTick = r.ReadInt32(),
                IsFull = r.ReadBool(),
                BaseFrameId = r.ReadInt32()
            };

            int entityCount = r.ReadByte();
            for (int i = 0; i < entityCount; i++)
                msg.Entities.Add(ReadEntityDelta(r));

            return msg;
        }

        private static void WriteEntityDelta(PacketWriter w, EntityDelta entity)
        {
            w.WriteUInt32(entity.NetId);
            int compCount = entity.Components?.Count ?? 0;
            w.WriteByte((byte)compCount);
            if (entity.Components != null)
            {
                foreach (var comp in entity.Components)
                    WriteComponentDelta(w, comp);
            }
        }

        private static EntityDelta ReadEntityDelta(PacketReader r)
        {
            var entity = new EntityDelta { NetId = r.ReadUInt32() };
            int compCount = r.ReadByte();
            for (int i = 0; i < compCount; i++)
                entity.Components.Add(ReadComponentDelta(r));
            return entity;
        }

        private static void WriteComponentDelta(PacketWriter w, ComponentDelta comp)
        {
            w.WriteByte(comp.ComponentTypeId);
            w.WriteBool(comp.IsFull);
            int dataLen = comp.Data?.Length ?? 0;
            w.WriteUInt16((ushort)dataLen);
            if (dataLen > 0)
                w.WriteBytes(comp.Data, 0, dataLen);
        }

        private static ComponentDelta ReadComponentDelta(PacketReader r)
        {
            var comp = new ComponentDelta
            {
                ComponentTypeId = r.ReadByte(),
                IsFull = r.ReadBool()
            };
            int dataLen = r.ReadUInt16();
            if (dataLen > 0)
                comp.Data = r.ReadBytes(dataLen);
            return comp;
        }

        // ==================== RPC Call Serialization ====================

        public static void WriteRpcCall(PacketWriter w, RpcCallMsg msg)
        {
            int callCount = msg.Calls?.Count ?? 0;
            w.WriteByte((byte)callCount);
            if (msg.Calls != null)
            {
                foreach (var call in msg.Calls)
                    WriteRpcEntry(w, call);
            }
        }

        public static RpcCallMsg ReadRpcCall(PacketReader r)
        {
            var msg = new RpcCallMsg();
            int callCount = r.ReadByte();
            for (int i = 0; i < callCount; i++)
                msg.Calls.Add(ReadRpcEntry(r));
            return msg;
        }

        private static void WriteRpcEntry(PacketWriter w, RpcEntry entry)
        {
            w.WriteUInt32(entry.NetId);
            w.WriteInt64(entry.MethodHash);
            w.WriteUInt32(entry.ReqId);
            int argsLen = entry.Args?.Length ?? 0;
            w.WriteUInt16((ushort)argsLen);
            if (argsLen > 0)
                w.WriteBytes(entry.Args, 0, argsLen);
        }

        private static RpcEntry ReadRpcEntry(PacketReader r)
        {
            return new RpcEntry
            {
                NetId = r.ReadUInt32(),
                MethodHash = r.ReadInt64(),
                ReqId = r.ReadUInt32(),
                Args = r.ReadBytes(r.ReadUInt16())
            };
        }
    }
}
