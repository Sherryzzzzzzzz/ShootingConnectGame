using System;

namespace ShootingGame.Shared.Simulation
{
    /// <summary>
    /// Monotonically increasing state revision number for server-authoritative state tracking.
    /// Clients include their BaseRevision when sending operations; the server rejects
    /// operations based on stale state.
    ///
    /// This prevents clients from making decisions based on outdated game state
    /// (e.g., firing at a position where the target USED to be).
    /// </summary>
    [Serializable]
    public struct StateRevision
    {
        /// <summary>Current revision number. Incremented on each authoritative state change.</summary>
        public uint Value;

        /// <summary>Increment the revision after a state mutation.</summary>
        public void Increment()
        {
            unchecked { Value++; }
        }

        /// <summary>Check if a client operation is based on current state.</summary>
        public bool IsCurrent(uint clientBaseRevision)
        {
            return clientBaseRevision == Value;
        }

        /// <summary>
        /// Check if client operati...stale.
        /// Returns true if the client's base revision doesn't match (stale).
        /// </summary>
        public bool IsStale(uint clientBaseRevision)
        {
            return clientBaseRevision != Value;
        }

        public void Reset()
        {
            Value = 0;
        }

        public override string ToString() => $"Rev={Value}";
    }
}
