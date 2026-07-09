using ShootingGame.Shared.ECS;

namespace ShootingGame.Shared.Ability.Abilities
{
    /// <summary>
    /// MarkShot ability: applies Buff.DamageBoost tag to amplify next shot.
    /// Tag application/removal handled by AbilityLifecycleSystem via config.
    /// </summary>
    public class MarkShotAbility : IAbilityBehavior
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
