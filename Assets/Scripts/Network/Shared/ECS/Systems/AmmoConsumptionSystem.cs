using ShootingGame.Shared.Simulation;

namespace ShootingGame.Shared.ECS
{
    /// <summary>
    /// 弹药消耗系统：开火时消耗弹药并设置冷却。
    /// </summary>
    public static class AmmoConsumptionSystem
    {
        public static void Execute(EntityManager em, Entity entity)
        {
            if (!em.HasComponent<InputComponent>(entity)) return;
            if (!em.HasComponent<AmmoComponent>(entity)) return;
            if (!em.HasComponent<ReloadComponent>(entity)) return;
            if (!em.HasComponent<FireCooldownComponent>(entity)) return;

            var input = em.GetComponent<InputComponent>(entity);
            var ammo = em.GetComponent<AmmoComponent>(entity);
            var reload = em.GetComponent<ReloadComponent>(entity);
            var cooldown = em.GetComponent<FireCooldownComponent>(entity);

            if (input.Fire && cooldown.Cooldown <= 0f && !reload.IsReloading && ammo.Current > 0)
            {
                cooldown.Cooldown = GameConstants.FireRate;
                ammo.Current--;
                em.SetComponent(entity, cooldown);
                em.SetComponent(entity, ammo);
            }
        }
    }
}
