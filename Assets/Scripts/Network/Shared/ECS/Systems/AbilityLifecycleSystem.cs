using System;
using System.Collections.Generic;
using ShootingGame.Shared.ECS;
using ShootingGame.Shared.GameplayTags;

namespace ShootingGame.Shared.Ability
{
    /// <summary>
    /// 能力生命周期系统：驱动能力状态机，管理标签应用/移除，调度 IAbilityBehavior。
    ///
    /// 状态转换：
    ///   Inactive → Predicting (客户端预测激活)
    ///   Inactive → Active    (服务端权威激活)
    ///   Predicting → Active  (服务端确认)
    ///   Predicting → Cancelled (服务端拒绝)
    ///   Active → Ending      (持续时间到期，由 AbilityCooldownSystem 触发)
    ///   Ending → Inactive    (正常结束，Tick 中自动处理)
    ///   Active → Cancelled   (被打断或标签取消)
    ///   Cancelled → Inactive (CleanupFinished 清理)
    /// </summary>
    public static class AbilityLifecycleSystem
    {
        private static readonly Dictionary<string, IAbilityBehavior> BehaviorCache = new();

        /// <summary>
        /// 请求激活能力。返回 InstanceId（0 表示失败）。
        /// isPredicting: true=客户端预测, false=服务端权威激活。
        /// </summary>
        public static ushort RequestActivate(EntityManager em, Entity entity, byte assetId, bool isPredicting)
        {
            if (!em.HasComponent<AbilityOwnerComponent>(entity)) return 0;
            if (!em.HasComponent<AbilityInstanceComponent>(entity)) return 0;

            var owner = em.GetComponent<AbilityOwnerComponent>(entity);
            var config = owner.GetConfig(assetId);
            if (config == null) return 0;

            if (!AbilityTagCheckSystem.CanActivate(em, entity, config)) return 0;

            var behavior = GetBehavior(config.BehaviorTypeName);
            if (behavior != null && !behavior.CanActivate(em, entity, config)) return 0;

            var comp = em.GetComponent<AbilityInstanceComponent>(entity);

            if (comp.HasActive(assetId)) return 0;

            int slotIndex = comp.FindFreeSlot();
            if (slotIndex < 0) return 0;

            ushort instanceId = AbilityInstanceComponent.NextInstanceId();
            var data = AbilityInstanceData.Create(assetId, instanceId, config.Cooldown, config.Duration);
            data.State = isPredicting ? AbilityState.Predicting : AbilityState.Active;
            data.AppliedTagsMask = config.AppliedTags;

            comp.SetSlot(slotIndex, data);
            comp.ActiveCount++;
            em.SetComponent(entity, comp);

            ApplyTags(em, entity, config);
            behavior?.OnActivate(em, entity, config);

            return instanceId;
        }

        /// <summary>
        /// 确认预测激活（服务端认可）。
        /// </summary>
        public static bool ConfirmActivate(EntityManager em, Entity entity, ushort instanceId)
        {
            if (!em.HasComponent<AbilityInstanceComponent>(entity)) return false;

            var comp = em.GetComponent<AbilityInstanceComponent>(entity);
            for (int i = 0; i < 4; i++)
            {
                var slot = comp.GetSlot(i);
                if (slot.InstanceId == instanceId && slot.State == AbilityState.Predicting)
                {
                    slot.State = AbilityState.Active;
                    comp.SetSlot(i, slot);
                    em.SetComponent(entity, comp);

                    if (em.HasComponent<TagComponent>(entity))
                    {
                        var tags = em.GetComponent<TagComponent>(entity);
                        tags.Tags.ConfirmPrediction();
                        em.SetComponent(entity, tags);
                    }
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 拒绝预测激活（服务端否定）。回滚标签并调用 OnCancel。
        /// </summary>
        public static bool RejectActivate(EntityManager em, Entity entity, ushort instanceId)
        {
            if (!em.HasComponent<AbilityInstanceComponent>(entity)) return false;
            if (!em.HasComponent<AbilityOwnerComponent>(entity)) return false;

            var comp = em.GetComponent<AbilityInstanceComponent>(entity);
            var owner = em.GetComponent<AbilityOwnerComponent>(entity);

            for (int i = 0; i < 4; i++)
            {
                var slot = comp.GetSlot(i);
                if (slot.InstanceId == instanceId && slot.State == AbilityState.Predicting)
                {
                    var config = owner.GetConfig(slot.AssetId);
                    var behavior = config != null ? GetBehavior(config.BehaviorTypeName) : null;
                    behavior?.OnCancel(em, entity, config);

                    ReverseTags(em, entity, config);

                    if (em.HasComponent<TagComponent>(entity))
                    {
                        var tags = em.GetComponent<TagComponent>(entity);
                        tags.Tags.RejectPrediction();
                        em.SetComponent(entity, tags);
                    }

                    slot.State = AbilityState.Cancelled;
                    comp.SetSlot(i, slot);
                    comp.CleanupFinished();
                    em.SetComponent(entity, comp);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 正常结束能力（Deactivate）。
        /// </summary>
        public static bool Deactivate(EntityManager em, Entity entity, ushort instanceId)
        {
            return EndAbility(em, entity, instanceId, isCancel: false);
        }

        /// <summary>
        /// 强制取消能力。
        /// </summary>
        public static bool Cancel(EntityManager em, Entity entity, ushort instanceId)
        {
            return EndAbility(em, entity, instanceId, isCancel: true);
        }

        private static bool EndAbility(EntityManager em, Entity entity, ushort instanceId, bool isCancel)
        {
            if (!em.HasComponent<AbilityInstanceComponent>(entity)) return false;
            if (!em.HasComponent<AbilityOwnerComponent>(entity)) return false;

            var comp = em.GetComponent<AbilityInstanceComponent>(entity);
            var owner = em.GetComponent<AbilityOwnerComponent>(entity);

            for (int i = 0; i < 4; i++)
            {
                var slot = comp.GetSlot(i);
                if (slot.InstanceId == instanceId && slot.IsActive)
                {
                    var config = owner.GetConfig(slot.AssetId);
                    var behavior = config != null ? GetBehavior(config.BehaviorTypeName) : null;

                    if (isCancel)
                        behavior?.OnCancel(em, entity, config);
                    else
                        behavior?.OnDeactivate(em, entity, config);

                    ReverseTags(em, entity, config);

                    slot.State = isCancel ? AbilityState.Cancelled : AbilityState.Inactive;
                    comp.SetSlot(i, slot);
                    comp.CleanupFinished();
                    em.SetComponent(entity, comp);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 每 tick 更新：驱动 Ending→Inactive，调用 Active/Predicting 的 OnUpdate。
        /// </summary>
        public static void Tick(EntityManager em, Entity entity, float dt)
        {
            if (!em.HasComponent<AbilityInstanceComponent>(entity)) return;
            if (!em.HasComponent<AbilityOwnerComponent>(entity)) return;

            var comp = em.GetComponent<AbilityInstanceComponent>(entity);
            var owner = em.GetComponent<AbilityOwnerComponent>(entity);

            for (int i = 0; i < 4; i++)
            {
                var slot = comp.GetSlot(i);
                if (slot.State == AbilityState.Inactive || slot.State == AbilityState.Cancelled)
                    continue;

                var config = owner.GetConfig(slot.AssetId);
                var behavior = config != null ? GetBehavior(config.BehaviorTypeName) : null;

                if (slot.State == AbilityState.Ending)
                {
                    behavior?.OnDeactivate(em, entity, config);
                    ReverseTags(em, entity, config);

                    // 重新添加冷却
                    if (config != null)
                        slot.CooldownRemaining = config.Cooldown;

                    slot.State = AbilityState.Inactive;
                    comp.SetSlot(i, slot);
                }
                else if (slot.State == AbilityState.Active || slot.State == AbilityState.Predicting)
                {
                    behavior?.OnUpdate(em, entity, config, dt);
                }
            }

            comp.CleanupFinished();
            em.SetComponent(entity, comp);
        }

        private static void ApplyTags(EntityManager em, Entity entity, AbilityConfig config)
        {
            if (!em.HasComponent<TagComponent>(entity)) return;
            if (config.AppliedTags == 0 && config.RemovedTags == 0) return;

            var tags = em.GetComponent<TagComponent>(entity);
            tags.TagBitMask |= config.AppliedTags;
            tags.TagBitMask &= ~config.RemovedTags;
            em.SetComponent(entity, tags);
        }

        private static void ReverseTags(EntityManager em, Entity entity, AbilityConfig config)
        {
            if (!em.HasComponent<TagComponent>(entity)) return;
            if (config == null) return;
            if (config.AppliedTags == 0 && config.RemovedTags == 0) return;

            var tags = em.GetComponent<TagComponent>(entity);
            tags.TagBitMask &= ~config.AppliedTags;
            tags.TagBitMask |= config.RemovedTags;
            em.SetComponent(entity, tags);
        }

        public static IAbilityBehavior GetBehavior(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return null;

            if (BehaviorCache.TryGetValue(typeName, out var cached))
                return cached;

            var type = Type.GetType(typeName);
            if (type == null) return null;

            var behavior = (IAbilityBehavior)Activator.CreateInstance(type);
            BehaviorCache[typeName] = behavior;
            return behavior;
        }
    }
}
