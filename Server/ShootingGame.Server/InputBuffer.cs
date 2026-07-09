using System;
using ShootingGame.Shared.Simulation;

namespace ShootingGame.Server
{
    /// <summary>
    /// Per-player input ring buffer. Stores InputFrames indexed by tick.
    /// Falls back to last known input if a tick is missing.
    /// </summary>
    public class InputBuffer
    {
        private readonly InputFrame[] _buffer;
        private readonly int _capacity;
        private int _lastReceivedTick = -1;
        private InputFrame _lastInput;
#if DEBUG
        private int _logCount;
#endif

        public InputBuffer(int capacity = GameConstants.SnapshotHistorySize)
        {
            _capacity = capacity;
            _buffer = new InputFrame[capacity];
        }

        public int LastReceivedTick => _lastReceivedTick;

        public void Store(InputFrame input)
        {
            if (input.Tick <= _lastReceivedTick) return; // ignore old/duplicate

            int index = input.Tick % _capacity;
            _buffer[index] = input;
            _lastReceivedTick = input.Tick;
            _lastInput = input;

#if DEBUG
            if (++_logCount <= 20 || _logCount % 60 == 0)
                Console.WriteLine($"[BUF-STORE] tick={input.Tick} aimYaw={input.AimYaw:F1} run={input.Run} fire={input.Fire}");
#endif
        }

        /// <summary>
        /// Get input for a specific tick. If missing, returns the last known input.
        /// </summary>
        public InputFrame Get(int tick)
        {
            if (_lastReceivedTick < 0)
            {
#if DEBUG
                if (++_logCount <= 10)
                    Console.WriteLine($"[BUF-GET] tick={tick}: NO_INPUT yet, returning default AimYaw=0");
#endif
                return new InputFrame { Tick = tick };
            }

            int index = tick % _capacity;
            if (_buffer[index].Tick == tick)
            {
#if DEBUG
                if (_logCount <= 20 || _logCount % 60 == 0)
                    Console.WriteLine($"[BUF-GET] tick={tick}: EXACT match, aimYaw={_buffer[index].AimYaw:F1}");
#endif
                return _buffer[index];
            }

            // Missing — reuse last known input with updated tick
            var fallback = _lastInput;
            fallback.Tick = tick;
#if DEBUG
            if (_logCount <= 20 || _logCount % 60 == 0)
                Console.WriteLine($"[BUF-GET] tick={tick}: FALLBACK (lastRecvTick={_lastReceivedTick}), aimYaw={fallback.AimYaw:F1}");
#endif
            return fallback;
        }
    }
}
