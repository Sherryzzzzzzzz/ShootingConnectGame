// 世界快照
using ShootingGame.Shared.Math;

namespace ShootingGame.Shared.Simulation
{
    public struct WorldSnapshot
    {
        public int Tick;
        public PlayerSnapshot[] Players;
    }
}