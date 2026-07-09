using ShootingGame.Shared.Simulation;

namespace ShootingGame.Shared.ECS
{
    /// <summary>
    /// 换弹系统：管理换弹计时和弹药补充。
    /// </summary>
    public static class ReloadSystem
    {
        public static void Execute(EntityManager em, Entity entity, float dt)
        {
            if (!em.HasComponent<ReloadComponent>(entity)) return;
            if (!em.HasComponent<AmmoComponent>(entity)) return;
            if (!em.HasComponent<InputComponent>(entity)) return;

            var reload = em.GetComponent<ReloadComponent>(entity);
            var ammo = em.GetComponent<AmmoComponent>(entity);
            var input = em.GetComponent<InputComponent>(entity);

            if (reload.IsReloading)
            {
                reload.Timer -= dt;
                if (reload.Timer <= 0f)
                {
                    ammo.Current = GameConstants.MaxAmmoPerClip;
                    reload.IsReloading = false;
                    reload.Timer = 0f;
                }
            }
            else if (input.Reload && !ammo.IsFull)
            {
                reload.IsReloading = true;
                reload.Timer = GameConstants.ReloadTime;
            }

            em.SetComponent(entity, reload);
            em.SetComponent(entity, ammo);
        }
    }
}
