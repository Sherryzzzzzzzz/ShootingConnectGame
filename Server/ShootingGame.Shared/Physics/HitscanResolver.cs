using ShootingGame.Shared.Math;
using ShootingGame.Shared.Simulation;

namespace ShootingGame.Shared.Physics
{
    public struct HitscanResult
    {
        public bool Hit;
        public byte TargetId;
        public Vec3 HitPoint;
        public float Distance;

        public static readonly HitscanResult Miss = new HitscanResult { Hit = false };
    }

    /// <summary>
    /// Resolves hitscan (instant raycast) against player capsules from a historical world snapshot.
    /// Used by the server for lag-compensated hit detection.
    /// </summary>
    public static class HitscanResolver
    {
        /// <summary>
        /// Cast a ray against all players in the snapshot (except the shooter).
        /// Returns the closest player hit within maxDistance.
        /// </summary>
        public static HitscanResult Resolve(Vec3 origin, Vec3 direction, WorldSnapshot snapshot, byte shooterId, float maxDistance)
        {
            if (snapshot.Players == null) return HitscanResult.Miss;

            HitscanResult closest = HitscanResult.Miss;
            float closestDist = maxDistance;

            Ray ray = new Ray(origin, direction);

            for (int i = 0; i < snapshot.Players.Length; i++)
            {
                if (i == shooterId) continue;

                var playerSnap = snapshot.Players[i];
                if (playerSnap.Health <= 0) continue; // skip dead players

                // Build capsule at historical position
                Capsule capsule = Capsule.Player(playerSnap.Position);

                var hit = Intersection.RayCapsule(ray, capsule, closestDist);
                if (hit.Hit && hit.Distance < closestDist)
                {
                    closest = new HitscanResult
                    {
                        Hit = true,
                        TargetId = (byte)i,
                        HitPoint = hit.Point,
                        Distance = hit.Distance
                    };
                    closestDist = hit.Distance;
                }
            }

            return closest;
        }
    }
}
