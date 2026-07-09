using System;

namespace ShootingGame.Shared.Network
{
    /// <summary>
    /// Per-client write permission bitmap for network-synced properties.
    /// Uses an 8-bit mask — bit N set = client N has write permission.
    ///
    /// Usage:
    ///   ServerOnly   — flag=0x00, only the server can modify (default for HP, death state)
    ///   Everyone     — flag=0xFF, all clients can modify (for input/position prediction)
    ///   SpecificClient(id) — only client id can modify
    /// </summary>
    [Serializable]
    public struct NetPermission : IEquatable<NetPermission>
    {
        private byte _flag;

        public byte RawValue => _flag;

        // ── Static factories ──

        /// <summary>Only the server can modify this property.</summary>
        public static NetPermission ServerOnly => new NetPermission { _flag = 0 };

        /// <summary>All clients can modify this property (for client-predicted values).</summary>
        public static NetPermission Everyone => new NetPermission { _flag = 0xFF };

        /// <summary>Only a specific client can modify.</summary>
        public static NetPermission SpecificClient(byte clientId)
        {
            return new NetPermission { _flag = (byte)(1 << clientId) };
        }

        // ── Permission checks ──

        /// <summary>Check if the given client has write permission.</summary>
        public bool HasPermission(byte clientId)
        {
            return (_flag & (1 << clientId)) != 0;
        }

        /// <summary>Whether this is server-only (no client can write).</summary>
        public bool IsServerOnly => _flag == 0;

        /// <summary>Whether any client can write.</summary>
        public bool IsEveryone => _flag == 0xFF;

        // ── Modifiers ──

        public void GrantPermission(byte clientId)
        {
            _flag |= (byte)(1 << clientId);
        }

        public void RevokePermission(byte clientId)
        {
            _flag &= (byte)~(1 << clientId);
        }

        // ── Network serialization ──

        public void Serialize(Protocol.PacketWriter writer)
        {
            writer.WriteByte(_flag);
        }

        public void Deserialize(Protocol.PacketReader reader)
        {
            _flag = reader.ReadByte();
        }

        // ── Equality ──

        public bool Equals(NetPermission other) => _flag == other._flag;
        public override bool Equals(object obj) => obj is NetPermission p && Equals(p);
        public override int GetHashCode() => _flag.GetHashCode();
        public static bool operator ==(NetPermission a, NetPermission b) => a._flag == b._flag;
        public static bool operator !=(NetPermission a, NetPermission b) => a._flag != b._flag;
    }
}
