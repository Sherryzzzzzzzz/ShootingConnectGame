using System;
using System.Collections.Generic;
using System.Linq;
using ShootingGame.Shared.Simulation;

namespace ShootingGame.Server
{
    /// <summary>
    /// Per-player input buffer. Stores inputs by client tick.
    ///
    /// ConsumeNext() 按客户端 tick 严格递增消费：
    /// - 目标 tick 的输入已到达 → 消费它（保留跳跃等边沿事件）
    /// - 目标 tick 未到达（网络延迟/丢包）→ 用"不超前"的最近输入（位置不跳、不丢失边沿）
    ///
    /// 这样既保证移动平滑（缺失时沿用最近输入），又保证边沿事件（跳跃）不丢失。
    /// </summary>
    public class InputBuffer
    {
        private readonly Dictionary<int, InputFrame> _buffer = new Dictionary<int, InputFrame>();
        private int _lastReceivedTick = -1;
        private int _consumedTick = 0;
        private InputFrame _lastInput;
        private const int MaxBufferedTicks = 512;

        public int LastReceivedTick => _lastReceivedTick;

        public void Store(InputFrame input)
        {
            if (input.Tick <= _lastReceivedTick) return; // ignore old/duplicate

            _buffer[input.Tick] = input;
            _lastReceivedTick = input.Tick;
            _lastInput = input;

            // 裁剪过旧输入（防止长期运行内存膨胀）
            if (_buffer.Count > MaxBufferedTicks)
            {
                int oldest = _buffer.Keys.Min();
                _buffer.Remove(oldest);
            }
        }

        /// <summary>
        /// 返回最近收到的输入（位置平滑跟随客户端，不因 tick 频率差异落后）。
        /// 跳跃等边沿事件由 HandlePlayerOperation 事件驱动（_pendingJumps），不依赖输入消费顺序。
        /// </summary>
        public InputFrame ConsumeNext()
        {
            if (_lastReceivedTick < 0)
                return new InputFrame { Tick = 0 };
            _consumedTick = _lastReceivedTick;
            var fb = _lastInput;
            fb.Tick = _consumedTick;
            return fb;
        }

        /// <summary>按 tick 精确取输入（旧版 GameServer 用），缺失回退最近输入。</summary>
        public InputFrame Get(int tick)
        {
            if (_buffer.TryGetValue(tick, out var input))
                return input;
            return _buffer.Count > 0 ? _buffer.Values.Last() : new InputFrame { Tick = tick };
        }

        /// <summary>重置（新对局/重连时）</summary>
        public void Reset()
        {
            _buffer.Clear();
            _lastReceivedTick = -1;
            _consumedTick = -1;
        }
    }
}
