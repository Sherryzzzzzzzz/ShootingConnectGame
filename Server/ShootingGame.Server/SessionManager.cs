using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using ShootingGame.Shared.ECS;
using ShootingGame.Shared.Math;
using ShootingGame.Shared.Simulation;

namespace ShootingGame.Server
{
    /// <summary>
    /// Manages persistent player sessions for reconnection support.
    /// When a player disconnects, their session state is preserved for
    /// a configurable grace period, allowing them to reconnect without
    /// losing progress (HP, position, entities, etc.).
    ///
    /// Reference: SpaceBuilder's ClientTarget with token-based reconnect.
    /// </summary>
    public class SessionManager
    {
        /// <summary>How long to preserve a disconnected player's state (seconds).</summary>
        public float ReconnectGracePeriodSec = 30.0f;

        /// <summary>Max reconnection attempts per player.</summary>
        public int MaxReconnectAttempts = 3;

        private readonly ConcurrentDictionary<byte, PlayerSession> _sessions = new ConcurrentDictionary<byte, PlayerSession>();
        private readonly ConcurrentDictionary<string, byte> _tokenToPlayerId = new ConcurrentDictionary<string, byte>();

        /// <summary>
        /// Per-player session data preserved across disconnections.
        /// </summary>
        public class PlayerSession
        {
            public byte PlayerId;
            public string Token;              // Reconnection token (validated on reconnect)
            public int ReconnectAttempts;
            public float DisconnectedTime;
            public bool IsDisconnected;
            public IPEndPoint LastEndpoint;

            // Preserved ECS state for WorldSnapshot rebuild
            public Entity EcsEntity;
            public PlayerSnapshot LastSnapshot;
            public int LastServerTick;

            // Battle context
            public int BattleId;
            public string HeroId;
            public int TeamId;
        }

        // ── Token generation ──

        public string GenerateToken(byte playerId)
        {
            string token = Guid.NewGuid().ToString("N")[..12];
            _tokenToPlayerId[token] = playerId;
            return token;
        }

        // ── Session lifecycle ──

        public PlayerSession CreateSession(byte playerId, string heroId, int battleId, int teamId, Entity entity)
        {
            string token = GenerateToken(playerId);
            var session = new PlayerSession
            {
                PlayerId = playerId,
                Token = token,
                HeroId = heroId,
                BattleId = battleId,
                TeamId = teamId,
                EcsEntity = entity,
                IsDisconnected = false,
            };
            _sessions[playerId] = session;
            return session;
        }

        public void MarkConnected(byte playerId, IPEndPoint endpoint)
        {
            if (_sessions.TryGetValue(playerId, out var session))
            {
                session.IsDisconnected = false;
                session.ReconnectAttempts = 0;
                session.LastEndpoint = endpoint;
            }
        }

        public void MarkDisconnected(byte playerId, float currentTime)
        {
            if (_sessions.TryGetValue(playerId, out var session))
            {
                session.IsDisconnected = true;
                session.DisconnectedTime = currentTime;
            }
        }

        public void RemoveSession(byte playerId)
        {
            if (_sessions.TryRemove(playerId, out var session))
            {
                _tokenToPlayerId.TryRemove(session.Token, out _);
            }
        }

        // ── Reconnection validation ──

        /// <summary>
        /// Validate a reconnection attempt. Returns the PlayerSession if valid, null if rejected.
        /// </summary>
        public PlayerSession ValidateReconnect(string token, byte claimedPlayerId, float currentTime)
        {
            // Token lookup
            if (!_tokenToPlayerId.TryGetValue(token, out byte storedPlayerId))
                return null;

            // PlayerId must match
            if (storedPlayerId != claimedPlayerId)
                return null;

            // Session must exist and be disconnected
            if (!_sessions.TryGetValue(claimedPlayerId, out var session))
                return null;

            if (!session.IsDisconnected)
                return null;

            // Token must match
            if (session.Token != token)
                return null;

            // Grace period check
            if (currentTime - session.DisconnectedTime > ReconnectGracePeriodSec)
                return null;

            // Attempt limit
            if (session.ReconnectAttempts >= MaxReconnectAttempts)
                return null;

            session.ReconnectAttempts++;
            return session;
        }

        // ── State preservation ──

        public void SavePlayerState(byte playerId, PlayerSnapshot snapshot, int serverTick)
        {
            if (_sessions.TryGetValue(playerId, out var session))
            {
                session.LastSnapshot = snapshot;
                session.LastServerTick = serverTick;
            }
        }

        public PlayerSnapshot GetPlayerSnapshot(byte playerId)
        {
            return _sessions.TryGetValue(playerId, out var session)
                ? session.LastSnapshot
                : default;
        }

        /// <summary>Remove timed-out disconnected sessions.</summary>
        public void CleanupTimedOut(float currentTime)
        {
            var toRemove = new List<byte>();
            foreach (var kvp in _sessions)
            {
                if (kvp.Value.IsDisconnected
                    && currentTime - kvp.Value.DisconnectedTime > ReconnectGracePeriodSec)
                {
                    toRemove.Add(kvp.Key);
                }
            }
            foreach (var id in toRemove)
                RemoveSession(id);
        }

        public PlayerSession GetSession(byte playerId)
        {
            _sessions.TryGetValue(playerId, out var session);
            return session;
        }

        public IEnumerable<PlayerSession> GetAllSessions() => _sessions.Values;
        public int SessionCount => _sessions.Count;
    }
}
