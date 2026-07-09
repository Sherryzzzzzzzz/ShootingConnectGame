namespace ShootingGame.Shared.Network
{
    /// <summary>
    /// Interface for network-syncable properties within an ECS Component.
    /// Each property has a unique index within its component for dirty tracking.
    /// </summary>
    public interface INetworkProperty
    {
        /// <summary>Whether this property has changed since last sync.</summary>
        bool IsDirty { get; }

        /// <summary>Reset the dirty flag after serialization.</summary>
        void ResetDirty();

        /// <summary>Unique index within the owning component (0-63).</summary>
        int PropertyIndex { get; }
    }
}
