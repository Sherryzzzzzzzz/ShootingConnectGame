using System;
using System.Collections.Generic;
using System.IO;
using Google.Protobuf;
using ShootingGame.Shared.Ability;
using ShootingGame.Shared.Math;
using ShootingGame.Shared.Simulation;

namespace ShootingGame.Shared.Protocol
{
    /// <summary>
    /// Protobuf wire-format serializer for all network messages.
    /// Uses Google.Protobuf's CodedOutputStream/CodedInputStream for
    /// standard protobuf varint + length-delimited encoding.
    ///
    /// Wire format per field: (field_number &lt;&lt; 3) | wire_type, then value.
    /// Wire types: 0=Varint, 1=Fixed64, 2=LengthDelimited, 5=Fixed32
    /// </summary>
    public static class ProtobufSerializer
    {
        #region MainPack

        public static byte[] SerializeMainPack(MainPack pack)
        {
            using var ms = new MemoryStream();
            using var cos = new CodedOutputStream(ms);

            if (pack.RequestCode != RequestCode.None)
                cos.WriteInt32Tag(1).WriteEnum((int)pack.RequestCode);
            if (pack.ActionCode != ActionCode.Login)
                cos.WriteInt32Tag(2).WriteEnum((int)pack.ActionCode);
            if (pack.ReturnCode != ReturnCode.Success)
                cos.WriteInt32Tag(3).WriteEnum((int)pack.ReturnCode);
            if (pack.Timestamp != 0)
                cos.WriteInt64Tag(4).WriteInt64(pack.Timestamp);
            if (!string.IsNullOrEmpty(pack.Str))
                cos.WriteStringTag(5).WriteString(pack.Str);
            if (pack.IntVal != 0)
                cos.WriteInt32Tag(6).WriteInt32(pack.IntVal);

            if (pack.BattleInfo != null)
                SerializeSubMessage(cos, 7, subCos => SerializeBattleInfo(subCos, pack.BattleInfo));
            if (pack.UserInfo != null)
                SerializeSubMessage(cos, 8, subCos => SerializeUserInfo(subCos, pack.UserInfo));
            if (pack.RoomInfo != null)
                SerializeSubMessage(cos, 9, subCos => SerializeRoomInfo(subCos, pack.RoomInfo));
            if (pack.RoomInfos != null)
            {
                foreach (var ri in pack.RoomInfos)
                    SerializeSubMessage(cos, 10, subCos => SerializeRoomInfo(subCos, ri));
            }
            if (pack.BattlePlayerPacks != null)
            {
                foreach (var bp in pack.BattlePlayerPacks)
                    SerializeSubMessage(cos, 11, subCos => SerializeBattlePlayerPack(subCos, bp));
            }
            if (pack.RpcPayload != null && pack.RpcPayload.Length > 0)
                cos.WriteBytesTag(12).WriteBytes(Google.Protobuf.ByteString.CopyFrom(pack.RpcPayload));
            if (pack.ScoreEntries != null)
            {
                foreach (var se in pack.ScoreEntries)
                    SerializeSubMessage(cos, 13, subCos => SerializeScoreEntryMsg(subCos, se));
            }

            cos.Flush();
            return ms.ToArray();
        }

        public static MainPack DeserializeMainPack(byte[] data)
        {
            var pack = new MainPack();
            var cis = new CodedInputStream(data);

            while (cis.ReadTag(out uint tag))
            {
                int field = (int)(tag >> 3);
                switch (field)
                {
                    case 1: pack.RequestCode = (RequestCode)cis.ReadEnum(); break;
                    case 2: pack.ActionCode = (ActionCode)cis.ReadEnum(); break;
                    case 3: pack.ReturnCode = (ReturnCode)cis.ReadEnum(); break;
                    case 4: pack.Timestamp = cis.ReadInt64(); break;
                    case 5: pack.Str = cis.ReadString(); break;
                    case 6: pack.IntVal = cis.ReadInt32(); break;
                    case 7:
                    {
                        var subCis = cis.ReadMessage();
                        pack.BattleInfo = DeserializeBattleInfo(subCis);
                        break;
                    }
                    case 8:
                    {
                        var subCis = cis.ReadMessage();
                        pack.UserInfo = DeserializeUserInfo(subCis);
                        break;
                    }
                    case 9:
                    {
                        var subCis = cis.ReadMessage();
                        pack.RoomInfo = DeserializeRoomInfo(subCis);
                        break;
                    }
                    case 10:
                    {
                        var subCis = cis.ReadMessage();
                        pack.RoomInfos.Add(DeserializeRoomInfo(subCis));
                        break;
                    }
                    case 11:
                    {
                        var subCis = cis.ReadMessage();
                        pack.BattlePlayerPacks.Add(DeserializeBattlePlayerPack(subCis));
                        break;
                    }
                    case 12: pack.RpcPayload = cis.ReadBytes().ToByteArray(); break;
                    case 13:
                    {
                        var subCis = cis.ReadMessage();
                        pack.ScoreEntries.Add(DeserializeScoreEntryMsg(subCis));
                        break;
                    }
                }
            }
            return pack;
        }

        private static void SerializeScoreEntryMsg(CodedOutputStream cos, ScoreEntryMsg se)
        {
            if (se.PlayerId != 0) cos.WriteInt32Tag(1).WriteInt32(se.PlayerId);
            if (!string.IsNullOrEmpty(se.PlayerName)) cos.WriteStringTag(2).WriteString(se.PlayerName);
            if (se.Kills != 0) cos.WriteInt32Tag(3).WriteInt32(se.Kills);
            if (se.Deaths != 0) cos.WriteInt32Tag(4).WriteInt32(se.Deaths);
            cos.Flush();
        }

        private static ScoreEntryMsg DeserializeScoreEntryMsg(CodedInputStream cis)
        {
            var se = new ScoreEntryMsg();
            while (cis.ReadTag(out uint tag))
            {
                switch (tag >> 3)
                {
                    case 1: se.PlayerId = cis.ReadInt32(); break;
                    case 2: se.PlayerName = cis.ReadString(); break;
                    case 3: se.Kills = cis.ReadInt32(); break;
                    case 4: se.Deaths = cis.ReadInt32(); break;
                }
            }
            return se;
        }

        /// <summary>Write a full MainPack including 4-byte big-endian length prefix (TCP frame format).</summary>
        public static byte[] SerializeMainPackFrame(MainPack pack)
        {
            byte[] body = SerializeMainPack(pack);
            byte[] frame = new byte[4 + body.Length];
            frame[0] = (byte)(body.Length >> 24);
            frame[1] = (byte)(body.Length >> 16);
            frame[2] = (byte)(body.Length >> 8);
            frame[3] = (byte)(body.Length);
            Buffer.BlockCopy(body, 0, frame, 4, body.Length);
            return frame;
        }

        #endregion

        #region User / Room / BattlePlayerPack

        private static void SerializeUserInfo(CodedOutputStream cos, UserInfo user)
        {
            if (user.UserId != 0) cos.WriteInt32Tag(1).WriteInt32(user.UserId);
            if (!string.IsNullOrEmpty(user.Username)) cos.WriteStringTag(2).WriteString(user.Username);
            if (!string.IsNullOrEmpty(user.Password)) cos.WriteStringTag(3).WriteString(user.Password);
            cos.Flush();
        }

        private static UserInfo DeserializeUserInfo(CodedInputStream cis)
        {
            var u = new UserInfo();
            while (cis.ReadTag(out uint tag))
            {
                switch (tag >> 3)
                {
                    case 1: u.UserId = cis.ReadInt32(); break;
                    case 2: u.Username = cis.ReadString(); break;
                    case 3: u.Password = cis.ReadString(); break;
                }
            }
            return u;
        }

        private static void SerializeRoomInfo(CodedOutputStream cos, RoomInfo ri)
        {
            if (ri.RoomId != 0) cos.WriteInt32Tag(1).WriteInt32(ri.RoomId);
            if (!string.IsNullOrEmpty(ri.RoomName)) cos.WriteStringTag(2).WriteString(ri.RoomName);
            if (!string.IsNullOrEmpty(ri.CreatorName)) cos.WriteStringTag(3).WriteString(ri.CreatorName);
            if (ri.PlayerCount != 0) cos.WriteInt32Tag(4).WriteInt32(ri.PlayerCount);
            if (ri.MaxPlayers != 0) cos.WriteInt32Tag(5).WriteInt32(ri.MaxPlayers);
            if (ri.Status != 0) cos.WriteInt32Tag(6).WriteInt32(ri.Status);
            cos.Flush();
        }

        private static RoomInfo DeserializeRoomInfo(CodedInputStream cis)
        {
            var ri = new RoomInfo();
            while (cis.ReadTag(out uint tag))
            {
                switch (tag >> 3)
                {
                    case 1: ri.RoomId = cis.ReadInt32(); break;
                    case 2: ri.RoomName = cis.ReadString(); break;
                    case 3: ri.CreatorName = cis.ReadString(); break;
                    case 4: ri.PlayerCount = cis.ReadInt32(); break;
                    case 5: ri.MaxPlayers = cis.ReadInt32(); break;
                    case 6: ri.Status = cis.ReadInt32(); break;
                }
            }
            return ri;
        }

        private static void SerializeBattlePlayerPack(CodedOutputStream cos, BattlePlayerPack bp)
        {
            if (bp.UserId != 0) cos.WriteInt32Tag(1).WriteInt32(bp.UserId);
            if (bp.BattleId != 0) cos.WriteInt32Tag(2).WriteInt32(bp.BattleId);
            if (!string.IsNullOrEmpty(bp.PlayerName)) cos.WriteStringTag(3).WriteString(bp.PlayerName);
            if (bp.TeamId != 0) cos.WriteInt32Tag(4).WriteInt32(bp.TeamId);
            cos.Flush();
        }

        private static BattlePlayerPack DeserializeBattlePlayerPack(CodedInputStream cis)
        {
            var bp = new BattlePlayerPack();
            while (cis.ReadTag(out uint tag))
            {
                switch (tag >> 3)
                {
                    case 1: bp.UserId = cis.ReadInt32(); break;
                    case 2: bp.BattleId = cis.ReadInt32(); break;
                    case 3: bp.PlayerName = cis.ReadString(); break;
                    case 4: bp.TeamId = cis.ReadInt32(); break;
                }
            }
            return bp;
        }

        #endregion

        #region BattleInfo

        private static void SerializeBattleInfo(CodedOutputStream cos, BattleInfo bi)
        {
            if (bi.OperationId != 0) cos.WriteInt32Tag(1).WriteInt32(bi.OperationId);
            if (bi.BattleId != 0) cos.WriteInt32Tag(2).WriteInt32(bi.BattleId);
            if (bi.RandSeed != 0) cos.WriteInt32Tag(3).WriteInt32(bi.RandSeed);
            if (bi.ClientAckedFrame != 0) cos.WriteInt32Tag(4).WriteInt32(bi.ClientAckedFrame);

            if (bi.SelfOperation != null)
                SerializeSubMessage(cos, 5, subCos => SerializePlayerOperation(subCos, bi.SelfOperation));
            if (bi.Operations != null)
            {
                foreach (var op in bi.Operations)
                    SerializeSubMessage(cos, 6, subCos => SerializePlayerOperation(subCos, op));
            }
            if (bi.AllPlayerOperations != null)
            {
                foreach (var apo in bi.AllPlayerOperations)
                    SerializeSubMessage(cos, 7, subCos => SerializeAllPlayerOperation(subCos, apo));
            }
            if (bi.HitEvents != null)
            {
                foreach (var he in bi.HitEvents)
                    SerializeSubMessage(cos, 8, subCos => SerializeHitEventMsg(subCos, he));
            }
            if (bi.PlayerStates != null)
            {
                foreach (var ps in bi.PlayerStates)
                    SerializeSubMessage(cos, 9, subCos => SerializePlayerStateMsg(subCos, ps));
            }
            if (bi.BattlePlayers != null)
            {
                foreach (var bpi in bi.BattlePlayers)
                    SerializeSubMessage(cos, 10, subCos => SerializeBattlePlayerInfo(subCos, bpi));
            }
            if (bi.SpawnPoints != null)
            {
                foreach (var sp in bi.SpawnPoints)
                    SerializeSubMessage(cos, 11, subCos => SerializeSpawnPointMsg(subCos, sp));
            }
            if (bi.CollisionData != null && bi.CollisionData.Length > 0)
                cos.WriteBytesTag(12).WriteBytes(Google.Protobuf.ByteString.CopyFrom(bi.CollisionData));
            cos.Flush();
        }

        private static BattleInfo DeserializeBattleInfo(CodedInputStream cis)
        {
            var bi = new BattleInfo();
            while (cis.ReadTag(out uint tag))
            {
                switch (tag >> 3)
                {
                    case 1: bi.OperationId = cis.ReadInt32(); break;
                    case 2: bi.BattleId = cis.ReadInt32(); break;
                    case 3: bi.RandSeed = cis.ReadInt32(); break;
                    case 4: bi.ClientAckedFrame = cis.ReadInt32(); break;
                    case 5:
                    {
                        var subCis = cis.ReadMessage();
                        bi.SelfOperation = DeserializePlayerOperation(subCis);
                        break;
                    }
                    case 6:
                    {
                        var subCis = cis.ReadMessage();
                        bi.Operations.Add(DeserializePlayerOperation(subCis));
                        break;
                    }
                    case 7:
                    {
                        var subCis = cis.ReadMessage();
                        bi.AllPlayerOperations.Add(DeserializeAllPlayerOperation(subCis));
                        break;
                    }
                    case 8:
                    {
                        var subCis = cis.ReadMessage();
                        bi.HitEvents.Add(DeserializeHitEventMsg(subCis));
                        break;
                    }
                    case 9:
                    {
                        var subCis = cis.ReadMessage();
                        bi.PlayerStates.Add(DeserializePlayerStateMsg(subCis));
                        break;
                    }
                    case 10:
                    {
                        var subCis = cis.ReadMessage();
                        bi.BattlePlayers.Add(DeserializeBattlePlayerInfo(subCis));
                        break;
                    }
                    case 11:
                    {
                        var subCis = cis.ReadMessage();
                        bi.SpawnPoints.Add(DeserializeSpawnPointMsg(subCis));
                        break;
                    }
                    case 12: bi.CollisionData = cis.ReadBytes().ToByteArray(); break;
                }
            }
            return bi;
        }

        private static void SerializeSpawnPointMsg(CodedOutputStream cos, SpawnPointMsg sp)
        {
            if (sp.Position.x != 0 || sp.Position.y != 0 || sp.Position.z != 0)
                SerializeSubMessage(cos, 1, subCos => SerializeVec3(subCos, sp.Position));
            if (sp.Yaw != 0) cos.WriteFloatTag(2).WriteFloat(sp.Yaw);
            if (sp.TeamId != 0) cos.WriteInt32Tag(3).WriteInt32(sp.TeamId);
            cos.Flush();
        }

        private static SpawnPointMsg DeserializeSpawnPointMsg(CodedInputStream cis)
        {
            var sp = new SpawnPointMsg();
            while (cis.ReadTag(out uint tag))
            {
                switch (tag >> 3)
                {
                    case 1: { var subCis = cis.ReadMessage(); sp.Position = DeserializeVec3(subCis); break; }
                    case 2: sp.Yaw = cis.ReadFloat(); break;
                    case 3: sp.TeamId = cis.ReadInt32(); break;
                }
            }
            return sp;
        }

        #endregion

        #region PlayerOperation / AttackOperation

        private static void SerializePlayerOperation(CodedOutputStream cos, PlayerOperation op)
        {
            if (op.PlayerId != 0) cos.WriteInt32Tag(1).WriteInt32(op.PlayerId);
            if (op.MoveX != 0) cos.WriteFloatTag(2).WriteFloat(op.MoveX);
            if (op.MoveY != 0) cos.WriteFloatTag(3).WriteFloat(op.MoveY);
            if (op.AimYaw != 0) cos.WriteFloatTag(4).WriteFloat(op.AimYaw);
            if (op.AimPitch != 0) cos.WriteFloatTag(5).WriteFloat(op.AimPitch);
            if (op.Fire) cos.WriteBoolTag(6).WriteBool(op.Fire);
            if (op.Jump) cos.WriteBoolTag(7).WriteBool(op.Jump);
            if (op.AttackId != 0) cos.WriteInt32Tag(8).WriteInt32(op.AttackId);
            if (op.ClientFrameId != 0) cos.WriteInt32Tag(9).WriteInt32(op.ClientFrameId);
            if (op.AttackOperations != null)
            {
                foreach (var atk in op.AttackOperations)
                    SerializeSubMessage(cos, 10, subCos => SerializeAttackOperation(subCos, atk));
            }
            if (op.Run) cos.WriteBoolTag(11).WriteBool(op.Run);
            if (op.Aim) cos.WriteBoolTag(12).WriteBool(op.Aim);
            if (op.Reload) cos.WriteBoolTag(13).WriteBool(op.Reload);
            // 客户端预测位置（字段 15-20）
            if (op.PosX != 0) cos.WriteFloatTag(15).WriteFloat(op.PosX);
            if (op.PosY != 0) cos.WriteFloatTag(16).WriteFloat(op.PosY);
            if (op.PosZ != 0) cos.WriteFloatTag(17).WriteFloat(op.PosZ);
            if (op.VelX != 0) cos.WriteFloatTag(18).WriteFloat(op.VelX);
            if (op.VelZ != 0) cos.WriteFloatTag(19).WriteFloat(op.VelZ);
            if (op.IsGrounded) cos.WriteBoolTag(20).WriteBool(op.IsGrounded);
            if (op.AbilityEvents != null)
            {
                foreach (var evt in op.AbilityEvents)
                    SerializeSubMessage(cos, 14, subCos => SerializeAbilityEvent(subCos, evt));
            }
            cos.Flush();
        }

        private static PlayerOperation DeserializePlayerOperation(CodedInputStream cis)
        {
            var op = new PlayerOperation();
            while (cis.ReadTag(out uint tag))
            {
                switch (tag >> 3)
                {
                    case 1: op.PlayerId = cis.ReadInt32(); break;
                    case 2: op.MoveX = cis.ReadFloat(); break;
                    case 3: op.MoveY = cis.ReadFloat(); break;
                    case 4: op.AimYaw = cis.ReadFloat(); break;
                    case 5: op.AimPitch = cis.ReadFloat(); break;
                    case 6: op.Fire = cis.ReadBool(); break;
                    case 7: op.Jump = cis.ReadBool(); break;
                    case 8: op.AttackId = cis.ReadInt32(); break;
                    case 9: op.ClientFrameId = cis.ReadInt32(); break;
                    case 10:
                    {
                        var subCis = cis.ReadMessage();
                        op.AttackOperations.Add(DeserializeAttackOperation(subCis));
                        break;
                    }
                    case 11: op.Run = cis.ReadBool(); break;
                    case 12: op.Aim = cis.ReadBool(); break;
                    case 13: op.Reload = cis.ReadBool(); break;
                    case 24: op.Crouch = cis.ReadBool(); break;
                    case 14:
                    {
                        var subCis = cis.ReadMessage();
                        op.AbilityEvents.Add(DeserializeAbilityEvent(subCis));
                        break;
                    }
                    case 15: op.PosX = cis.ReadFloat(); break;
                    case 16: op.PosY = cis.ReadFloat(); break;
                    case 17: op.PosZ = cis.ReadFloat(); break;
                    case 18: op.VelX = cis.ReadFloat(); break;
                    case 19: op.VelZ = cis.ReadFloat(); break;
                    case 20: op.IsGrounded = cis.ReadBool(); break;
                }
            }
            return op;
        }

        private static void SerializeAttackOperation(CodedOutputStream cos, AttackOperation atk)
        {
            if (atk.AttackId != 0) cos.WriteInt32Tag(1).WriteInt32(atk.AttackId);
            if (atk.TowardX != 0) cos.WriteFloatTag(2).WriteFloat(atk.TowardX);
            if (atk.TowardY != 0) cos.WriteFloatTag(3).WriteFloat(atk.TowardY);
            if (atk.AimPitch != 0) cos.WriteFloatTag(4).WriteFloat(atk.AimPitch);
            if (atk.ClientFrameId != 0) cos.WriteInt32Tag(5).WriteInt32(atk.ClientFrameId);
            if (atk.SpawnPos.x != 0 || atk.SpawnPos.y != 0 || atk.SpawnPos.z != 0)
                SerializeSubMessage(cos, 6, subCos => SerializeVec3(subCos, atk.SpawnPos));
            cos.Flush();
        }

        private static AttackOperation DeserializeAttackOperation(CodedInputStream cis)
        {
            var atk = new AttackOperation();
            while (cis.ReadTag(out uint tag))
            {
                switch (tag >> 3)
                {
                    case 1: atk.AttackId = cis.ReadInt32(); break;
                    case 2: atk.TowardX = cis.ReadFloat(); break;
                    case 3: atk.TowardY = cis.ReadFloat(); break;
                    case 4: atk.AimPitch = cis.ReadFloat(); break;
                    case 5: atk.ClientFrameId = cis.ReadInt32(); break;
                    case 6:
                    {
                        var subCis = cis.ReadMessage();
                        atk.SpawnPos = DeserializeVec3(subCis);
                        break;
                    }
                }
            }
            return atk;
        }

        #endregion

        #region AllPlayerOperation

        private static void SerializeAllPlayerOperation(CodedOutputStream cos, AllPlayerOperation apo)
        {
            if (apo.FrameId != 0) cos.WriteInt32Tag(1).WriteInt32(apo.FrameId);
            if (apo.Operations != null)
            {
                foreach (var op in apo.Operations)
                    SerializeSubMessage(cos, 2, subCos => SerializePlayerOperation(subCos, op));
            }
            if (apo.PlayerStates != null)
            {
                foreach (var ps in apo.PlayerStates)
                    SerializeSubMessage(cos, 3, subCos => SerializePlayerStateMsg(subCos, ps));
            }
            if (apo.HitEvents != null)
            {
                foreach (var he in apo.HitEvents)
                    SerializeSubMessage(cos, 4, subCos => SerializeHitEventMsg(subCos, he));
            }
            if (apo.AbilityEvents != null)
            {
                foreach (var evt in apo.AbilityEvents)
                    SerializeSubMessage(cos, 5, subCos => SerializeAbilityEvent(subCos, evt));
            }
            cos.Flush();
        }

        private static AllPlayerOperation DeserializeAllPlayerOperation(CodedInputStream cis)
        {
            var apo = new AllPlayerOperation();
            while (cis.ReadTag(out uint tag))
            {
                switch (tag >> 3)
                {
                    case 1: apo.FrameId = cis.ReadInt32(); break;
                    case 2:
                    {
                        var subCis = cis.ReadMessage();
                        apo.Operations.Add(DeserializePlayerOperation(subCis));
                        break;
                    }
                    case 3:
                    {
                        var subCis = cis.ReadMessage();
                        apo.PlayerStates.Add(DeserializePlayerStateMsg(subCis));
                        break;
                    }
                    case 4:
                    {
                        var subCis = cis.ReadMessage();
                        apo.HitEvents.Add(DeserializeHitEventMsg(subCis));
                        break;
                    }
                    case 5:
                    {
                        var subCis = cis.ReadMessage();
                        apo.AbilityEvents.Add(DeserializeAbilityEvent(subCis));
                        break;
                    }
                }
            }
            return apo;
        }

        #endregion

        #region PlayerStateMsg

        private static void SerializePlayerStateMsg(CodedOutputStream cos, PlayerStateMsg ps)
        {
            if (ps.PlayerId != 0) cos.WriteInt32Tag(1).WriteInt32(ps.PlayerId);
            if (ps.Position.x != 0 || ps.Position.y != 0 || ps.Position.z != 0)
                SerializeSubMessage(cos, 2, subCos => SerializeVec3(subCos, ps.Position));
            if (ps.Hp != 0) cos.WriteInt32Tag(3).WriteInt32(ps.Hp);
            if (ps.IsDead) cos.WriteBoolTag(4).WriteBool(ps.IsDead);
            if (ps.Velocity.x != 0 || ps.Velocity.y != 0 || ps.Velocity.z != 0)
                SerializeSubMessage(cos, 5, subCos => SerializeVec3(subCos, ps.Velocity));
            if (ps.IsGrounded) cos.WriteBoolTag(6).WriteBool(ps.IsGrounded);
            if (ps.StateEnum != 0) cos.WriteInt32Tag(7).WriteInt32(ps.StateEnum);
            if (ps.FireCooldown != 0) cos.WriteFloatTag(8).WriteFloat(ps.FireCooldown);
            if (ps.RotationY != 0) cos.WriteFloatTag(9).WriteFloat(ps.RotationY);
            if (ps.IsRunning) cos.WriteBoolTag(10).WriteBool(ps.IsRunning);
            if (ps.IsAiming) cos.WriteBoolTag(19).WriteBool(ps.IsAiming);
            if (ps.IsCrouching) cos.WriteBoolTag(25).WriteBool(ps.IsCrouching);
            if (ps.CurrentAmmo != 0) cos.WriteInt32Tag(11).WriteInt32(ps.CurrentAmmo);
            if (ps.IsReloading) cos.WriteBoolTag(12).WriteBool(ps.IsReloading);
            if (ps.VerticalVelocity != 0) cos.WriteFloatTag(15).WriteFloat(ps.VerticalVelocity);
            if (ps.TagBitmask != 0) cos.WriteInt64Tag(13).WriteInt64(ps.TagBitmask);
            if (ps.ActiveAbilities != null)
            {
                foreach (var ab in ps.ActiveAbilities)
                    SerializeSubMessage(cos, 14, subCos => SerializeAbilityInstance(subCos, ab));
            }
            if (ps.MaxHp != 0) cos.WriteInt32Tag(16).WriteInt32(ps.MaxHp);
            if (ps.Kills != 0) cos.WriteInt32Tag(17).WriteInt32(ps.Kills);
            if (ps.Deaths != 0) cos.WriteInt32Tag(18).WriteInt32(ps.Deaths);
            cos.Flush();
        }

        private static PlayerStateMsg DeserializePlayerStateMsg(CodedInputStream cis)
        {
            var ps = new PlayerStateMsg();
            while (cis.ReadTag(out uint tag))
            {
                switch (tag >> 3)
                {
                    case 1: ps.PlayerId = cis.ReadInt32(); break;
                    case 2: { var subCis = cis.ReadMessage(); ps.Position = DeserializeVec3(subCis); break; }
                    case 3: ps.Hp = cis.ReadInt32(); break;
                    case 4: ps.IsDead = cis.ReadBool(); break;
                    case 5: { var subCis = cis.ReadMessage(); ps.Velocity = DeserializeVec3(subCis); break; }
                    case 6: ps.IsGrounded = cis.ReadBool(); break;
                    case 7: ps.StateEnum = cis.ReadInt32(); break;
                    case 8: ps.FireCooldown = cis.ReadFloat(); break;
                    case 9: ps.RotationY = cis.ReadFloat(); break;
                    case 10: ps.IsRunning = cis.ReadBool(); break;
                    case 19: ps.IsAiming = cis.ReadBool(); break;
                    case 25: ps.IsCrouching = cis.ReadBool(); break;
                    case 11: ps.CurrentAmmo = cis.ReadInt32(); break;
                    case 12: ps.IsReloading = cis.ReadBool(); break;
                    case 13: ps.TagBitmask = cis.ReadInt64(); break;
                    case 14:
                    {
                        var subCis = cis.ReadMessage();
                        if (ps.ActiveAbilities == null) ps.ActiveAbilities = new List<AbilityInstanceData>();
                        ps.ActiveAbilities.Add(DeserializeAbilityInstance(subCis));
                        break;
                    }
                    case 15: ps.VerticalVelocity = cis.ReadFloat(); break;
                    case 16: ps.MaxHp = cis.ReadInt32(); break;
                    case 17: ps.Kills = cis.ReadInt32(); break;
                    case 18: ps.Deaths = cis.ReadInt32(); break;
                }
            }
            return ps;
        }

        #endregion

        #region HitEventMsg / BattlePlayerInfo

        private static void SerializeHitEventMsg(CodedOutputStream cos, HitEventMsg he)
        {
            if (he.AttackId != 0) cos.WriteInt32Tag(1).WriteInt32(he.AttackId);
            if (he.AttackerId != 0) cos.WriteInt32Tag(2).WriteInt32(he.AttackerId);
            if (he.VictimId != 0) cos.WriteInt32Tag(3).WriteInt32(he.VictimId);
            if (he.Damage != 0) cos.WriteInt32Tag(4).WriteInt32(he.Damage);
            if (he.IsKill) cos.WriteBoolTag(5).WriteBool(he.IsKill);
            if (he.HitPoint.x != 0 || he.HitPoint.y != 0 || he.HitPoint.z != 0)
                SerializeSubMessage(cos, 6, subCos => SerializeVec3(subCos, he.HitPoint));
            if (he.HitFrameId != 0) cos.WriteInt32Tag(7).WriteInt32(he.HitFrameId);
            if (he.BodyPart != 0) cos.WriteInt32Tag(8).WriteInt32(he.BodyPart);
            cos.Flush();
        }

        private static HitEventMsg DeserializeHitEventMsg(CodedInputStream cis)
        {
            var he = new HitEventMsg();
            while (cis.ReadTag(out uint tag))
            {
                switch (tag >> 3)
                {
                    case 1: he.AttackId = cis.ReadInt32(); break;
                    case 2: he.AttackerId = cis.ReadInt32(); break;
                    case 3: he.VictimId = cis.ReadInt32(); break;
                    case 4: he.Damage = cis.ReadInt32(); break;
                    case 5: he.IsKill = cis.ReadBool(); break;
                    case 6: { var subCis = cis.ReadMessage(); he.HitPoint = DeserializeVec3(subCis); break; }
                    case 7: he.HitFrameId = cis.ReadInt32(); break;
                    case 8: he.BodyPart = cis.ReadInt32(); break;
                }
            }
            return he;
        }

        private static void SerializeBattlePlayerInfo(CodedOutputStream cos, BattlePlayerInfo bpi)
        {
            if (bpi.PlayerId != 0) cos.WriteInt32Tag(1).WriteInt32(bpi.PlayerId);
            if (bpi.TeamId != 0) cos.WriteInt32Tag(2).WriteInt32(bpi.TeamId);
            if (bpi.UserId != 0) cos.WriteInt32Tag(3).WriteInt32(bpi.UserId);
            if (!string.IsNullOrEmpty(bpi.PlayerName)) cos.WriteStringTag(4).WriteString(bpi.PlayerName);
            if (bpi.SpawnPosition.x != 0 || bpi.SpawnPosition.y != 0 || bpi.SpawnPosition.z != 0)
                SerializeSubMessage(cos, 5, subCos => SerializeVec3(subCos, bpi.SpawnPosition));
            if (bpi.HeroId != 0) cos.WriteInt32Tag(6).WriteInt32(bpi.HeroId);
            cos.Flush();
        }

        private static BattlePlayerInfo DeserializeBattlePlayerInfo(CodedInputStream cis)
        {
            var bpi = new BattlePlayerInfo();
            while (cis.ReadTag(out uint tag))
            {
                switch (tag >> 3)
                {
                    case 1: bpi.PlayerId = cis.ReadInt32(); break;
                    case 2: bpi.TeamId = cis.ReadInt32(); break;
                    case 3: bpi.UserId = cis.ReadInt32(); break;
                    case 4: bpi.PlayerName = cis.ReadString(); break;
                    case 5: { var subCis = cis.ReadMessage(); bpi.SpawnPosition = DeserializeVec3(subCis); break; }
                    case 6: bpi.HeroId = cis.ReadInt32(); break;
                }
            }
            return bpi;
        }

        #endregion

        #region Gameplay Messages (Path A)

        public static byte[] SerializeGameMessage(GameMessage msg)
        {
            using var ms = new MemoryStream();
            using var cos = new CodedOutputStream(ms);

            cos.WriteInt32Tag(1).WriteEnum((int)msg.MsgType);

            switch (msg.MsgType)
            {
                case GameMessageType.InputMessage:
                    if (msg.InputBatch != null)
                        SerializeSubMessage(cos, 10, subCos => SerializeInputBatch(subCos, msg.InputBatch));
                    break;
                case GameMessageType.WorldStateMessage:
                    if (msg.WorldState != null)
                        SerializeSubMessage(cos, 11, subCos => SerializeWorldState(subCos, msg.WorldState));
                    break;
                case GameMessageType.DamageEvent:
                    if (msg.DamageEvent != null)
                        SerializeSubMessage(cos, 12, subCos => SerializeDamageEvent(subCos, msg.DamageEvent));
                    break;
                case GameMessageType.ConnectionRequest:
                    if (msg.ConnectionRequest != null)
                        SerializeSubMessage(cos, 13, subCos => SerializeConnectionRequest(subCos, msg.ConnectionRequest));
                    break;
                case GameMessageType.ConnectionAccepted:
                    if (msg.ConnectionAccepted != null)
                        SerializeSubMessage(cos, 14, subCos => SerializeConnectionAccepted(subCos, msg.ConnectionAccepted));
                    break;
                case GameMessageType.PlayerJoined:
                    if (msg.PlayerJoined != null)
                        SerializeSubMessage(cos, 15, subCos => SerializePlayerJoined(subCos, msg.PlayerJoined));
                    break;
                case GameMessageType.PlayerLeft:
                    if (msg.PlayerLeft != null)
                        SerializeSubMessage(cos, 16, subCos => SerializePlayerLeft(subCos, msg.PlayerLeft));
                    break;
                case GameMessageType.Disconnect:
                    if (msg.Disconnect != null)
                        SerializeSubMessage(cos, 17, subCos => SerializeDisconnect(subCos, msg.Disconnect));
                    break;
                case GameMessageType.Heartbeat:
                    if (msg.Heartbeat != null)
                        SerializeSubMessage(cos, 18, subCos => SerializeHeartbeat(subCos, msg.Heartbeat));
                    break;
                case GameMessageType.AbilityEvent:
                    SerializeSubMessage(cos, 19, subCos => SerializeAbilityEvent(subCos, msg.AbilityEvent));
                    break;
                case GameMessageType.DeltaState:
                case GameMessageType.RpcCall:
                    if (msg.BinaryPayload != null && msg.BinaryPayload.Length > 0)
                        cos.WriteBytesTag(20).WriteBytes(Google.Protobuf.ByteString.CopyFrom(msg.BinaryPayload));
                    break;
            }

            cos.Flush();
            return ms.ToArray();
        }

        public static GameMessage DeserializeGameMessage(byte[] data)
        {
            var msg = new GameMessage();
            var cis = new CodedInputStream(data);

            while (cis.ReadTag(out uint tag))
            {
                int field = (int)(tag >> 3);
                switch (field)
                {
                    case 1: msg.MsgType = (GameMessageType)cis.ReadEnum(); break;
                    case 10:
                    {
                        var subCis = cis.ReadMessage();
                        msg.InputBatch = DeserializeInputBatch(subCis);
                        break;
                    }
                    case 11:
                    {
                        var subCis = cis.ReadMessage();
                        msg.WorldState = DeserializeWorldState(subCis);
                        break;
                    }
                    case 12:
                    {
                        var subCis = cis.ReadMessage();
                        msg.DamageEvent = DeserializeDamageEvent(subCis);
                        break;
                    }
                    case 13:
                    {
                        var subCis = cis.ReadMessage();
                        msg.ConnectionRequest = DeserializeConnectionRequest(subCis);
                        break;
                    }
                    case 14:
                    {
                        var subCis = cis.ReadMessage();
                        msg.ConnectionAccepted = DeserializeConnectionAccepted(subCis);
                        break;
                    }
                    case 15:
                    {
                        var subCis = cis.ReadMessage();
                        msg.PlayerJoined = DeserializePlayerJoined(subCis);
                        break;
                    }
                    case 16:
                    {
                        var subCis = cis.ReadMessage();
                        msg.PlayerLeft = DeserializePlayerLeft(subCis);
                        break;
                    }
                    case 17:
                    {
                        var subCis = cis.ReadMessage();
                        msg.Disconnect = DeserializeDisconnect(subCis);
                        break;
                    }
                    case 18:
                    {
                        var subCis = cis.ReadMessage();
                        msg.Heartbeat = DeserializeHeartbeat(subCis);
                        break;
                    }
                    case 19:
                    {
                        var subCis = cis.ReadMessage();
                        msg.AbilityEvent = DeserializeAbilityEvent(subCis);
                        break;
                    }
                    case 20:
                    {
                        msg.BinaryPayload = cis.ReadBytes().ToByteArray();
                        break;
                    }
                }
            }
            return msg;
        }

        #region Gameplay sub-messages

        private static void SerializeInputBatch(CodedOutputStream cos, InputBatchMsg batch)
        {
            if (batch.Frames != null)
            {
                foreach (var f in batch.Frames)
                    SerializeSubMessage(cos, 2, subCos => SerializeInputFrame(subCos, f));
            }
            cos.Flush();
        }

        private static InputBatchMsg DeserializeInputBatch(CodedInputStream cis)
        {
            var batch = new InputBatchMsg { Frames = new List<InputFrameMsg>() };
            while (cis.ReadTag(out uint tag))
            {
                if ((tag >> 3) == 2)
                {
                    var subCis = cis.ReadMessage();
                    batch.Frames.Add(DeserializeInputFrame(subCis));
                }
            }
            batch.Count = batch.Frames.Count;
            return batch;
        }

        private static void SerializeInputFrame(CodedOutputStream cos, InputFrameMsg f)
        {
            if (f.Tick != 0) cos.WriteInt32Tag(1).WriteInt32(f.Tick);
            if (f.Movement.x != 0 || f.Movement.y != 0)
                SerializeSubMessage(cos, 2, subCos => SerializeVec2(subCos, f.Movement));
            if (f.Flags != 0) cos.WriteUInt32Tag(3).WriteUInt32(f.Flags);
            if (f.AimYaw != 0) cos.WriteFloatTag(4).WriteFloat(f.AimYaw);
            if (f.AimPitch != 0) cos.WriteFloatTag(5).WriteFloat(f.AimPitch);
            cos.Flush();
        }

        private static InputFrameMsg DeserializeInputFrame(CodedInputStream cis)
        {
            var f = new InputFrameMsg();
            while (cis.ReadTag(out uint tag))
            {
                switch (tag >> 3)
                {
                    case 1: f.Tick = cis.ReadInt32(); break;
                    case 2: { var subCis = cis.ReadMessage(); f.Movement = DeserializeVec2(subCis); break; }
                    case 3: f.Flags = cis.ReadUInt32(); break;
                    case 4: f.AimYaw = cis.ReadFloat(); break;
                    case 5: f.AimPitch = cis.ReadFloat(); break;
                }
            }
            return f;
        }

        private static void SerializeWorldState(CodedOutputStream cos, WorldStateMsg ws)
        {
            if (ws.ServerTick != 0) cos.WriteInt32Tag(1).WriteInt32(ws.ServerTick);
            if (ws.Players != null)
            {
                foreach (var p in ws.Players)
                    SerializeSubMessage(cos, 3, subCos => SerializePlayerSnap(subCos, p));
            }
            if (ws.LastProcessedInputTicks != null && ws.LastProcessedInputTicks.Length > 0)
            {
                foreach (int t in ws.LastProcessedInputTicks)
                {
                    cos.WriteInt32Tag(4);
                    cos.WriteInt32(t);
                }
            }
            if (ws.PlayerCount != 0) cos.WriteInt32Tag(2).WriteInt32(ws.PlayerCount);
            cos.Flush();
        }

        private static WorldStateMsg DeserializeWorldState(CodedInputStream cis)
        {
            var ws = new WorldStateMsg { Players = new List<PlayerSnapMsg>() };
            var ticks = new List<int>();
            while (cis.ReadTag(out uint tag))
            {
                switch (tag >> 3)
                {
                    case 1: ws.ServerTick = cis.ReadInt32(); break;
                    case 2: ws.PlayerCount = cis.ReadInt32(); break;
                    case 3:
                    {
                        var subCis = cis.ReadMessage();
                        ws.Players.Add(DeserializePlayerSnap(subCis));
                        break;
                    }
                    case 4: ticks.Add(cis.ReadInt32()); break;
                }
            }
            ws.LastProcessedInputTicks = ticks.ToArray();
            return ws;
        }

        private static void SerializePlayerSnap(CodedOutputStream cos, PlayerSnapMsg s)
        {
            if (s.Tick != 0) cos.WriteInt32Tag(1).WriteInt32(s.Tick);
            if (s.Position.x != 0 || s.Position.y != 0 || s.Position.z != 0)
                SerializeSubMessage(cos, 2, subCos => SerializeVec3(subCos, s.Position));
            if (s.Rotation.x != 0 || s.Rotation.y != 0 || s.Rotation.z != 0 || s.Rotation.w != 0)
                SerializeSubMessage(cos, 3, subCos => SerializeQuat(subCos, s.Rotation));
            if (s.Velocity.x != 0 || s.Velocity.y != 0 || s.Velocity.z != 0)
                SerializeSubMessage(cos, 4, subCos => SerializeVec3(subCos, s.Velocity));
            if (s.VerticalVelocity != 0) cos.WriteFloatTag(5).WriteFloat(s.VerticalVelocity);
            if (s.IsGrounded) cos.WriteBoolTag(6).WriteBool(s.IsGrounded);
            if (s.State != 0) cos.WriteUInt32Tag(7).WriteUInt32(s.State);
            if (s.FireCooldown != 0) cos.WriteFloatTag(8).WriteFloat(s.FireCooldown);
            if (s.Health != 0) cos.WriteUInt32Tag(9).WriteUInt32(s.Health);
            if (s.CurrentAmmo != 0) cos.WriteInt32Tag(10).WriteInt32(s.CurrentAmmo);
            if (s.IsReloading) cos.WriteBoolTag(11).WriteBool(s.IsReloading);
            if (s.ReloadTimer != 0) cos.WriteFloatTag(12).WriteFloat(s.ReloadTimer);
            if (s.TagBitmask != 0) cos.WriteInt64Tag(13).WriteInt64(s.TagBitmask);
            if (s.ActiveAbilities != null && s.ActiveAbilities.Count > 0)
            {
                foreach (var ab in s.ActiveAbilities)
                    SerializeSubMessage(cos, 15, subCos => SerializeAbilityInstance(subCos, ab));
            }
            if (s.ActiveAbilityCount != 0) cos.WriteUInt32Tag(14).WriteUInt32(s.ActiveAbilityCount);
            cos.Flush();
        }

        private static PlayerSnapMsg DeserializePlayerSnap(CodedInputStream cis)
        {
            var s = new PlayerSnapMsg();
            while (cis.ReadTag(out uint tag))
            {
                switch (tag >> 3)
                {
                    case 1: s.Tick = cis.ReadInt32(); break;
                    case 2: { var subCis = cis.ReadMessage(); s.Position = DeserializeVec3(subCis); break; }
                    case 3: { var subCis = cis.ReadMessage(); s.Rotation = DeserializeQuat(subCis); break; }
                    case 4: { var subCis = cis.ReadMessage(); s.Velocity = DeserializeVec3(subCis); break; }
                    case 5: s.VerticalVelocity = cis.ReadFloat(); break;
                    case 6: s.IsGrounded = cis.ReadBool(); break;
                    case 7: s.State = cis.ReadUInt32(); break;
                    case 8: s.FireCooldown = cis.ReadFloat(); break;
                    case 9: s.Health = cis.ReadUInt32(); break;
                    case 10: s.CurrentAmmo = cis.ReadInt32(); break;
                    case 11: s.IsReloading = cis.ReadBool(); break;
                    case 12: s.ReloadTimer = cis.ReadFloat(); break;
                    case 13: s.TagBitmask = cis.ReadInt64(); break;
                    case 14: s.ActiveAbilityCount = (byte)cis.ReadUInt32(); break;
                    case 15:
                    {
                        var subCis = cis.ReadMessage();
                        s.ActiveAbilities.Add(DeserializeAbilityInstance(subCis));
                        break;
                    }
                }
            }
            return s;
        }

        private static void SerializeDamageEvent(CodedOutputStream cos, DamageEventMsg de)
        {
            if (de.TargetId != 0) cos.WriteInt32Tag(1).WriteInt32(de.TargetId);
            if (de.ShooterId != 0) cos.WriteInt32Tag(2).WriteInt32(de.ShooterId);
            if (de.Damage != 0) cos.WriteInt32Tag(3).WriteInt32(de.Damage);
            if (de.NewHealth != 0) cos.WriteInt32Tag(4).WriteInt32(de.NewHealth);
            if (de.HitPoint.x != 0 || de.HitPoint.y != 0 || de.HitPoint.z != 0)
                SerializeSubMessage(cos, 5, subCos => SerializeVec3(subCos, de.HitPoint));
            cos.Flush();
        }

        private static DamageEventMsg DeserializeDamageEvent(CodedInputStream cis)
        {
            var de = new DamageEventMsg();
            while (cis.ReadTag(out uint tag))
            {
                switch (tag >> 3)
                {
                    case 1: de.TargetId = (byte)cis.ReadInt32(); break;
                    case 2: de.ShooterId = (byte)cis.ReadInt32(); break;
                    case 3: de.Damage = (byte)cis.ReadInt32(); break;
                    case 4: de.NewHealth = (byte)cis.ReadInt32(); break;
                    case 5: { var subCis = cis.ReadMessage(); de.HitPoint = DeserializeVec3(subCis); break; }
                }
            }
            return de;
        }

        private static void SerializeConnectionRequest(CodedOutputStream cos, ConnectionRequestMsg cr)
        {
            if (cr.ProtocolVersion != 0) cos.WriteUInt32Tag(1).WriteUInt32(cr.ProtocolVersion);
            cos.Flush();
        }

        private static ConnectionRequestMsg DeserializeConnectionRequest(CodedInputStream cis)
        {
            var cr = new ConnectionRequestMsg();
            while (cis.ReadTag(out uint tag))
            {
                if ((tag >> 3) == 1) cr.ProtocolVersion = cis.ReadUInt32();
            }
            return cr;
        }

        private static void SerializeConnectionAccepted(CodedOutputStream cos, ConnectionAcceptedMsg ca)
        {
            if (ca.PlayerId != 0) cos.WriteInt32Tag(1).WriteInt32((int)ca.PlayerId);
            if (ca.TickRate != 0) cos.WriteInt32Tag(2).WriteInt32(ca.TickRate);
            if (ca.ServerTick != 0) cos.WriteInt32Tag(3).WriteInt32(ca.ServerTick);
            cos.Flush();
        }

        private static ConnectionAcceptedMsg DeserializeConnectionAccepted(CodedInputStream cis)
        {
            var ca = new ConnectionAcceptedMsg();
            while (cis.ReadTag(out uint tag))
            {
                switch (tag >> 3)
                {
                    case 1: ca.PlayerId = (byte)cis.ReadInt32(); break;
                    case 2: ca.TickRate = cis.ReadInt32(); break;
                    case 3: ca.ServerTick = cis.ReadInt32(); break;
                }
            }
            return ca;
        }

        private static void SerializePlayerJoined(CodedOutputStream cos, PlayerJoinedMsg pj)
        {
            if (pj.PlayerId != 0) cos.WriteInt32Tag(1).WriteInt32((int)pj.PlayerId);
            cos.Flush();
        }

        private static PlayerJoinedMsg DeserializePlayerJoined(CodedInputStream cis)
        {
            var pj = new PlayerJoinedMsg();
            while (cis.ReadTag(out uint tag))
            {
                if ((tag >> 3) == 1) pj.PlayerId = (byte)cis.ReadInt32();
            }
            return pj;
        }

        private static void SerializePlayerLeft(CodedOutputStream cos, PlayerLeftMsg pl)
        {
            if (pl.PlayerId != 0) cos.WriteInt32Tag(1).WriteInt32((int)pl.PlayerId);
            cos.Flush();
        }

        private static PlayerLeftMsg DeserializePlayerLeft(CodedInputStream cis)
        {
            var pl = new PlayerLeftMsg();
            while (cis.ReadTag(out uint tag))
            {
                if ((tag >> 3) == 1) pl.PlayerId = (byte)cis.ReadInt32();
            }
            return pl;
        }

        private static void SerializeDisconnect(CodedOutputStream cos, DisconnectMsg d)
        {
            if (d.Reason != 0) cos.WriteUInt32Tag(1).WriteUInt32(d.Reason);
            cos.Flush();
        }

        private static DisconnectMsg DeserializeDisconnect(CodedInputStream cis)
        {
            var d = new DisconnectMsg();
            while (cis.ReadTag(out uint tag))
            {
                if ((tag >> 3) == 1) d.Reason = cis.ReadUInt32();
            }
            return d;
        }

        private static void SerializeHeartbeat(CodedOutputStream cos, HeartbeatMsg hb)
        {
            if (hb.Timestamp != 0) cos.WriteInt32Tag(1).WriteInt32(hb.Timestamp);
            cos.Flush();
        }

        private static HeartbeatMsg DeserializeHeartbeat(CodedInputStream cis)
        {
            var hb = new HeartbeatMsg();
            while (cis.ReadTag(out uint tag))
            {
                if ((tag >> 3) == 1) hb.Timestamp = cis.ReadInt32();
            }
            return hb;
        }

        private static void SerializeAbilityEvent(CodedOutputStream cos, AbilityEventData evt)
        {
            if (evt.PlayerId != 0) cos.WriteInt32Tag(1).WriteInt32((int)evt.PlayerId);
            if (evt.InstanceId != 0) cos.WriteUInt32Tag(2).WriteUInt32(evt.InstanceId);
            if (evt.AssetId != 0) cos.WriteInt32Tag(3).WriteInt32((int)evt.AssetId);
            if (evt.EventType != 0) cos.WriteUInt32Tag(4).WriteUInt32((uint)evt.EventType);
            cos.Flush();
        }

        private static AbilityEventData DeserializeAbilityEvent(CodedInputStream cis)
        {
            var evt = new AbilityEventData();
            while (cis.ReadTag(out uint tag))
            {
                switch (tag >> 3)
                {
                    case 1: evt.PlayerId = (byte)cis.ReadInt32(); break;
                    case 2: evt.InstanceId = (ushort)cis.ReadUInt32(); break;
                    case 3: evt.AssetId = (byte)cis.ReadInt32(); break;
                    case 4: evt.EventType = (AbilityEventType)cis.ReadUInt32(); break;
                }
            }
            return evt;
        }

        private static void SerializeAbilityInstance(CodedOutputStream cos, AbilityInstanceData ab)
        {
            if (ab.InstanceId != 0) cos.WriteUInt32Tag(1).WriteUInt32(ab.InstanceId);
            if (ab.AssetId != 0) cos.WriteInt32Tag(2).WriteInt32((int)ab.AssetId);
            if (ab.State != 0) cos.WriteUInt32Tag(3).WriteUInt32((uint)ab.State);
            if (ab.CooldownRemaining != 0) cos.WriteFloatTag(4).WriteFloat(ab.CooldownRemaining);
            if (ab.DurationRemaining != 0) cos.WriteFloatTag(5).WriteFloat(ab.DurationRemaining);
            if (ab.AppliedTagsMask != 0) cos.WriteInt64Tag(6).WriteInt64(ab.AppliedTagsMask);
            cos.Flush();
        }

        private static AbilityInstanceData DeserializeAbilityInstance(CodedInputStream cis)
        {
            var ab = new AbilityInstanceData();
            while (cis.ReadTag(out uint tag))
            {
                switch (tag >> 3)
                {
                    case 1: ab.InstanceId = (ushort)cis.ReadUInt32(); break;
                    case 2: ab.AssetId = (byte)cis.ReadInt32(); break;
                    case 3: ab.State = (AbilityState)cis.ReadUInt32(); break;
                    case 4: ab.CooldownRemaining = cis.ReadFloat(); break;
                    case 5: ab.DurationRemaining = cis.ReadFloat(); break;
                    case 6: ab.AppliedTagsMask = cis.ReadInt64(); break;
                }
            }
            return ab;
        }

        #endregion

        #endregion

        #region Math Types

        private static void SerializeVec2(CodedOutputStream cos, Vec2 v)
        {
            if (v.x != 0) cos.WriteFloatTag(1).WriteFloat(v.x);
            if (v.y != 0) cos.WriteFloatTag(2).WriteFloat(v.y);
            cos.Flush();
        }

        private static Vec2 DeserializeVec2(CodedInputStream cis)
        {
            var v = new Vec2();
            while (cis.ReadTag(out uint tag))
            {
                switch (tag >> 3)
                {
                    case 1: v.x = cis.ReadFloat(); break;
                    case 2: v.y = cis.ReadFloat(); break;
                }
            }
            return v;
        }

        private static void SerializeVec3(CodedOutputStream cos, Vec3 v)
        {
            if (v.x != 0) cos.WriteFloatTag(1).WriteFloat(v.x);
            if (v.y != 0) cos.WriteFloatTag(2).WriteFloat(v.y);
            if (v.z != 0) cos.WriteFloatTag(3).WriteFloat(v.z);
            cos.Flush();
        }

        private static Vec3 DeserializeVec3(CodedInputStream cis)
        {
            var v = new Vec3();
            while (cis.ReadTag(out uint tag))
            {
                switch (tag >> 3)
                {
                    case 1: v.x = cis.ReadFloat(); break;
                    case 2: v.y = cis.ReadFloat(); break;
                    case 3: v.z = cis.ReadFloat(); break;
                }
            }
            return v;
        }

        private static void SerializeQuat(CodedOutputStream cos, Quat q)
        {
            if (q.x != 0) cos.WriteFloatTag(1).WriteFloat(q.x);
            if (q.y != 0) cos.WriteFloatTag(2).WriteFloat(q.y);
            if (q.z != 0) cos.WriteFloatTag(3).WriteFloat(q.z);
            if (q.w != 0) cos.WriteFloatTag(4).WriteFloat(q.w);
            cos.Flush();
        }

        private static Quat DeserializeQuat(CodedInputStream cis)
        {
            var q = new Quat();
            while (cis.ReadTag(out uint tag))
            {
                switch (tag >> 3)
                {
                    case 1: q.x = cis.ReadFloat(); break;
                    case 2: q.y = cis.ReadFloat(); break;
                    case 3: q.z = cis.ReadFloat(); break;
                    case 4: q.w = cis.ReadFloat(); break;
                }
            }
            return q;
        }

        #endregion

        #region Conversion Helpers (Message ↔ Internal Types)

        /// <summary>Convert protobuf InputFrameMsg to internal InputFrame struct.</summary>
        public static InputFrame ToInputFrame(InputFrameMsg msg)
        {
            return new InputFrame
            {
                Tick = msg.Tick,
                Movement = msg.Movement,
                Jump = (msg.Flags & 1) != 0,
                Run = (msg.Flags & 2) != 0,
                Aim = (msg.Flags & 4) != 0,
                Fire = (msg.Flags & 8) != 0,
                Reload = (msg.Flags & 16) != 0,
                Ability3 = (msg.Flags & 32) != 0,
                Ability4 = (msg.Flags & 64) != 0,
                Ability1 = (msg.Flags & 128) != 0,
                Ability2 = (msg.Flags & 256) != 0,
                AimYaw = msg.AimYaw,
                AimPitch = msg.AimPitch
            };
        }

        /// <summary>Convert internal InputFrame struct to protobuf InputFrameMsg.</summary>
        public static InputFrameMsg ToInputFrameMsg(InputFrame frame)
        {
            uint flags = 0;
            if (frame.Jump) flags |= 1;
            if (frame.Run) flags |= 2;
            if (frame.Aim) flags |= 4;
            if (frame.Fire) flags |= 8;
            if (frame.Reload) flags |= 16;
            if (frame.Ability3) flags |= 32;
            if (frame.Ability4) flags |= 64;
            if (frame.Ability1) flags |= 128;
            if (frame.Ability2) flags |= 256;
            return new InputFrameMsg
            {
                Tick = frame.Tick,
                Movement = frame.Movement,
                Flags = flags,
                AimYaw = frame.AimYaw,
                AimPitch = frame.AimPitch
            };
        }

        /// <summary>Convert internal PlayerSnapshot struct to protobuf PlayerSnapMsg.</summary>
        public static PlayerSnapMsg ToPlayerSnapMsg(PlayerSnapshot snap)
        {
            var msg = new PlayerSnapMsg
            {
                Tick = snap.Tick,
                Position = snap.Position,
                Rotation = snap.Rotation,
                Velocity = snap.Velocity,
                VerticalVelocity = snap.VerticalVelocity,
                IsGrounded = snap.IsGrounded,
                State = (uint)snap.State,
                FireCooldown = snap.FireCooldown,
                Health = snap.Health,
                CurrentAmmo = snap.CurrentAmmo,
                IsReloading = snap.IsReloading,
                ReloadTimer = snap.ReloadTimer,
                TagBitmask = snap.TagBitmask,
                ActiveAbilityCount = snap.ActiveAbilityCount
            };
            if (snap.ActiveAbilities != null)
            {
                foreach (var ab in snap.ActiveAbilities)
                    msg.ActiveAbilities.Add(ab);
            }
            return msg;
        }

        /// <summary>Convert protobuf PlayerSnapMsg to internal PlayerSnapshot struct.</summary>
        public static PlayerSnapshot ToPlayerSnapshot(PlayerSnapMsg msg)
        {
            var snap = new PlayerSnapshot
            {
                Tick = msg.Tick,
                Position = msg.Position,
                Rotation = msg.Rotation,
                Velocity = msg.Velocity,
                VerticalVelocity = msg.VerticalVelocity,
                IsGrounded = msg.IsGrounded,
                State = (PlayerStateEnum)msg.State,
                FireCooldown = msg.FireCooldown,
                Health = (byte)msg.Health,
                CurrentAmmo = msg.CurrentAmmo,
                IsReloading = msg.IsReloading,
                ReloadTimer = msg.ReloadTimer,
                TagBitmask = msg.TagBitmask,
                ActiveAbilityCount = msg.ActiveAbilityCount
            };
            if (msg.ActiveAbilities != null && msg.ActiveAbilities.Count > 0)
            {
                snap.ActiveAbilities = msg.ActiveAbilities.ToArray();
            }
            return snap;
        }

        #endregion

        #region Extension Methods

        private static CodedOutputStream WriteInt32Tag(this CodedOutputStream cos, int fieldNumber)
        {
            cos.WriteTag(fieldNumber, WireFormat.WireType.Varint);
            return cos;
        }

        private static CodedOutputStream WriteUInt32Tag(this CodedOutputStream cos, int fieldNumber)
        {
            cos.WriteTag(fieldNumber, WireFormat.WireType.Varint);
            return cos;
        }

        private static CodedOutputStream WriteInt64Tag(this CodedOutputStream cos, int fieldNumber)
        {
            cos.WriteTag(fieldNumber, WireFormat.WireType.Varint);
            return cos;
        }

        private static CodedOutputStream WriteBoolTag(this CodedOutputStream cos, int fieldNumber)
        {
            cos.WriteTag(fieldNumber, WireFormat.WireType.Varint);
            return cos;
        }

        private static CodedOutputStream WriteStringTag(this CodedOutputStream cos, int fieldNumber)
        {
            cos.WriteTag(fieldNumber, WireFormat.WireType.LengthDelimited);
            return cos;
        }

        private static CodedOutputStream WriteBytesTag(this CodedOutputStream cos, int fieldNumber)
        {
            cos.WriteTag(fieldNumber, WireFormat.WireType.LengthDelimited);
            return cos;
        }

        private static CodedOutputStream WriteMessageTag(this CodedOutputStream cos, int fieldNumber)
        {
            cos.WriteTag(fieldNumber, WireFormat.WireType.LengthDelimited);
            return cos;
        }

        /// <summary>Write a length-delimited sub-message: tag + length prefix + raw bytes.</summary>
        private static void WriteSubMessage(CodedOutputStream cos, int fieldNumber, byte[] subData)
        {
            cos.WriteTag(fieldNumber, WireFormat.WireType.LengthDelimited);
            cos.WriteLength(subData.Length);
            for (int i = 0; i < subData.Length; i++)
                cos.WriteRawTag(subData[i]);
        }

        /// <summary>Serialize a sub-message to bytes, then write as length-delimited field.</summary>
        private static void SerializeSubMessage(CodedOutputStream cos, int fieldNumber, Action<CodedOutputStream> write)
        {
            using var subMs = new MemoryStream();
            var subCos = new CodedOutputStream(subMs);
            write(subCos);
            subCos.Flush();
            byte[] subData = subMs.ToArray();
            WriteSubMessage(cos, fieldNumber, subData);
        }

        private static CodedOutputStream WriteFloatTag(this CodedOutputStream cos, int fieldNumber)
        {
            cos.WriteTag(fieldNumber, WireFormat.WireType.Fixed32);
            return cos;
        }

        private static CodedOutputStream WriteFloat(this CodedOutputStream cos, float value)
        {
            cos.WriteFixed32(BitConverter.ToUInt32(BitConverter.GetBytes(value), 0));
            return cos;
        }

        private static CodedOutputStream WriteEnum(this CodedOutputStream cos, int value)
        {
            cos.WriteInt32(value);
            return cos;
        }

        private static int ReadEnum(this CodedInputStream cis)
        {
            return cis.ReadInt32();
        }

        private static float ReadFloat(this CodedInputStream cis)
        {
            uint raw = cis.ReadFixed32();
            return BitConverter.ToSingle(BitConverter.GetBytes(raw), 0);
        }

        // ReadMessage: reads a length-delimited sub-message and returns a new CodedInputStream.
        private static CodedInputStream ReadMessage(this CodedInputStream cis)
        {
            return new CodedInputStream(cis.ReadBytes().ToByteArray());
        }

        /// <summary>Read a tag and return true if more fields exist. tag = 0 means end of message.</summary>
        private static bool ReadTag(this CodedInputStream cis, out uint tag)
        {
            tag = cis.ReadTag();
            return tag != 0;
        }

        #endregion
    }

    #region Game Message POCOs (Path A - Gameplay)

    public enum GameMessageType
    {
        InputMessage = 0,
        WorldStateMessage = 1,
        DamageEvent = 2,
        ConnectionRequest = 3,
        ConnectionAccepted = 4,
        PlayerJoined = 5,
        PlayerLeft = 6,
        Disconnect = 7,
        Heartbeat = 8,
        AbilityEvent = 9,
        DeltaState = 10,    // I/P帧增量状态同步
        RpcCall = 11,       // RPC调用
    }

    public class GameMessage
    {
        public GameMessageType MsgType;

        // Oneof payload
        public InputBatchMsg InputBatch;
        public WorldStateMsg WorldState;
        public DamageEventMsg DamageEvent;
        public ConnectionRequestMsg ConnectionRequest;
        public ConnectionAcceptedMsg ConnectionAccepted;
        public PlayerJoinedMsg PlayerJoined;
        public PlayerLeftMsg PlayerLeft;
        public DisconnectMsg Disconnect;
        public HeartbeatMsg Heartbeat;
        public AbilityEventData AbilityEvent;

        // Binary payload for DeltaState and RpcCall
        // Raw bytes serialized by NetworkFrameSerializer, decoded by Network.Core layer
        public byte[] BinaryPayload;
    }

    public class InputBatchMsg
    {
        public int Count;
        public List<InputFrameMsg> Frames = new List<InputFrameMsg>();
    }

    public class InputFrameMsg
    {
        public int Tick;
        public Vec2 Movement;
        public uint Flags;
        public float AimYaw;
        public float AimPitch;
    }

    public class WorldStateMsg
    {
        public int ServerTick;
        public int PlayerCount;
        public List<PlayerSnapMsg> Players = new List<PlayerSnapMsg>();
        public int[] LastProcessedInputTicks = Array.Empty<int>();
    }

    public class PlayerSnapMsg
    {
        public int Tick;
        public Vec3 Position;
        public Quat Rotation;
        public Vec3 Velocity;
        public float VerticalVelocity;
        public bool IsGrounded;
        public uint State;
        public float FireCooldown;
        public uint Health;
        public int CurrentAmmo;
        public bool IsReloading;
        public float ReloadTimer;
        public long TagBitmask;
        public byte ActiveAbilityCount;
        public List<AbilityInstanceData> ActiveAbilities = new List<AbilityInstanceData>();
    }

    public class DamageEventMsg
    {
        public byte TargetId;
        public byte ShooterId;
        public byte Damage;
        public byte NewHealth;
        public Vec3 HitPoint;
    }

    public class ConnectionRequestMsg
    {
        public uint ProtocolVersion;
    }

    public class ConnectionAcceptedMsg
    {
        public byte PlayerId;
        public int TickRate;
        public int ServerTick;
    }

    public class PlayerJoinedMsg
    {
        public byte PlayerId;
    }

    public class PlayerLeftMsg
    {
        public byte PlayerId;
    }

    public class DisconnectMsg
    {
        public uint Reason;
    }

    public class HeartbeatMsg
    {
        public int Timestamp;
    }

    #endregion
}
