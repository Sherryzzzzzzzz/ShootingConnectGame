using ShootingGame.Shared.ECS;
using ShootingGame.Shared.GameplayTags;

namespace ShootingGame.Shared.Ability
{
    /// <summary>
    /// 能力标签检查系统：验证 RequiredTags/BlockedByTags，自动取消被 CancelledByTags 影响的能力。
    /// </summary>
    public static class AbilityTagCheckSystem
    {
        /// <summary>
        /// 检查能力是否可以激活（标签条件）。
        /// </summary>
        public static bool CanActivate(EntityManager em, Entity entity, AbilityConfig config)
        {
            if (!em.HasComponent<TagComponent>(entity)) return true;
            var tags = em.GetComponent<TagComponent>(entity);

            if (config.RequiredTags != 0 && !tags.Tags.HasAll(config.RequiredTags))
                return false;

            if (config.BlockedByTags != 0 && tags.Tags.HasAny(config.BlockedByTags))
                return false;

            return true;
        }

        /// <summary>
        /// 检查并自动取消被 CancelledByTags 影响的活跃能力。
        /// 返回被取消的能力数量。
        /// </summary>
        public static int AutoCancelBlocked(EntityManager em, Entity entity)
        {
            if (!em.HasComponent<TagComponent>(entity)) return 0;
            if (!em.HasComponent<AbilityInstanceComponent>(entity)) return 0;
            if (!em.HasComponent<AbilityOwnerComponent>(entity)) return 0;

            var tags = em.GetComponent<TagComponent>(entity);
            var comp = em.GetComponent<AbilityInstanceComponent>(entity);
            var owner = em.GetComponent<AbilityOwnerComponent>(entity);
            int cancelled = 0;

            for (int i = 0; i < 4; i++)
            {
                var slot = comp.GetSlot(i);
                if (slot.State != AbilityState.Active && slot.State != AbilityState.Predicting)
                    continue;

                var config = owner.GetConfig(slot.AssetId);
                if (config == null) continue;

                if (config.CancelledByTags != 0 && tags.Tags.HasAny(config.CancelledByTags))
                {
                    slot.State = AbilityState.Cancelled;
                    comp.SetSlot(i, slot);
                    cancelled++;
                }
            }

            if (cancelled > 0)
                em.SetComponent(entity, comp);

            return cancelled;
        }

        /// <summary>
        /// 检查指定 AssetId 的活跃能力是否应被取消。
        /// </summary>
        public static bool ShouldCancel(EntityManager em, Entity entity, byte assetId)
        {
            if (!em.HasComponent<TagComponent>(entity)) return false;
            if (!em.HasComponent<AbilityOwnerComponent>(entity)) return false;

            var tags = em.GetComponent<TagComponent>(entity);
            var owner = em.GetComponent<AbilityOwnerComponent>(entity);
            var config = owner.GetConfig(assetId);
            if (config == null) return false;

            return config.CancelledByTags != 0 && tags.Tags.HasAny(config.CancelledByTags);
        }
    }
}
