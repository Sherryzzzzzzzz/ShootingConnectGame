using System;
using System.Net;
using ShootingGame.Shared.Protocol.Kcp;

namespace ShootingGame.Shared.Protocol
{
    public enum ConnectionState : byte
    {
        Disconnected,
        Connecting,
        Connected
    }

    /// <summary>
    /// Connection lifecycle wrapper around KcpSession.
    /// Used by GameServer to manage per-client UDP connections.
    /// Timeout detection and heartbeat are delegated to KcpSession.
    /// </summary>
    public class Connection
    {
        public IPEndPoint RemoteEndPoint;
        public byte PlayerId;
        public ConnectionState State;
        public KcpSession Session;

        public float LastReceiveTime => Session?.LastReceiveTime ?? 0f;
        public float LastSendTime => Session?.LastSendTime ?? 0f;
        public float ConnectTime => Session?.ConnectTime ?? 0f;

        private const float HeartbeatInterval = 1.0f;
        private const float TimeoutDuration = 5.0f;

        public Connection(IPEndPoint remote, byte playerId, uint conv, Action<byte[], int> onSend)
        {
            RemoteEndPoint = remote;
            PlayerId = playerId;
            State = ConnectionState.Disconnected;
            Session = new KcpSession(conv, onSend);
            Session.TimeoutDurationSec = TimeoutDuration;
            Session.HeartbeatIntervalSec = HeartbeatInterval;
        }

        public void MarkReceived(float currentTime)
        {
            Session.MarkReceived(currentTime);
        }

        public void MarkSent(float currentTime)
        {
            // Tracked automatically by KcpSession
        }

        public bool IsTimedOut(float currentTime)
        {
            return Session.IsTimedOut;
        }

        public bool NeedsHeartbeat(float currentTime)
        {
            return State == ConnectionState.Connected
                && currentTime - LastSendTime > HeartbeatInterval;
        }

        public float Rtt => Session.SmoothedRtt;

        public void Connect()
        {
            State = ConnectionState.Connecting;
        }

        public void MarkConnected(float currentTime)
        {
            State = ConnectionState.Connected;
            Session.MarkConnected(currentTime);
        }

        public void Disconnect()
        {
            State = ConnectionState.Disconnected;
            Session.Disconnect();
        }
    }
}
