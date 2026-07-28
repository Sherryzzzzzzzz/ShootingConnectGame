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

        // Hero Select (220-229)
        HeroSelected = 220,
        HeroConfirmed = 221,

        // RPC & Delta (230-239)
        RpcCall = 230,
        DeltaState = 231,
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
        public byte[] RpcPayload;
        public List<ScoreEntryMsg> ScoreEntries = new List<ScoreEntryMsg>();
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
        public bool Crouch;
        public int AttackId;
        public int ClientFrameId;
        // 客户端预测位置/速度（服务端验证用，字段 15-20）
        public float PosX;
        public float PosY;
        public float PosZ;
        public float VelX;
        public float VelZ;
        public bool IsGrounded;
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
        public bool IsAiming;       // 是否瞄准（远程动画同步）
        public bool IsCrouching;    // 是否蹲伏（远程动画同步）
        public int CurrentAmmo;
        public bool IsReloading;
        public long TagBitmask;
        public List<AbilityInstanceData> ActiveAbilities;
        public int MaxHp;
        public int Kills;       // 击杀数（服务器权威，用于记分板）
        public int Deaths;      // 死亡数（服务器权威，用于记分板）
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
        public int BodyPart;    // 命中部位: 0=胸 1=头 2=腹 3=四肢
    }

    /// <summary>记分板条目（GameOver 时随包下发）</summary>
    public class ScoreEntryMsg
    {
        public int PlayerId;
        public string PlayerName;
        public int Kills;
        public int Deaths;
    }

    // ==================== Serialization ====================
    // ProtoSerializer removed — all serialization now unified in ProtobufSerializer.cs (Google.Protobuf)
}