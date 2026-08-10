namespace ShootingGame.Shared.Ability
{
    /// <summary>
    /// 能力冷却系统：每 tick 减少冷却时间和持续时间。
    /// 持续时间到期的能力自动进入 Ending 状态。
    /// </summary>
    public static class AbilityCooldownSystem
    {
        public static void Execute(ECS.EntityManager em, ECS.Entity entity, float dt)
        {
            if (!em.HasComponent<ECS.AbilityInstanceComponent>(entity)) return;

            var comp = em.GetComponent<ECS.AbilityInstanceComponent>(entity);

            for (int i = 0; i < 4; i++)
            {
                var slot = comp.GetSlot(i);
                if (slot.State == AbilityState.Inactive || slot.State == AbilityState.Cancelled)
                    continue;

                if (slot.CooldownRemaining > 0f)
                {
                    slot.CooldownRemaining -= dt;
                    if (slot.CooldownRemaining < 0f) slot.CooldownRemaining = 0f;
                }

                if (slot.State == AbilityState.Active &&
                    slot.DurationRemaining > 0f)
                {
                    slot.DurationRemaining -= dt;
                    if (slot.DurationRemaining <= 0f)
                    {
                        slot.DurationRemaining = 0f;
                        slot.State = AbilityState.Ending;
                    }
                }

                comp.SetSlot(i, slot);
            }

            em.SetComponent(entity, comp);
        }
    }
}
