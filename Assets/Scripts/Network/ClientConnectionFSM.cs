using System;
using UnityEngine;

namespace ShootingGame.Shared.Protocol.Kcp
{
    /// <summary>
    /// Client connection state machine. Manages the lifecycle:
    ///   Disconnected → Handshaking → Connected
    ///                     ↑              ↓
    ///                     └── Reconnecting ←┘
    /// </summary>
    public class ClientConnectionFSM
    {
        public enum State { Disconnected, Handshaking, Connected, Reconnecting }

        public State CurrentState { get; private set; } = State.Disconnected;

        // Events
        public event Action<State, State> OnStateChanged; // (from, to)
        public event Action OnConnected;
        public event Action OnDisconnected;
        public event Action OnReconnecting;

        // Handshake config
        public int MaxHandshakeRetries = 5;
        public float HandshakeRetryIntervalSec = 1.0f;
        public float ReconnectTimeoutSec = 30.0f;
        public float ReconnectBackoffBaseSec = 0.5f; // 重连指数退避基数
        public float ReconnectBackoffMaxSec = 10.0f;  // 重连最大间隔
        public int MaxReconnectAttempts = 10;

        // Token (assigned by server, preserved across reconnects)
        public string SessionToken { get; private set; }
        public byte AssignedPlayerId { get; private set; } = 255;

        // 指数退避重连跟踪
        private int _reconnectAttempts;
        private float _reconnectDelaySec;

        // Handshake state
        private int _handshakeAttempts;
        private float _lastHandshakeTime;
        private float _reconnectStartTime;

        // Action callbacks (set by NetworkClient)
        public Func<bool> SendHandshakeRequest;
        public Action OnEnterHandshaking;
        public Action OnEnterConnected;
        public Action OnEnterDisconnected;
        public Action OnEnterReconnecting;

        // ── Transitions ──

        public void StartHandshake()
        {
            if (CurrentState == State.Connected) return;
            TransitionTo(State.Handshaking);
            _handshakeAttempts = 0;
            SendHandshakeAttempt();
        }

        public void OnHandshakeAccepted(byte playerId, string token)
        {
            AssignedPlayerId = playerId;
            SessionToken = token;
            TransitionTo(State.Connected);
        }

        public void OnHandshakeFailed()
        {
            _handshakeAttempts++;
            if (_handshakeAttempts < MaxHandshakeRetries)
            {
                // Will retry in Update tick
            }
            else
            {
                Debug.LogWarning($"[ClientFSM] Handshake failed after {MaxHandshakeRetries} attempts");
                TransitionTo(State.Disconnected);
            }
        }

        public void OnConnectionLost()
        {
            if (CurrentState == State.Connected)
            {
                _reconnectStartTime = Time.unscaledTime;
                _reconnectAttempts = 0;
                _reconnectDelaySec = ReconnectBackoffBaseSec;
                TransitionTo(State.Reconnecting);
            }
        }

        /// <summary>重连成功（服务端接受了Token）</summary>
        public void OnReconnected()
        {
            _reconnectAttempts = 0;
            _reconnectDelaySec = ReconnectBackoffBaseSec;
            TransitionTo(State.Connected);
        }

        public void OnReconnectFailed()
        {
            TransitionTo(State.Disconnected);
        }

        // ── Update (call from NetworkClient.Update) ──

        public void Update(float currentTime)
        {
            switch (CurrentState)
            {
                case State.Handshaking:
                    if (currentTime - _lastHandshakeTime >= HandshakeRetryIntervalSec
                        && _handshakeAttempts < MaxHandshakeRetries)
                    {
                        SendHandshakeAttempt();
                    }
                    break;

                case State.Reconnecting:
                    if (currentTime - _reconnectStartTime > ReconnectTimeoutSec)
                    {
                        Debug.LogWarning($"[ClientFSM] Reconnection timed out after {ReconnectTimeoutSec}s");
                        OnReconnectFailed();
                    }
                    else if (currentTime - _lastHandshakeTime >= _reconnectDelaySec
                             && _reconnectAttempts < MaxReconnectAttempts)
                    {
                        _reconnectAttempts++;
                        // 指数退避: delay = min(base * 2^(n-1), max)
                        _reconnectDelaySec = Mathf.Min(ReconnectBackoffBaseSec * Mathf.Pow(2, _reconnectAttempts - 1), ReconnectBackoffMaxSec);
                        SendHandshakeAttempt();
                    }
                    break;
            }
        }

        // ── Internal ──

        private void SendHandshakeAttempt()
        {
            _lastHandshakeTime = Time.unscaledTime;
            bool sent = SendHandshakeRequest?.Invoke() ?? false;
            if (sent)
                Debug.Log($"[ClientFSM] Handshake attempt {_handshakeAttempts + 1}/{MaxHandshakeRetries}");
        }

        private void TransitionTo(State newState)
        {
            if (CurrentState == newState) return;

            State oldState = CurrentState;

            // Exit old state
            switch (oldState)
            {
                case State.Connected:
                    // connection lost
                    break;
            }

            CurrentState = newState;

            // Enter new state
            switch (newState)
            {
                case State.Handshaking:
                    _handshakeAttempts = 0;
                    _lastHandshakeTime = 0f;
                    OnEnterHandshaking?.Invoke();
                    break;

                case State.Connected:
                    OnConnected?.Invoke();
                    OnEnterConnected?.Invoke();
                    break;

                case State.Disconnected:
                    AssignedPlayerId = 255;
                    SessionToken = null;
                    OnDisconnected?.Invoke();
                    OnEnterDisconnected?.Invoke();
                    break;

                case State.Reconnecting:
                    _reconnectStartTime = Time.unscaledTime;
                    OnReconnecting?.Invoke();
                    OnEnterReconnecting?.Invoke();
                    break;
            }

            OnStateChanged?.Invoke(oldState, newState);
            Debug.Log($"[ClientFSM] {oldState} → {newState}");
        }
    }
}
