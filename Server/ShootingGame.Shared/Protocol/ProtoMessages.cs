using System;
using System.Collections.Generic;
using ShootingGame.Shared.Ability;
using ShootingGame.Shared.Math;

namespace ShootingGame.Shared.Protocol
{
    /// <summary>
    /// Proto-compatible message structures with manual binary serialization.
    /// Can be replaced with Google.Protobuf generated code when protoc is available.
    /// </summary>

    // ==================== Enum Definitions ====================

    public enum RequestCode
    {
        None = 0,
        User = 1,
        Matching = 2,
        Battle = 3,
    }

    public enum ActionCode
    {
        // User
        Login = 0,
        LoginResult = 1,

        // Matching (100-199)
        JoinQueue = 100,
        LeaveQueue = 101,
        MatchFound = 102,
        StartEnterBattle = 103,
        OnlinePlayers = 104,

        // Room (150-169)
        RoomList = 150,
        CreateRoom = 151,
        JoinRoom = 152,
        LeaveRoom = 153,
        RoomUpdate = 154,

        // Battle (200-299)
        BattleReady = 200,
        BattleStart = 201,
        BattleOperation = 202,
        BattleFrame = 203,
        HitEvent = 204,
        GameOver = 205,
        Ping = 206,
        Pong = 207,
        PlayerJoined = 208,
        PlayerLeft = 209,
        Disconnect = 210,
    }

    public enum ReturnCode
    {
        Success = 0,
        Fail = 1,
        NotFound = 2,
        AlreadyExists = 3,
        ServerFull = 4,
    }

    // ==================== Core Message Structures ====================

    public class MainPack
    {
        public RequestCode RequestCode;
        public ActionCode ActionCode;
        public ReturnCode ReturnCode;
        public long Timestamp;
        public string Str;
        public int IntVal;
        public BattleInfo BattleInfo;
        public UserInfo UserInfo;
        public RoomInfo RoomInfo;
        public List<RoomInfo> RoomInfos = new List<RoomInfo>();
        public List<BattlePlayerPack> BattlePlayerPacks = new List<BattlePlayerPack>();
    }

    // ==================== User Messages ====================

    public class UserInfo
    {
        public int UserId;
        public string Username;
        public string Password;
    }

    public class BattlePlayerPack
    {
        public int UserId;
        public int BattleId;      // battlePlayerId in battle context
        public string PlayerName;
        public int TeamId;
        public int HeroId;
    }

    /// <summary>
    /// Room info for room list display.
    /// </summary>
    public class RoomInfo
    {
        public int RoomId;
        public string RoomName;
        public string CreatorName;
        public int PlayerCount;
        public int MaxPlayers;
        public int Status; // 0=Waiting, 1=Playing
    }

    // ==================== Battle Messages ====================

    public class BattleInfo
    {
        public int OperationId;           // Frame ID / sequence number
        public int BattleId;              // Battle room ID
        public int RandSeed;              // Random seed for sync
        public int ClientAckedFrame;      // Client's last acked frame

        public PlayerOperation SelfOperation;
        public List<PlayerOperation> Operations = new List<PlayerOperation>();
        public List<AllPlayerOperation> AllPlayerOperations = new List<AllPlayerOperation>();
        public List<HitEventMsg> HitEvents = new List<HitEventMsg>();
        public List<PlayerStateMsg> PlayerStates = new List<PlayerStateMsg>();
        public List<BattlePlayerInfo> BattlePlayers = new List<BattlePlayerInfo>();
        public List<SpawnPointMsg> SpawnPoints = new List<SpawnPointMsg>();
        public byte[] CollisionData;
    }

    public class SpawnPointMsg
    {
        public Vec3 Position;
        public float Yaw;
        public int TeamId;
    }

    public class BattlePlayerInfo
    {
        public int PlayerId;
        public int TeamId;
        public int UserId;
        public string PlayerName;
        public Vec3 SpawnPosition;
        public int HeroId;
    }

    // ==================== Player Operations ====================

    public class PlayerOperation
    {
        public int PlayerId;
        public float MoveX;
        public float MoveY;
        public float AimYaw;
        public float AimPitch;
        public bool Fire;
        public bool Jump;
        public bool Run;
        public bool Aim;
        public bool Reload;
        public int AttackId;
        public int ClientFrameId;
        public List<AttackOperation> AttackOperations = new List<AttackOperation>();
        public List<AbilityEventData> AbilityEvents = new List<AbilityEventData>();
    }

    public class AttackOperation
    {
        public int AttackId;
        public float TowardX;
        public float TowardY;
        public float AimPitch;
        public int ClientFrameId;
        public Vec3 SpawnPos;
    }

    // ==================== Frame Sync ====================

    public class AllPlayerOperation
    {
        public int FrameId;
        public List<PlayerOperation> Operations = new List<PlayerOperation>();
        public List<PlayerStateMsg> PlayerStates = new List<PlayerStateMsg>();
        public List<HitEventMsg> HitEvents = new List<HitEventMsg>();
        public List<AbilityEventData> AbilityEvents = new List<AbilityEventData>();
    }

    // ==================== State Sync ====================

    public class PlayerStateMsg
    {
        public int PlayerId;
        public Vec3 Position;
        public int Hp;
        public bool IsDead;
        public Vec3 Velocity;
        public float VerticalVelocity;
        public bool IsGrounded;
        public int StateEnum;
        public float FireCooldown;
        public float RotationY;     // Y-axis rotation (yaw) in degrees
        public bool IsRunning;      // Whether player is running
        public int CurrentAmmo;
        public bool IsReloading;
        public long TagBitmask;
        public List<AbilityInstanceData> ActiveAbilities;
        public int MaxHp;
    }

    // ==================== Hit Events ====================

    public class HitEventMsg
    {
        public int AttackId;
        public int AttackerId;
        public int VictimId;
        public int Damage;
        public bool IsKill;
        public Vec3 HitPoint;
        public int HitFrameId;
    }

    // ==================== Serialization ====================

    public static class ProtoSerializer
    {
        // MainPack serialization
        public static void WriteMainPack(PacketWriter w, MainPack pack)
        {
            w.WriteByte((byte)pack.RequestCode);
            w.WriteByte((byte)pack.ActionCode);
            w.WriteByte((byte)pack.ReturnCode);
            w.WriteInt64(pack.Timestamp);
            w.WriteInt32(pack.IntVal);
            w.WriteString(pack.Str ?? "");

            if (pack.UserInfo != null)
            {
                w.WriteByte(1);
                WriteUserInfo(w, pack.UserInfo);
            }
            else
            {
                w.WriteByte(0);
            }

            if (pack.BattleInfo != null)
            {
                w.WriteByte(1);
                WriteBattleInfo(w, pack.BattleInfo);
            }
            else
            {
                w.WriteByte(0);
            }

            if (pack.RoomInfo != null)
            {
                w.WriteByte(1);
                WriteRoomInfo(w, pack.RoomInfo);
            }
            else
            {
                w.WriteByte(0);
            }

            w.WriteByte((byte)pack.RoomInfos.Count);
            foreach (var ri in pack.RoomInfos)
                WriteRoomInfo(w, ri);

            w.WriteByte((byte)pack.BattlePlayerPacks.Count);
            foreach (var bp in pack.BattlePlayerPacks)
            {
                WriteBattlePlayerPack(w, bp);
            }
        }

        public static MainPack ReadMainPack(PacketReader r)
        {
            var pack = new MainPack();
            pack.RequestCode = (RequestCode)r.ReadByte();
            pack.ActionCode = (ActionCode)r.ReadByte();
            pack.ReturnCode = (ReturnCode)r.ReadByte();
            pack.Timestamp = r.ReadInt64();
            pack.IntVal = r.ReadInt32();
            pack.Str = r.ReadString();

            if (r.ReadByte() == 1)
                pack.UserInfo = ReadUserInfo(r);

            if (r.ReadByte() == 1)
                pack.BattleInfo = ReadBattleInfo(r);

            if (r.ReadByte() == 1)
                pack.RoomInfo = ReadRoomInfo(r);

            int riCount = r.ReadByte();
            for (int i = 0; i < riCount; i++)
                pack.RoomInfos.Add(ReadRoomInfo(r));

            int bpCount = r.ReadByte();
            for (int i = 0; i < bpCount; i++)
                pack.BattlePlayerPacks.Add(ReadBattlePlayerPack(r));

            return pack;
        }

        // UserInfo serialization
        private static void WriteUserInfo(PacketWriter w, UserInfo user)
        {
            w.WriteInt32(user.UserId);
            w.WriteString(user.Username ?? "");
            w.WriteString(user.Password ?? "");
        }

        private static UserInfo ReadUserInfo(PacketReader r)
        {
            return new UserInfo
            {
                UserId = r.ReadInt32(),
                Username = r.ReadString(),
                Password = r.ReadString()
            };
        }

        // BattlePlayerPack serialization
        private static void WriteBattlePlayerPack(PacketWriter w, BattlePlayerPack bp)
        {
            w.WriteInt32(bp.UserId);
            w.WriteInt32(bp.BattleId);
            w.WriteString(bp.PlayerName ?? "");
            w.WriteInt32(bp.TeamId);
            w.WriteInt32(bp.HeroId);
        }

        private static BattlePlayerPack ReadBattlePlayerPack(PacketReader r)
        {
            return new BattlePlayerPack
            {
                UserId = r.ReadInt32(),
                BattleId = r.ReadInt32(),
                PlayerName = r.ReadString(),
                TeamId = r.ReadInt32(),
                HeroId = r.ReadInt32()
            };
        }

        private static void WriteRoomInfo(PacketWriter w, RoomInfo ri)
        {
            w.WriteInt32(ri.RoomId);
            w.WriteString(ri.RoomName ?? "");
            w.WriteString(ri.CreatorName ?? "");
            w.WriteInt32(ri.PlayerCount);
            w.WriteInt32(ri.MaxPlayers);
            w.WriteInt32(ri.Status);
        }

        private static RoomInfo ReadRoomInfo(PacketReader r)
        {
            return new RoomInfo
            {
                RoomId = r.ReadInt32(),
                RoomName = r.ReadString(),
                CreatorName = r.ReadString(),
                PlayerCount = r.ReadInt32(),
                MaxPlayers = r.ReadInt32(),
                Status = r.ReadInt32()
            };
        }

        // BattleInfo serialization
        private static void WriteBattleInfo(PacketWriter w, BattleInfo bi)
        {
            w.WriteInt32(bi.OperationId);
            w.WriteInt32(bi.BattleId);
            w.WriteInt32(bi.RandSeed);
            w.WriteInt32(bi.ClientAckedFrame);

            // SelfOperation
            if (bi.SelfOperation != null)
            {
                w.WriteByte(1);
                WritePlayerOperation(w, bi.SelfOperation);
            }
            else
            {
                w.WriteByte(0);
            }

            // Operations list
            w.WriteByte((byte)bi.Operations.Count);
            foreach (var op in bi.Operations)
                WritePlayerOperation(w, op);

            // AllPlayerOperations list
            w.WriteByte((byte)bi.AllPlayerOperations.Count);
            foreach (var apo in bi.AllPlayerOperations)
                WriteAllPlayerOperation(w, apo);

            // HitEvents list
            w.WriteByte((byte)bi.HitEvents.Count);
            foreach (var he in bi.HitEvents)
                WriteHitEventMsg(w, he);

            // PlayerStates list
            w.WriteByte((byte)bi.PlayerStates.Count);
            foreach (var ps in bi.PlayerStates)
                WritePlayerStateMsg(w, ps);

            // BattlePlayers list
            w.WriteByte((byte)bi.BattlePlayers.Count);
            foreach (var bpi in bi.BattlePlayers)
                WriteBattlePlayerInfo(w, bpi);
        }

        private static BattleInfo ReadBattleInfo(PacketReader r)
        {
            var bi = new BattleInfo();
            bi.OperationId = r.ReadInt32();
            bi.BattleId = r.ReadInt32();
            bi.RandSeed = r.ReadInt32();
            bi.ClientAckedFrame = r.ReadInt32();

            if (r.ReadByte() == 1)
                bi.SelfOperation = ReadPlayerOperation(r);

            int opCount = r.ReadByte();
            for (int i = 0; i < opCount; i++)
                bi.Operations.Add(ReadPlayerOperation(r));

            int apoCount = r.ReadByte();
            for (int i = 0; i < apoCount; i++)
                bi.AllPlayerOperations.Add(ReadAllPlayerOperation(r));

            int heCount = r.ReadByte();
            for (int i = 0; i < heCount; i++)
                bi.HitEvents.Add(ReadHitEventMsg(r));

            int psCount = r.ReadByte();
            for (int i = 0; i < psCount; i++)
                bi.PlayerStates.Add(ReadPlayerStateMsg(r));

            int bpiCount = r.ReadByte();
            for (int i = 0; i < bpiCount; i++)
                bi.BattlePlayers.Add(ReadBattlePlayerInfo(r));

            return bi;
        }

        // PlayerOperation serialization
        private static void WritePlayerOperation(PacketWriter w, PlayerOperation op)
        {
            w.WriteInt32(op.PlayerId);
            w.WriteFloat(op.MoveX);
            w.WriteFloat(op.MoveY);
            w.WriteFloat(op.AimYaw);
            w.WriteFloat(op.AimPitch);
            w.WriteBool(op.Fire);
            w.WriteBool(op.Jump);
            w.WriteInt32(op.AttackId);
            w.WriteInt32(op.ClientFrameId);

            w.WriteByte((byte)op.AttackOperations.Count);
            foreach (var atk in op.AttackOperations)
                WriteAttackOperation(w, atk);

            w.WriteByte((byte)op.AbilityEvents.Count);
            foreach (var evt in op.AbilityEvents)
            {
                w.WriteUInt16(evt.InstanceId);
                w.WriteByte(evt.AssetId);
                w.WriteByte((byte)evt.EventType);
            }

            w.WriteBool(op.Run);
            w.WriteBool(op.Aim);
            w.WriteBool(op.Reload);
        }

        private static PlayerOperation ReadPlayerOperation(PacketReader r)
        {
            var op = new PlayerOperation();
            op.PlayerId = r.ReadInt32();
            op.MoveX = r.ReadFloat();
            op.MoveY = r.ReadFloat();
            op.AimYaw = r.ReadFloat();
            op.AimPitch = r.ReadFloat();
            op.Fire = r.ReadBool();
            op.Jump = r.ReadBool();
            op.AttackId = r.ReadInt32();
            op.ClientFrameId = r.ReadInt32();

            int atkCount = r.ReadByte();
            for (int i = 0; i < atkCount; i++)
                op.AttackOperations.Add(ReadAttackOperation(r));

            int abCount = r.ReadByte();
            for (int i = 0; i < abCount; i++)
            {
                op.AbilityEvents.Add(new AbilityEventData
                {
                    InstanceId = r.ReadUInt16(),
                    AssetId = r.ReadByte(),
                    EventType = (AbilityEventType)r.ReadByte()
                });
            }

            op.Run = r.ReadBool();
            op.Aim = r.ReadBool();
            op.Reload = r.ReadBool();
            return op;
        }

        // AttackOperation serialization
        private static void WriteAttackOperation(PacketWriter w, AttackOperation atk)
        {
            w.WriteInt32(atk.AttackId);
            w.WriteFloat(atk.TowardX);
            w.WriteFloat(atk.TowardY);
            w.WriteFloat(atk.AimPitch);
            w.WriteInt32(atk.ClientFrameId);
            w.WriteVec3(atk.SpawnPos);
        }

        private static AttackOperation ReadAttackOperation(PacketReader r)
        {
            return new AttackOperation
            {
                AttackId = r.ReadInt32(),
                TowardX = r.ReadFloat(),
                TowardY = r.ReadFloat(),
                AimPitch = r.ReadFloat(),
                ClientFrameId = r.ReadInt32(),
                SpawnPos = r.ReadVec3()
            };
        }

        // AllPlayerOperation serialization
        private static void WriteAllPlayerOperation(PacketWriter w, AllPlayerOperation apo)
        {
            w.WriteInt32(apo.FrameId);

            w.WriteByte((byte)apo.Operations.Count);
            foreach (var op in apo.Operations)
                WritePlayerOperation(w, op);

            w.WriteByte((byte)apo.PlayerStates.Count);
            foreach (var ps in apo.PlayerStates)
                WritePlayerStateMsg(w, ps);

            w.WriteByte((byte)apo.HitEvents.Count);
            foreach (var he in apo.HitEvents)
                WriteHitEventMsg(w, he);

            w.WriteByte((byte)apo.AbilityEvents.Count);
            foreach (var evt in apo.AbilityEvents)
            {
                w.WriteUInt16(evt.InstanceId);
                w.WriteByte(evt.AssetId);
                w.WriteByte((byte)evt.EventType);
            }
        }

        private static AllPlayerOperation ReadAllPlayerOperation(PacketReader r)
        {
            var apo = new AllPlayerOperation();
            apo.FrameId = r.ReadInt32();

            int opCount = r.ReadByte();
            for (int i = 0; i < opCount; i++)
                apo.Operations.Add(ReadPlayerOperation(r));

            int psCount = r.ReadByte();
            for (int i = 0; i < psCount; i++)
                apo.PlayerStates.Add(ReadPlayerStateMsg(r));

            int heCount = r.ReadByte();
            for (int i = 0; i < heCount; i++)
                apo.HitEvents.Add(ReadHitEventMsg(r));

            int abCount = r.ReadByte();
            for (int i = 0; i < abCount; i++)
            {
                apo.AbilityEvents.Add(new AbilityEventData
                {
                    InstanceId = r.ReadUInt16(),
                    AssetId = r.ReadByte(),
                    EventType = (AbilityEventType)r.ReadByte()
                });
            }

            return apo;
        }

        // PlayerStateMsg serialization
        private static void WritePlayerStateMsg(PacketWriter w, PlayerStateMsg ps)
        {
            w.WriteInt32(ps.PlayerId);
            w.WriteVec3(ps.Position);
            w.WriteInt32(ps.Hp);
            w.WriteBool(ps.IsDead);
            w.WriteVec3(ps.Velocity);
            w.WriteFloat(ps.VerticalVelocity);
            w.WriteBool(ps.IsGrounded);
            w.WriteInt32(ps.StateEnum);
            w.WriteFloat(ps.FireCooldown);
            w.WriteFloat(ps.RotationY);
            w.WriteBool(ps.IsRunning);
            w.WriteInt32(ps.CurrentAmmo);
            w.WriteBool(ps.IsReloading);
            w.WriteInt64(ps.TagBitmask);

            int abCount = ps.ActiveAbilities?.Count ?? 0;
            w.WriteByte((byte)abCount);
            if (ps.ActiveAbilities != null)
            {
                foreach (var ab in ps.ActiveAbilities)
                {
                    w.WriteUInt16(ab.InstanceId);
                    w.WriteByte(ab.AssetId);
                    w.WriteByte((byte)ab.State);
                    w.WriteFloat(ab.CooldownRemaining);
                    w.WriteFloat(ab.DurationRemaining);
                    w.WriteInt64(ab.AppliedTagsMask);
                }
            }
            w.WriteInt32(ps.MaxHp);
        }

        private static PlayerStateMsg ReadPlayerStateMsg(PacketReader r)
        {
            var ps = new PlayerStateMsg
            {
                PlayerId = r.ReadInt32(),
                Position = r.ReadVec3(),
                Hp = r.ReadInt32(),
                IsDead = r.ReadBool(),
                Velocity = r.ReadVec3(),
                VerticalVelocity = r.ReadFloat(),
                IsGrounded = r.ReadBool(),
                StateEnum = r.ReadInt32(),
                FireCooldown = r.ReadFloat(),
                RotationY = r.ReadFloat(),
                IsRunning = r.ReadBool(),
                CurrentAmmo = r.ReadInt32(),
                IsReloading = r.ReadBool(),
                TagBitmask = r.ReadInt64()
            };

            int abCount = r.ReadByte();
            if (abCount > 0)
            {
                ps.ActiveAbilities = new List<AbilityInstanceData>(abCount);
                for (int i = 0; i < abCount; i++)
                {
                    ps.ActiveAbilities.Add(new AbilityInstanceData
                    {
                        InstanceId = r.ReadUInt16(),
                        AssetId = r.ReadByte(),
                        State = (AbilityState)r.ReadByte(),
                        CooldownRemaining = r.ReadFloat(),
                        DurationRemaining = r.ReadFloat(),
                        AppliedTagsMask = r.ReadInt64()
                    });
                }
            }
            ps.MaxHp = r.ReadInt32();
            return ps;
        }

        // HitEventMsg serialization
        private static void WriteHitEventMsg(PacketWriter w, HitEventMsg he)
        {
            w.WriteInt32(he.AttackId);
            w.WriteInt32(he.AttackerId);
            w.WriteInt32(he.VictimId);
            w.WriteInt32(he.Damage);
            w.WriteBool(he.IsKill);
            w.WriteVec3(he.HitPoint);
            w.WriteInt32(he.HitFrameId);
        }

        private static HitEventMsg ReadHitEventMsg(PacketReader r)
        {
            return new HitEventMsg
            {
                AttackId = r.ReadInt32(),
                AttackerId = r.ReadInt32(),
                VictimId = r.ReadInt32(),
                Damage = r.ReadInt32(),
                IsKill = r.ReadBool(),
                HitPoint = r.ReadVec3(),
                HitFrameId = r.ReadInt32()
            };
        }

        // BattlePlayerInfo serialization
        private static void WriteBattlePlayerInfo(PacketWriter w, BattlePlayerInfo bpi)
        {
            w.WriteInt32(bpi.PlayerId);
            w.WriteInt32(bpi.TeamId);
            w.WriteInt32(bpi.UserId);
            w.WriteString(bpi.PlayerName ?? "");
            w.WriteVec3(bpi.SpawnPosition);
            w.WriteInt32(bpi.HeroId);
        }

        private static BattlePlayerInfo ReadBattlePlayerInfo(PacketReader r)
        {
            return new BattlePlayerInfo
            {
                PlayerId = r.ReadInt32(),
                TeamId = r.ReadInt32(),
                UserId = r.ReadInt32(),
                PlayerName = r.ReadString(),
                SpawnPosition = r.ReadVec3(),
                HeroId = r.ReadInt32()
            };
        }
    }
}