namespace ShootingGame.Shared.ECS
{
    /// <summary>
    /// 伤害应用系统：扣除生命值。
    /// </summary>
    public static class DamageApplicationSystem
    {
        public static bool ApplyDamage(EntityManager em, Entity entity, int damage)
        {
            if (!em.HasComponent<HealthComponent>(entity)) return false;

            var health = em.GetComponent<HealthComponent>(entity);
            if (health.IsDead) return false;

            int newHp = health.Current - damage;
            if (newHp < 0) newHp = 0;
            health.Current = (byte)newHp;
            em.SetComponent(entity, health);

            return health.IsDead;
        }
    }
}
