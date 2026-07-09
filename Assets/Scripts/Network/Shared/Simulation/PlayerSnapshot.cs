using ShootingGame.Shared.Math;

namespace ShootingGame.Shared.Simulation
{
    public enum PlayerStateEnum : byte
    {
        Ground = 0,
        Sky = 1,
        Aim = 2
    }

    public struct PlayerSnapshot
    {
        public int Tick;
        public Vec3 Position;
        public Quat Rotation;
        public Vec3 Velocity;
        public float VerticalVelocity;
        public bool IsGrounded;
        public PlayerStateEnum State;
        public float FireCooldown;
        public byte Health;

        // Ammo
        public int CurrentAmmo;
        public bool IsReloading;
        public float ReloadTimer;

        // Tags
        public long TagBitmask;

        // Ability
        public Ability.AbilityInstanceData[] ActiveAbilities;
        public byte ActiveAbilityCount;

        public static PlayerSnapshot Default(Vec3 spawnPosition)
        {
            return new PlayerSnapshot
            {
                Tick = 0,
                Position = spawnPosition,
                Rotation = Quat.Identity,
                Velocity = Vec3.Zero,
                VerticalVelocity = 0f,
                IsGrounded = true,
                State = PlayerStateEnum.Ground,
                FireCooldown = 0f,
                Health = GameConstants.MaxHealth,
                CurrentAmmo = GameConstants.MaxAmmoPerClip,
                IsReloading = false,
                ReloadTimer = 0f
            };
        }
    }
}
