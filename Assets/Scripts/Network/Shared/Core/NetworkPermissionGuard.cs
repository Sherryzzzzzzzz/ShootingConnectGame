using System;
using ShootingGame.Shared.ECS;
using ShootingGame.Shared.ECS.Components;

namespace ShootingGame.Shared.Network
{
    /// <summary>
    /// Lightweight permission guard for ECS component writes.
    /// Checks whether a client has permission to modify a component's syncable properties.
    ///
    /// Reference: SpaceBuilder's NetPermission + NetworkBehaviour.HasPermission() pattern,
    /// adapted for SCG's ECS architecture.
    /// </summary>
    public static class NetworkPermissionGuard
    {
        /// <summary>
        /// Check if a client can write to a component.
        /// Server always has permission. Clients are restricted per-component.
        /// </summary>
        /// <param name="isServer">True if executing on server.</param>
        /// <param name="permission">The permission setting for this component.</param>
        /// <param name="clientId">The client attempting the write (0-7).</param>
        public static bool CanWrite(bool isServer, NetPermission permission, byte clientId)
        {
            if (isServer) return true;
            return permission.HasPermission(clientId);
        }

        /// <summary>
        /// Default permissions per component type (generic version).
        /// - HP/Death: ServerOnly (prevent cheat)
        /// - Position/Velocity: Everyone (allow client prediction)
        /// - Ammo/Reload: ServerOnly (authoritative)
        /// - Input: Everyone (client generates input)
        /// </summary>
        public static NetPermission GetDefaultPermission<T>()
        {
            int typeId = ComponentTypeId.Get<T>();
            return GetDefaultPermission(typeId);
        }

        /// <summary>
        /// Default permissions per component type (int version).
        /// </summary>
        public static NetPermission GetDefaultPermission(int componentTypeId)
        {
            // Map component type IDs to their default permissions.
            // IDs are dynamically assigned at runtime, so we compare
            // against the known types at call time.
            if (componentTypeId == ComponentTypeId.Get<HealthComponent>() ||
                componentTypeId == ComponentTypeId.Get<AmmoComponent>() ||
                componentTypeId == ComponentTypeId.Get<FireCooldownComponent>() ||
                componentTypeId == ComponentTypeId.Get<ReloadComponent>() ||
                componentTypeId == ComponentTypeId.Get<PlayerStateComponent>())
                return NetPermission.ServerOnly;

            if (componentTypeId == ComponentTypeId.Get<MovementComponent>() ||
                componentTypeId == ComponentTypeId.Get<TransformComponent>() ||
                componentTypeId == ComponentTypeId.Get<InputComponent>())
                return NetPermission.Everyone;

            return NetPermission.ServerOnly; // Safe default
        }
    }
}
