using ShootingGame.Shared.Simulation;

/// <summary>
/// Buffer of timestamped snapshots for smooth interpolation of remote players.
/// </summary>
public class InterpolationBuffer
{
    private struct Entry
    {
        public float Time;
        public PlayerSnapshot Snapshot;
        public bool Valid;
    }

    private readonly Entry[] _buffer;
    private readonly int _capacity;
    private int _writeIndex;
    private int _count;

    public InterpolationBuffer(int capacity = 64)
    {
        _capacity = capacity;
        _buffer = new Entry[capacity];
    }

    public void Add(float time, PlayerSnapshot snapshot)
    {
        _buffer[_writeIndex] = new Entry { Time = time, Snapshot = snapshot, Valid = true };
        _writeIndex = (_writeIndex + 1) % _capacity;
        if (_count < _capacity) _count++;
    }

    /// <summary>
    /// Find two snapshots bracketing the given render time and return them with interpolation factor.
    /// Returns false if not enough data.
    /// </summary>
    public bool Sample(float renderTime, out PlayerSnapshot from, out PlayerSnapshot to, out float t)
    {
        from = default;
        to = default;
        t = 0f;

        // Find the two entries that bracket renderTime
        Entry before = default;
        Entry after = default;
        bool foundBefore = false;
        bool foundAfter = false;

        for (int i = 0; i < _count; i++)
        {
            int idx = ((_writeIndex - 1 - i) % _capacity + _capacity) % _capacity;
            var entry = _buffer[idx];
            if (!entry.Valid) continue;

            if (entry.Time <= renderTime)
            {
                if (!foundBefore || entry.Time > before.Time)
                {
                    before = entry;
                    foundBefore = true;
                }
            }

            if (entry.Time >= renderTime)
            {
                if (!foundAfter || entry.Time < after.Time)
                {
                    after = entry;
                    foundAfter = true;
                }
            }
        }

        if (!foundBefore && !foundAfter) return false;

        if (!foundBefore)
        {
            from = after.Snapshot;
            to = after.Snapshot;
            t = 0f;
            return true;
        }

        if (!foundAfter)
        {
            from = before.Snapshot;
            to = before.Snapshot;
            t = 0f;
            return true;
        }

        from = before.Snapshot;
        to = after.Snapshot;

        float duration = after.Time - before.Time;
        if (duration < 0.0001f)
            t = 0f;
        else
            t = (renderTime - before.Time) / duration;

        return true;
    }
}
