using System.Collections.Generic;
using ShootingGame.Shared.Simulation;

namespace ShootingGame.Network.Server
{
    /// <summary>
    /// 服务端输入缓冲。存储每个客户端最近收到的输入帧（含冗余），
    /// 在 tick 时检索可用的最新输入。
    ///
    /// 策略：不等待 + 冗余回退 + 动作事件不重放。
    /// </summary>
    public class ServerInputBuffer
    {
        /// <summary>最大缓冲帧数</summary>
        private const int MaxBufferSize = 128;
        /// <summary>最大客户端数</summary>
        private const int MaxClients = 16;

        /// <summary>每个客户端的输入环形缓冲 [clientIndex][tick % MaxBufferSize]</summary>
        private readonly InputFrame?[][] _buffers;
        /// <summary>每个客户端最新收到的非空输入（用于丢包时的回退）</summary>
        private readonly InputFrame?[] _fallbackInputs;

        public ServerInputBuffer()
        {
            _buffers = new InputFrame?[MaxClients][];
            _fallbackInputs = new InputFrame?[MaxClients];
            for (int i = 0; i < MaxClients; i++)
                _buffers[i] = new InputFrame?[MaxBufferSize];
        }

        /// <summary>
        /// 存储来自客户端的输入帧（含冗余帧）。
        /// </summary>
        /// <param name="clientIndex">客户端索引 (0-based, < MaxClients)</param>
        /// <param name="frames">输入帧数组（最新帧在前，冗余帧在后）</param>
        public void StoreInputs(int clientIndex, InputFrame[] frames)
        {
            if (clientIndex < 0 || clientIndex >= MaxClients) return;

            var buffer = _buffers[clientIndex];
            foreach (var frame in frames)
            {
                int slot = frame.Tick % MaxBufferSize;
                // 只保留每个 tick 的第一份输入（防止重复）
                if (buffer[slot] == null || buffer[slot]?.Tick != frame.Tick)
                    buffer[slot] = frame;
            }

            // 更新回退输入
            if (frames.Length > 0)
                _fallbackInputs[clientIndex] = frames[0];
        }

        /// <summary>
        /// 获取指定 tick 的客户端输入。
        /// 优先精确匹配，匹配不到用最接近的旧帧回退（动作事件禁用）。
        /// </summary>
        /// <param name="clientIndex">客户端索引</param>
        /// <param name="tick">目标 tick</param>
        /// <returns>输入帧。如果完全没有历史输入则返回 null。</returns>
        public InputFrame? GetInput(int clientIndex, int tick)
        {
            if (clientIndex < 0 || clientIndex >= MaxClients) return null;

            var buffer = _buffers[clientIndex];
            int slot = tick % MaxBufferSize;

            // 精确匹配（当前 tick 有输入）
            if (buffer[slot]?.Tick == tick)
                return buffer[slot]!.Value;

            // 回退：使用最近的旧输入，但禁用动作事件
            var fallback = _fallbackInputs[clientIndex];
            if (fallback.HasValue)
            {
                var input = fallback.Value;
                // 用旧帧的移动方向/瞄准/跑动，但禁止开火/跳跃/换弹/技能
                input.Tick = tick;
                input.Jump = false;
                input.Fire = false;
                input.Reload = false;
                input.Ability1 = false;
                input.Ability2 = false;
                input.Ability3 = false;
                input.Ability4 = false;
                return input;
            }

            return null;
        }

        /// <summary>
        /// 清除指定客户端的所有缓冲（断线时调用）。
        /// </summary>
        public void ClearClient(int clientIndex)
        {
            if (clientIndex < 0 || clientIndex >= MaxClients) return;
            for (int i = 0; i < MaxBufferSize; i++)
                _buffers[clientIndex][i] = null;
            _fallbackInputs[clientIndex] = null;
        }

        /// <summary>
        /// 清空所有缓冲。
        /// </summary>
        public void Clear()
        {
            for (int i = 0; i < MaxClients; i++)
                ClearClient(i);
        }
    }
}
