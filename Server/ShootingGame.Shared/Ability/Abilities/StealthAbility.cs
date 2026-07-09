using ShootingGame.Shared.ECS;

namespace ShootingGame.Shared.Ability.Abilities
{
    /// <summary>
    /// Stealth ability: applies Buff.Invisible tag.
    /// Tag application/removal handled by AbilityLifecycleSystem via config.
    /// </summary>
    public class StealthAbility : IAbilityBehavior
    {
        public bool CanActivate(EntityManager em, Entity entity, AbilityConfig config)
        {
            if (!em.HasComponent<HealthComponent>(entity)) return false;
            var hp = em.GetComponent<HealthComponent>(entity);
            return hp.IsAlive;
        }

        public void OnActivate(EntityManager em, Entity entity, AbilityConfig config) { }
        public void OnUpdate(EntityManager em, Entity entity, AbilityConfig config, float dt) { }
        public void OnDeactivate(EntityManager em, Entity entity, AbilityConfig config) { }
        public void OnCancel(EntityManager em, Entity entity, AbilityConfig config) { }
    }
}
