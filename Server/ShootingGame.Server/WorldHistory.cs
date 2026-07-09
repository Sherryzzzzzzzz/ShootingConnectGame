using ShootingGame.Shared.Simulation;

namespace ShootingGame.Server
{
    /// <summary>
    /// Ring buffer storing WorldSnapshots for lag compensation.
    /// </summary>
    public class WorldHistory
    {
        private readonly WorldSnapshot[] _buffer;
        private readonly int _capacity;

        public WorldHistory(int capacity = GameConstants.WorldHistorySize)
        {
            _capacity = capacity;
            _buffer = new WorldSnapshot[capacity];
        }

        public void Store(int tick, WorldSnapshot snapshot)
        {
            _buffer[tick % _capacity] = snapshot;
        }

        public WorldSnapshot Get(int tick)
        {
            return _buffer[tick % _capacity];
        }

        public bool HasTick(int tick)
        {
            return _buffer[tick % _capacity].Tick == tick;
        }
    }
}
