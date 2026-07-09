using ShootingGame.Shared.ECS;
using ShootingGame.Shared.Simulation;

namespace ShootingGame.Shared.Ability.Abilities
{
    /// <summary>
    /// 换弹能力：等待换弹时间后填满弹药。
    /// Duration 到期时由 AbilityLifecycleSystem.Tick 调用 OnDeactivate。
    /// </summary>
    public class ReloadWeaponAbility : IAbilityBehavior
    {
        public bool CanActivate(EntityManager em, Entity entity, AbilityConfig config)
        {
            if (!em.HasComponent<AmmoComponent>(entity)) return false;
            var ammo = em.GetComponent<AmmoComponent>(entity);
            return !ammo.IsFull;
        }

        public void OnActivate(EntityManager em, Entity entity, AbilityConfig config) { }

        public void OnUpdate(EntityManager em, Entity entity, AbilityConfig config, float dt) { }

        public void OnDeactivate(EntityManager em, Entity entity, AbilityConfig config)
        {
            if (!em.HasComponent<AmmoComponent>(entity)) return;
            var ammo = em.GetComponent<AmmoComponent>(entity);
            ammo.Current = ammo.Max;
            em.SetComponent(entity, ammo);
        }

        public void OnCancel(EntityManager em, Entity entity, AbilityConfig config) { }
    }
}
