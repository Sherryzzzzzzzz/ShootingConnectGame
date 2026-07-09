using ShootingGame.Shared.ECS;
using ShootingGame.Shared.Math;
using ShootingGame.Shared.Simulation;

namespace ShootingGame.Shared.Ability.Abilities
{
    /// <summary>
    /// 开火能力：消耗弹药，生成子弹实体。
    /// </summary>
    public class FireWeaponAbility : IAbilityBehavior
    {
        public bool CanActivate(EntityManager em, Entity entity, AbilityConfig config)
        {
            if (!em.HasComponent<AmmoComponent>(entity)) return false;
            var ammo = em.GetComponent<AmmoComponent>(entity);
            return !ammo.IsEmpty;
        }

        public void OnActivate(EntityManager em, Entity entity, AbilityConfig config)
        {
            if (!em.HasComponent<AmmoComponent>(entity)) return;

            var ammo = em.GetComponent<AmmoComponent>(entity);
            ammo.Current--;
            em.SetComponent(entity, ammo);

            SpawnBullet(em, entity);
        }

        public void OnUpdate(EntityManager em, Entity entity, AbilityConfig config, float dt) { }

        public void OnDeactivate(EntityManager em, Entity entity, AbilityConfig config) { }

        public void OnCancel(EntityManager em, Entity entity, AbilityConfig config) { }

        private static void SpawnBullet(EntityManager em, Entity ownerEntity)
        {
            if (!em.HasComponent<TransformComponent>(ownerEntity)) return;

            var transform = em.GetComponent<TransformComponent>(ownerEntity);
            var forward = transform.Rotation.Rotate(Vec3.Forward);

            // 子弹从玩家位置前方 1m 处生成
            var spawnPos = new Vec3(
                transform.Position.x + forward.x * 1.0f,
                transform.Position.y + 0.8f,
                transform.Position.z + forward.z * 1.0f
            );

            var bulletEntity = em.CreateEntity();
            em.AddComponent(bulletEntity, new TransformComponent(spawnPos, transform.Rotation));
            em.AddComponent(bulletEntity, new BulletComponent
            {
                AttackId = 0,
                OwnerId = ownerEntity.Id,
                OwnerTeamId = 0,
                Direction = forward,
                Speed = 80f,
                MaxDistance = GameConstants.HitscanRange,
                TraveledDistance = 0f,
                Damage = GameConstants.HitscanDamage,
                SpawnFrameId = 0,
            });
        }
    }
}
