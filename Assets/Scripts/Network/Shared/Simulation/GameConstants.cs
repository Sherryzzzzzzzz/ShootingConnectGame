// 游戏常量
using ShootingGame.Shared.Physics;

namespace ShootingGame.Shared.Simulation
{
    public static class GameConstants
    {
        public const int TickRate = 60;
        public const float TickDelta = 1f / TickRate;

        // 移动
        public const float MoveSpeed = 6f;
        public const float RunMultiplier = 1.5f;
        public const float MovementAcceleration = 12f;
        public const float MovementStopAcceleration = 16f;
        public const float AimMoveMultiplier = 0.38f;
        public const float RotationSpeed = 360f;

        // 物理（委托给 PhysicsConstants，保证单一数据源）
        public const float Gravity = PhysicsConstants.Gravity;
        public const float JumpForce = 8f;
        public const float GroundSnapVelocity = -2f;

        // 玩家
        public const byte MaxHealth = 100;
        public const float PlayerHeight = PhysicsConstants.PlayerHeight;
        public const float PlayerRadius = PhysicsConstants.PlayerRadius;
        public const float HitCapsuleRadius = PhysicsConstants.HitCapsuleRadius;
        public const float BulletRadius = PhysicsConstants.BulletRadius;
        public const float FootCapsuleOffset = PhysicsConstants.FootCapsuleOffset;
        public const float SlopeLimit = PhysicsConstants.SlopeLimit;
        public const float MaxStepHeight = PhysicsConstants.MaxStepHeight;

        // 战斗
        public const float FireRate = 0.15f;
        public const byte HitscanDamage = 25;
        public const float HitscanRange = 200f;
        public const float RespawnDelay = 3f;
        public const int DeathmatchLives = 3;
        public const float MatchDurationSeconds = 300f;

        // 身体部位伤害倍率
        public const float HeadDamageMultiplier = 2.0f;
        public const float ChestDamageMultiplier = 1.0f;
        public const float AbdomenDamageMultiplier = 0.75f;
        public const float LimbDamageMultiplier = 0.5f;

        // 身体部位高度比例（从脚底算起）
        public const float HeadHeightRatio = 0.80f;
        public const float ChestHeightRatio = 0.55f;
        public const float AbdomenHeightRatio = 0.30f;

        // 网络
        public const int InputRedundancy = 3;
        public const int MaxCompensationTicks = 12;
        public const int SnapshotHistorySize = 128;
        public const int WorldHistorySize = 64;

        // 弹药
        public const int MaxAmmoPerClip = 30;
        public const float ReloadTime = 2.0f;

        // 最大玩家数
        public const int MaxPlayers = 10;
        public const int MaxPlayersPerTeam = 5;
        public const int MaxTeams = 2;
    }

    public struct SpawnPoint
    {
        public Math.Vec3 Position;
        public float Yaw;
        public int TeamId; // 0=任意, 1=队伍1, 2=队伍2

        public SpawnPoint(Math.Vec3 position, float yaw = 0f, int teamId = 0)
        {
            Position = position;
            Yaw = yaw;
            TeamId = teamId;
        }
    }
}
