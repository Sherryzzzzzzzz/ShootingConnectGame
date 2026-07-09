using ShootingGame.Shared.Math;

namespace ShootingGame.Shared.Physics
{
    /// <summary>
    /// Capsule collider defined by a base position (feet), height, and radius.
    /// The capsule center is at base + (0, height/2, 0).
    /// </summary>
    public struct Capsule
    {
        public Vec3 Base;    // feet position
        public float Height;
        public float Radius;

        public Capsule(Vec3 basePos, float height, float radius)
        {
            Base = basePos;
            Height = height;
            Radius = radius;
        }

        public Vec3 Center => Base + Vec3.Up * (Height * 0.5f);

        /// <summary>
        /// Bottom sphere center (feet + radius up).
        /// </summary>
        public Vec3 Bottom => Base + Vec3.Up * Radius;

        /// <summary>
        /// Top sphere center (base + height - radius up).
        /// </summary>
        public Vec3 Top => Base + Vec3.Up * (Height - Radius);

        /// <summary>
        /// Build an AABB that fully contains this capsule.
        /// </summary>
        public AABB BoundingBox()
        {
            return new AABB(
                new Vec3(Base.x - Radius, Base.y, Base.z - Radius),
                new Vec3(Base.x + Radius, Base.y + Height, Base.z + Radius)
            );
        }

        /// <summary>
        /// Create a player-sized capsule at the given feet position.
        /// </summary>
        public static Capsule Player(Vec3 feetPosition)
        {
            return new Capsule(feetPosition, Simulation.GameConstants.PlayerHeight, Simulation.GameConstants.PlayerRadius);
        }
    }
}
