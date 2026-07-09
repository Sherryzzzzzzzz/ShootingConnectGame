using ShootingGame.Shared.Simulation;

namespace ShootingGame.Shared.ECS
{
    /// <summary>
    /// 开火冷却系统：每 tick 减少冷却时间。
    /// </summary>
    public static class FireCooldownSystem
    {
        public static void Execute(EntityManager em, Entity entity, float dt)
        {
            if (!em.HasComponent<FireCooldownComponent>(entity)) return;

            var cooldown = em.GetComponent<FireCooldownComponent>(entity);
            if (cooldown.Cooldown > 0f)
            {
                cooldown.Cooldown -= dt;
                em.SetComponent(entity, cooldown);
            }
        }
    }
}
