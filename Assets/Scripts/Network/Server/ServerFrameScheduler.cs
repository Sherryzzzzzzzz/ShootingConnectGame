using System.Collections.Generic;
using ShootingGame.Shared.Simulation;

namespace ShootingGame.Network.Server
{
    /// <summary>
    /// I/P帧发送调度器 + 状态版本对账。
    /// </summary>
    public class ServerFrameScheduler
    {
        public int IFrameInterval { get; set; } = 10;

        private int _currentTick;
        private readonly Dictionary<int, int> _lastIFrameTick = new Dictionary<int, int>();
        private readonly HashSet<int> _pendingIFrameRequests = new HashSet<int>();

        /// <summary>Monotonic state revision — incremented on authoritative state changes.</summary>
        public StateRevision Revision;

        public void Tick(int tick) { _currentTick = tick; }
        public void RegisterClient(int clientId) { _lastIFrameTick[clientId] = -1; }
        public void UnregisterClient(int clientId) { _lastIFrameTick.Remove(clientId); _pendingIFrameRequests.Remove(clientId); }
        public void RequestIFrame(int clientId) { _pendingIFrameRequests.Add(clientId); }

        public bool ShouldSendIFrame(int clientId)
        {
            if (!_lastIFrameTick.TryGetValue(clientId, out int lastTick) || lastTick < 0) return true;
            if (_pendingIFrameRequests.Contains(clientId)) return true;
            if (_currentTick - lastTick >= IFrameInterval) return true;
            return false;
        }

        public void MarkIFrameSent(int clientId)
        {
            _lastIFrameTick[clientId] = _currentTick;
            _pendingIFrameRequests.Remove(clientId);
        }

        public bool ShouldSendPFrame(int clientId)
        {
            if (ShouldSendIFrame(clientId)) return false;
            return true;
        }

        /// <summary>Validate client operation revision against current state.</summary>
        public bool ValidateRevision(uint clientBaseRevision)
        {
            return Revision.IsCurrent(clientBaseRevision);
        }
    }
}
