using System;
using System.Net;

namespace ShootingGame.Shared.Protocol
{
    public enum ConnectionState : byte
    {
        Disconnected,
        Connecting,
        Connected
    }

    /// <summary>
    /// Manages the connection lifecycle for a single peer connection.
    /// Handles handshake, heartbeat, and timeout detection.
    /// Used by both client (single connection to server) and server (per-client).
    /// </summary>
    public class Connection
    {
        public IPEndPoint RemoteEndPoint;
        public byte PlayerId;
        public ConnectionState State;
        public KcpChannel Kcp;

        public float LastReceiveTime;
        public float LastSendTime;
        public float ConnectTime;

        private const float HeartbeatInterval = 1.0f;
        private const float TimeoutDuration = 5.0f;

        public Connection(IPEndPoint remote, byte playerId, uint conv, Action<byte[], int> onSend)
        {
            RemoteEndPoint = remote;
            PlayerId = playerId;
            State = ConnectionState.Disconnected;
            Kcp = new KcpChannel(conv, onSend);
        }

        public void MarkReceived(float currentTime)
        {
            LastReceiveTime = currentTime;
        }

        public void MarkSent(float currentTime)
        {
            LastSendTime = currentTime;
        }

        public bool IsTimedOut(float currentTime)
        {
            return currentTime - LastReceiveTime > TimeoutDuration;
        }

        public bool NeedsHeartbeat(float currentTime)
        {
            return currentTime - LastSendTime > HeartbeatInterval;
        }

        public float Rtt => Kcp.SmoothedRtt;
    }
}
