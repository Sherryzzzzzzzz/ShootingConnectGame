namespace ShootingGame.Shared.Simulation
{
    public static class GameConstants
    {
        public const int TickRate = 60;
        public const float TickDelta = 1f / TickRate;

        // Movement
        public const float MoveSpeed = 6f;
        public const float RunMultiplier = 1.5f;
        public const float MovementAcceleration = 12f;
        public const float MovementStopAcceleration = 16f;
        public const float AimMoveMultiplier = 0.38f;
        public const float RotationSpeed = 360f;

        // Physics
        public const float Gravity = -20f;
        public const float JumpForce = 8f;
        public const float GroundSnapVelocity = -2f;

        // Player
        public const byte MaxHealth = 100;
        public const float PlayerHeight = 1.8f;
        public const float PlayerRadius = 0.35f;          // physics/movement collision radius
        public const float HitCapsuleRadius = 0.5f;       // hit registration capsule radius (more generous)
        public const float BulletRadius = 0.1f;           // bullet sweep sphere radius
        public const float FootCapsuleOffset = 0.2f;      // downward capsule extension for foot/leg coverage
        public const float SlopeLimit = 45f;
        public const float MaxStepHeight = 0.3f;

        // Combat
        public const float FireRate = 0.15f; // seconds between shots
        public const byte HitscanDamage = 25;
        public const float HitscanRange = 200f;
        public const float RespawnDelay = 3f; // seconds before respawn
        public const int DeathmatchLives = 3;
        public const float MatchDurationSeconds = 300f;

        // Body part damage multipliers
        public const float HeadDamageMultiplier = 2.0f;
        public const float ChestDamageMultiplier = 1.0f;
        public const float AbdomenDamageMultiplier = 0.75f;
        public const float LimbDamageMultiplier = 0.5f;

        // Body part height ratios (from feet)
        public const float HeadHeightRatio = 0.80f;
        public const float ChestHeightRatio = 0.55f;
        public const float AbdomenHeightRatio = 0.30f;

        // Network
        public const int InputRedundancy = 3;
        public const int MaxCompensationTicks = 12; // ~200ms at 60 tick
        public const int SnapshotHistorySize = 128;
        public const int WorldHistorySize = 64;

        // Ammo
        public const int MaxAmmoPerClip = 30;
        public const float ReloadTime = 2.0f;

        // Max players
        public const int MaxPlayers = 10;
        public const int MaxPlayersPerTeam = 5;
        public const int MaxTeams = 2;
    }

    public struct SpawnPoint
    {
        public Math.Vec3 Position;
        public float Yaw;     // spawn facing direction
        public int TeamId;    // 0=any, 1=team1, 2=team2

        public SpawnPoint(Math.Vec3 position, float yaw = 0f, int teamId = 0)
        {
            Position = position;
            Yaw = yaw;
            TeamId = teamId;
        }
    }
}
