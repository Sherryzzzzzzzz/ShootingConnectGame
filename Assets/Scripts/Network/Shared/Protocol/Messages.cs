using ShootingGame.Shared.Ability;
using ShootingGame.Shared.Simulation;

namespace ShootingGame.Shared.Protocol
{
    /// <summary>
    /// Serialization helpers for all network message types.
    /// </summary>
    public static class Messages
    {
        #region InputMessage (Client → Server)

        public static void WriteInputMessage(PacketWriter w, InputFrame[] frames, int count)
        {
            w.WriteByte((byte)MessageType.InputMessage);
            w.WriteByte((byte)count);
            for (int i = 0; i < count; i++)
            {
                WriteInputFrame(w, frames[i]);
            }
        }

        public static InputFrame[] ReadInputMessage(PacketReader r, out int count)
        {
            count = r.ReadByte();
            var frames = new InputFrame[count];
            for (int i = 0; i < count; i++)
            {
                frames[i] = ReadInputFrame(r);
            }
            return frames;
        }

        private static void WriteInputFrame(PacketWriter w, InputFrame f)
        {
            w.WriteInt32(f.Tick);
            w.WriteVec2(f.Movement);
            // Pack booleans into a ushort
            ushort flags = 0;
            if (f.Jump) flags |= 1;
            if (f.Run) flags |= 2;
            if (f.Aim) flags |= 4;
            if (f.Fire) flags |= 8;
            if (f.Reload) flags |= 16;
            if (f.Ability3) flags |= 32;
            if (f.Ability4) flags |= 64;
            if (f.Ability1) flags |= 128;
            if (f.Ability2) flags |= 256;
            w.WriteUInt16(flags);
            w.WriteFloat(f.AimYaw);
            w.WriteFloat(f.AimPitch);
        }

        private static InputFrame ReadInputFrame(PacketReader r)
        {
            var f = new InputFrame();
            f.Tick = r.ReadInt32();
            f.Movement = r.ReadVec2();
            ushort flags = r.ReadUInt16();
            f.Jump = (flags & 1) != 0;
            f.Run = (flags & 2) != 0;
            f.Aim = (flags & 4) != 0;
            f.Fire = (flags & 8) != 0;
            f.Reload = (flags & 16) != 0;
            f.Ability3 = (flags & 32) != 0;
            f.Ability4 = (flags & 64) != 0;
            f.Ability1 = (flags & 128) != 0;
            f.Ability2 = (flags & 256) != 0;
            f.AimYaw = r.ReadFloat();
            f.AimPitch = r.ReadFloat();
            return f;
        }

        #endregion

        #region WorldStateMessage (Server → Client)

        public static void WriteWorldStateMessage(PacketWriter w, int serverTick, PlayerSnapshot[] players, int playerCount, int[] lastProcessedInputTicks)
        {
            w.WriteByte((byte)MessageType.WorldStateMessage);
            w.WriteInt32(serverTick);
            w.WriteByte((byte)playerCount);
            for (int i = 0; i < playerCount; i++)
            {
                w.WriteInt32(lastProcessedInputTicks[i]);
                WritePlayerSnapshot(w, players[i]);
            }
        }

        public static void ReadWorldStateMessage(PacketReader r, out int serverTick, out PlayerSnapshot[] players, out int[] lastProcessedInputTicks)
        {
            serverTick = r.ReadInt32();
            int count = r.ReadByte();
            players = new PlayerSnapshot[count];
            lastProcessedInputTicks = new int[count];
            for (int i = 0; i < count; i++)
            {
                lastProcessedInputTicks[i] = r.ReadInt32();
                players[i] = ReadPlayerSnapshot(r);
            }
        }

        private static void WritePlayerSnapshot(PacketWriter w, PlayerSnapshot s)
        {
            w.WriteInt32(s.Tick);
            w.WriteVec3(s.Position);
            w.WriteQuat(s.Rotation);
            w.WriteVec3(s.Velocity);
            w.WriteFloat(s.VerticalVelocity);
            w.WriteBool(s.IsGrounded);
            w.WriteByte((byte)s.State);
            w.WriteFloat(s.FireCooldown);
            w.WriteByte(s.Health);
            w.WriteInt32(s.CurrentAmmo);
            w.WriteBool(s.IsReloading);
            w.WriteFloat(s.ReloadTimer);
            w.WriteInt64(s.TagBitmask);
            w.WriteByte(s.ActiveAbilityCount);
            for (byte i = 0; i < s.ActiveAbilityCount && s.ActiveAbilities != null; i++)
                WriteAbilityInstanceData(w, s.ActiveAbilities[i]);
        }

        private static PlayerSnapshot ReadPlayerSnapshot(PacketReader r)
        {
            var snap = new PlayerSnapshot
            {
                Tick = r.ReadInt32(),
                Position = r.ReadVec3(),
                Rotation = r.ReadQuat(),
                Velocity = r.ReadVec3(),
                VerticalVelocity = r.ReadFloat(),
                IsGrounded = r.ReadBool(),
                State = (PlayerStateEnum)r.ReadByte(),
                FireCooldown = r.ReadFloat(),
                Health = r.ReadByte(),
                CurrentAmmo = r.ReadInt32(),
                IsReloading = r.ReadBool(),
                ReloadTimer = r.ReadFloat(),
                TagBitmask = r.ReadInt64(),
                ActiveAbilityCount = r.ReadByte()
            };
            snap.ActiveAbilities = new AbilityInstanceData[snap.ActiveAbilityCount];
            for (byte i = 0; i < snap.ActiveAbilityCount; i++)
                snap.ActiveAbilities[i] = ReadAbilityInstanceData(r);
            return snap;
        }

        #endregion

        #region DamageEvent (Server → Client, Reliable)

        public static void WriteDamageEvent(PacketWriter w, byte targetId, byte shooterId, byte damage, byte newHealth, Math.Vec3 hitPoint)
        {
            w.WriteByte((byte)MessageType.DamageEvent);
            w.WriteByte(targetId);
            w.WriteByte(shooterId);
            w.WriteByte(damage);
            w.WriteByte(newHealth);
            w.WriteVec3(hitPoint);
        }

        public static void ReadDamageEvent(PacketReader r, out byte targetId, out byte shooterId, out byte damage, out byte newHealth, out Math.Vec3 hitPoint)
        {
            targetId = r.ReadByte();
            shooterId = r.ReadByte();
            damage = r.ReadByte();
            newHealth = r.ReadByte();
            hitPoint = r.ReadVec3();
        }

        #endregion

        #region ConnectionRequest (Client → Server, Reliable)

        public static void WriteConnectionRequest(PacketWriter w, byte protocolVersion)
        {
            w.WriteByte((byte)MessageType.ConnectionRequest);
            w.WriteByte(protocolVersion);
        }

        public static byte ReadConnectionRequest(PacketReader r)
        {
            return r.ReadByte(); // protocolVersion
        }

        #endregion

        #region ConnectionAccepted (Server → Client, Reliable)

        public static void WriteConnectionAccepted(PacketWriter w, byte playerId, int tickRate, int serverTick)
        {
            w.WriteByte((byte)MessageType.ConnectionAccepted);
            w.WriteByte(playerId);
            w.WriteInt32(tickRate);
            w.WriteInt32(serverTick);
        }

        public static void ReadConnectionAccepted(PacketReader r, out byte playerId, out int tickRate, out int serverTick)
        {
            playerId = r.ReadByte();
            tickRate = r.ReadInt32();
            serverTick = r.ReadInt32();
        }

        #endregion

        #region PlayerJoined / PlayerLeft (Server → Client, Reliable)

        public static void WritePlayerJoined(PacketWriter w, byte playerId)
        {
            w.WriteByte((byte)MessageType.PlayerJoined);
            w.WriteByte(playerId);
        }

        public static byte ReadPlayerJoined(PacketReader r) => r.ReadByte();

        public static void WritePlayerLeft(PacketWriter w, byte playerId)
        {
            w.WriteByte((byte)MessageType.PlayerLeft);
            w.WriteByte(playerId);
        }

        public static byte ReadPlayerLeft(PacketReader r) => r.ReadByte();

        #endregion

        #region Disconnect (Client → Server, Reliable)

        public static void WriteDisconnect(PacketWriter w, byte reason)
        {
            w.WriteByte((byte)MessageType.Disconnect);
            w.WriteByte(reason);
        }

        public static byte ReadDisconnect(PacketReader r) => r.ReadByte();

        #endregion

        #region Heartbeat (Both directions, Unreliable)

        public static void WriteHeartbeat(PacketWriter w, int timestamp)
        {
            w.WriteByte((byte)MessageType.Heartbeat);
            w.WriteInt32(timestamp);
        }

        public static int ReadHeartbeat(PacketReader r) => r.ReadInt32();

        #endregion

        #region AbilityEvent (Both directions, Unreliable)

        public static void WriteAbilityEvent(PacketWriter w, AbilityEventData evt)
        {
            w.WriteByte((byte)MessageType.AbilityEvent);
            w.WriteByte(evt.PlayerId);
            w.WriteUInt16(evt.InstanceId);
            w.WriteByte(evt.AssetId);
            w.WriteByte((byte)evt.EventType);
        }

        public static AbilityEventData ReadAbilityEvent(PacketReader r)
        {
            return new AbilityEventData
            {
                PlayerId = r.ReadByte(),
                InstanceId = r.ReadUInt16(),
                AssetId = r.ReadByte(),
                EventType = (AbilityEventType)r.ReadByte()
            };
        }

        #endregion

        #region AbilityInstanceData helpers

        private static void WriteAbilityInstanceData(PacketWriter w, AbilityInstanceData d)
        {
            w.WriteUInt16(d.InstanceId);
            w.WriteByte(d.AssetId);
            w.WriteByte((byte)d.State);
            w.WriteFloat(d.CooldownRemaining);
            w.WriteFloat(d.DurationRemaining);
            w.WriteInt64(d.AppliedTagsMask);
        }

        private static AbilityInstanceData ReadAbilityInstanceData(PacketReader r)
        {
            return new AbilityInstanceData
            {
                InstanceId = r.ReadUInt16(),
                AssetId = r.ReadByte(),
                State = (AbilityState)r.ReadByte(),
                CooldownRemaining = r.ReadFloat(),
                DurationRemaining = r.ReadFloat(),
                AppliedTagsMask = r.ReadInt64()
            };
        }

        #endregion
    }
}
