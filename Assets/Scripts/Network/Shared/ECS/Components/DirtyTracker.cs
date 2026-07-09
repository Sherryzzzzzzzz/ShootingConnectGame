using System;

namespace ShootingGame.Shared.ECS.Components
{
    /// <summary>
    /// Tracks which properties within an ECS Component have changed since the last network sync.
    /// Uses a 64-bit mask for up to 64 properties per component — all O(1) operations.
    ///
    /// Usage (inside a component struct):
    ///   DirtyTracker _dirty;
    ///   public Vec3 Velocity { get => _velocity; set { _velocity = value; _dirty.MarkDirty(1); } }
    /// </summary>
    public struct DirtyTracker
    {
        private ulong _dirtyMask;

        /// <summary>Whether any property is dirty.</summary>
        public bool HasDirty => _dirtyMask != 0;

        /// <summary>The dirty bitmask (for serialization).</summary>
        public ulong DirtyMask => _dirtyMask;

        /// <summary>Mark a specific property as changed.</summary>
        public void MarkDirty(int propertyIndex)
        {
            _dirtyMask |= 1UL << propertyIndex;
        }

        /// <summary>Check if a specific property is dirty.</summary>
        public bool IsDirty(int propertyIndex)
        {
            return (_dirtyMask & (1UL << propertyIndex)) != 0;
        }

        /// <summary>Reset all dirty flags (call after serialization).</summary>
        public void ResetAll()
        {
            _dirtyMask = 0;
        }

        /// <summary>Reset specific dirty flag.</summary>
        public void ResetDirty(int propertyIndex)
        {
            _dirtyMask &= ~(1UL << propertyIndex);
        }
    }
}
