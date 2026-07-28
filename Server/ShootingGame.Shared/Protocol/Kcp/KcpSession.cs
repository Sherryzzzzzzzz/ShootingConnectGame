using System;
using System.Collections.Generic;

namespace ShootingGame.Shared.Protocol.Kcp
{
    /// <summary>
    /// Session layer on top of KcpChannel. Manages heartbeat, timeout detection,
    /// RTT smoothing, and connection statistics. Replaces manual heartbeat logic
    /// in NetworkClient and Connection.
    ///
    /// Architecture: KCP (protocol) → KcpChannel (multiplexing) → KcpSession (lifecycle)
    /// </summary>
    public class KcpSession
    {
        // ── Heartbeat / Timeout configuration ──
        public float HeartbeatIntervalSec = 1.0f;
        public float FastHeartbeatIntervalSec = 0.2f;  // used for first 5s to establish RTT quickly
        public float FastHeartbeatDurationSec = 5.0f;
        public float TimeoutDurationSec = 10.0f;

        // ── RTT smoothing ──
        private float _smoothedRtt;
        private float _ewmaRttAlpha = 0.9f;
        public float SmoothedRtt
        {
            get
            {
                // Prefer KCP's internal RTO-based estimate when available
                float kcpRto = _channel.Kcp?.Rto ?? 0f;
                if (kcpRto > 0f) return kcpRto * 0.5f; // RTO ≈ 2×RTT typical
                return _smoothedRtt;
            }
        }

        // ── State ──
        public uint Conv { get; }
        public bool IsConnected { get; private set; }
        public float LastReceiveTime { get; private set; }
        public float LastSendTime { get; private set; }
        public float ConnectTime { get; private set; }
        public bool IsTimedOut => _currentTime - LastReceiveTime > TimeoutDurationSec;

        // ── Statistics ──
        public long TotalSentBytes { get; private set; }
        public long TotalRecvBytes { get; private set; }
        public long TotalSentPackets { get; private set; }
        public long TotalRecvPackets { get; private set; }
        public int SendQueueSize => _channel.Kcp?.WaitSnd ?? 0;

        // ── Events ──
        public event Action OnConnected;
        public event Action OnTimeout;
        public event Action OnDisconnected;
        public event Action<float> OnHeartbeatResponse; // RTT sample (seconds)

        // ── Internals ──
        private readonly KcpChannel _channel;
        private float _currentTime;
        private float _lastHeartbeatTime;
        private bool _timedOut;
        private bool _firstHeartbeatReceived;

        /// <summary>
        /// The underlying KcpChannel (for direct access to SendReliable/WrapUnreliable/Input).
        /// </summary>
        public KcpChannel Channel => _channel;

        /// <summary>
        /// Raw KCP engine (for WaitSnd, Rto, etc.).
        /// </summary>
        public KCP Kcp => _channel.Kcp;

        public KcpSession(uint conv, Action<byte[], int> output)
        {
            Conv = conv;
            _channel = new KcpChannel(conv, output);
            _smoothedRtt = 0.05f;
        }

        /// <summary>
        /// Mark the session as connected (called after handshake completes).
        /// </summary>
        public void MarkConnected(float currentTime)
        {
            IsConnected = true;
            ConnectTime = currentTime;
            LastReceiveTime = currentTime;
            LastSendTime = currentTime;
            _lastHeartbeatTime = currentTime;
            _firstHeartbeatReceived = false;
            OnConnected?.Invoke();
        }

        /// <summary>
        /// Mark that data was received at this time.
        /// </summary>
        public void MarkReceived(float currentTime)
        {
            LastReceiveTime = currentTime;
        }

        /// <summary>
        /// Mark that data was sent at this time.
        /// </summary>
        public void MarkSent(int byteCount)
        {
            TotalSentBytes += byteCount;
            TotalSentPackets++;
            LastSendTime = _currentTime;
        }

        /// <summary>
        /// Record received byte count for statistics.
        /// </summary>
        public void RecordRecv(int byteCount)
        {
            TotalRecvBytes += byteCount;
            TotalRecvPackets++;
        }

        /// <summary>
        /// Drive the session: update KCP, drain received messages, check heartbeat/timeout.
        /// Call this every frame (or every ~10ms for KCP timing).
        /// </summary>
        public void Update(float currentTimeSec)
        {
            _currentTime = currentTimeSec;

            // Drive KCP state machine
            _channel.Update((uint)(currentTimeSec * 1000));

            // Check timeout on connected sessions
            if (IsConnected && !_timedOut)
            {
                if (currentTimeSec - LastReceiveTime > TimeoutDurationSec)
                {
                    _timedOut = true;
                    IsConnected = false;
                    OnTimeout?.Invoke();
                    return;
                }

                // Send heartbeat
                CheckHeartbeat(currentTimeSec);
            }
        }

        /// <summary>
        /// Feed raw received UDP data. Returns true if an unreliable message was extracted.
        /// </summary>
        public bool Input(byte[] data, int offset, int length, out byte[] unreliablePayload)
        {
            RecordRecv(length);
            LastReceiveTime = _currentTime;
            return _channel.Input(data, offset, length, out unreliablePayload);
        }

        public bool Input(byte[] data, int length, out byte[] unreliablePayload)
        {
            return Input(data, 0, length, out unreliablePayload);
        }

        /// <summary>
        /// Get next reliable message from KCP. Returns null if none available.
        /// </summary>
        public byte[] TryRecv()
        {
            _channel.TryRecv(out byte[] msg);
            return msg;
        }

        /// <summary>
        /// Drain all pending reliable messages.
        /// </summary>
        public List<byte[]> DrainRecv()
        {
            return _channel.DrainRecv();
        }

        /// <summary>
        /// Send reliable data via KCP.
        /// </summary>
        public void SendReliable(byte[] data)
        {
            _channel.SendReliable(data);
            MarkSent(data.Length);
        }

        /// <summary>
        /// Wrap and track unreliable data.
        /// </summary>
        public void MarkUnreliableSent(byte[] packet)
        {
            MarkSent(packet.Length);
        }

        /// <summary>
        /// Called by the application when a heartbeat response (Pong) is received.
        /// Updates the smoothed RTT estimate.
        /// </summary>
        public void OnPongReceived(float rttSampleSec)
        {
            if (!_firstHeartbeatReceived)
            {
                _smoothedRtt = rttSampleSec;
                _firstHeartbeatReceived = true;
            }
            else
            {
                _smoothedRtt = _ewmaRttAlpha * _smoothedRtt + (1f - _ewmaRttAlpha) * rttSampleSec;
            }
            OnHeartbeatResponse?.Invoke(rttSampleSec);
        }

        public void Disconnect()
        {
            IsConnected = false;
            _timedOut = false;
            OnDisconnected?.Invoke();
        }

        private void CheckHeartbeat(float currentTime)
        {
            float interval = (currentTime - ConnectTime < FastHeartbeatDurationSec)
                ? FastHeartbeatIntervalSec
                : HeartbeatIntervalSec;

            if (currentTime - _lastHeartbeatTime >= interval)
            {
                _lastHeartbeatTime = currentTime;
                SendHeartbeat();
            }
        }

        /// <summary>
        /// Override to customize heartbeat packet content.
        /// Default sends a minimal 1-byte heartbeat via unreliable channel.
        /// </summary>
        protected virtual byte[] BuildHeartbeatPacket()
        {
            return new byte[] { 0xFF }; // heartbeat marker
        }

        private void SendHeartbeat()
        {
            byte[] hbData = BuildHeartbeatPacket();
            byte[] packet = KcpChannel.WrapUnreliable(Conv, hbData, 0, hbData.Length);
            _channel.SendRaw(packet);
            MarkSent(packet.Length);
        }
    }
}
