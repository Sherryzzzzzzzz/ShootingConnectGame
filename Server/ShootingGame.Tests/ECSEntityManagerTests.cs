using ShootingGame.Shared.ECS;
using ShootingGame.Shared.Math;
using Xunit;

namespace ShootingGame.Tests
{
    public class ECSEntityManagerTests
    {
        [Fact]
        public void CreateEntity_ReturnsValidEntity()
        {
            var em = new EntityManager();
            var entity = em.CreateEntity();

            Assert.True(em.IsValid(entity));
            Assert.True(entity.Id >= 0);
        }

        [Fact]
        public void CreateEntity_UniqueIds()
        {
            var em = new EntityManager();
            var e1 = em.CreateEntity();
            var e2 = em.CreateEntity();

            Assert.NotEqual(e1.Id, e2.Id);
        }

        [Fact]
        public void DestroyEntity_MarksInvalid()
        {
            var em = new EntityManager();
            var entity = em.CreateEntity();

            em.DestroyEntity(entity);

            Assert.False(em.IsValid(entity));
        }

        [Fact]
        public void DestroyAndCreate_ReusesSlot_WithDifferentGeneration()
        {
            var em = new EntityManager();
            var e1 = em.CreateEntity();
            int id1 = e1.Id;
            int gen1 = e1.Generation;

            em.DestroyEntity(e1);

            var e2 = em.CreateEntity();
            int id2 = e2.Id;
            int gen2 = e2.Generation;

            // Same slot reused
            Assert.Equal(id1, id2);
            // Generation incremented
            Assert.NotEqual(gen1, gen2);
        }

        [Fact]
        public void OldEntity_InvalidAfterReuse()
        {
            var em = new EntityManager();
            var e1 = em.CreateEntity();

            em.DestroyEntity(e1);
            var e2 = em.CreateEntity(); // reuses slot

            // Old entity is now invalid
            Assert.False(em.IsValid(e1));
            // New entity is valid
            Assert.True(em.IsValid(e2));
        }

        [Fact]
        public void AddComponent_SetsHasComponent()
        {
            var em = new EntityManager();
            var entity = em.CreateEntity();

            em.AddComponent<HealthComponent>(entity);

            Assert.True(em.HasComponent<HealthComponent>(entity));
        }

        [Fact]
        public void AddComponent_WithInitialValue()
        {
            var em = new EntityManager();
            var entity = em.CreateEntity();

            em.AddComponent(entity, new HealthComponent(50, 100));

            var hc = em.GetComponent<HealthComponent>(entity);
            Assert.Equal(50, hc.Current);
            Assert.Equal(100, hc.Max);
        }

        [Fact]
        public void GetComponent_ReturnsRef()
        {
            var em = new EntityManager();
            var entity = em.CreateEntity();
            em.AddComponent(entity, new MovementComponent(Vec3.Zero, 0f, true));

            ref var mv = ref em.GetComponent<MovementComponent>(entity);
            mv.VerticalVelocity = 10f;

            var mv2 = em.GetComponent<MovementComponent>(entity);
            Assert.Equal(10f, mv2.VerticalVelocity);
        }

        [Fact]
        public void SetComponent_AutoAdds_IfMissing()
        {
            var em = new EntityManager();
            var entity = em.CreateEntity();

            em.SetComponent(entity, new AmmoComponent(20, 30));

            Assert.True(em.HasComponent<AmmoComponent>(entity));
            var ammo = em.GetComponent<AmmoComponent>(entity);
            Assert.Equal(20, ammo.Current);
        }

        [Fact]
        public void RemoveComponent_ClearsMask()
        {
            var em = new EntityManager();
            var entity = em.CreateEntity();
            em.AddComponent<HealthComponent>(entity);

            em.RemoveComponent<HealthComponent>(entity);

            Assert.False(em.HasComponent<HealthComponent>(entity));
        }

        [Fact]
        public void GetEntitiesWith_FindsMatches()
        {
            var em = new EntityManager();
            var e1 = em.CreateEntity();
            var e2 = em.CreateEntity();
            em.AddComponent<HealthComponent>(e1);
            em.AddComponent<HealthComponent>(e2);

            var list = new System.Collections.Generic.List<Entity>();
            em.GetEntitiesWith<HealthComponent>(list);

            Assert.Equal(2, list.Count);
            Assert.Contains(e1, list);
            Assert.Contains(e2, list);
        }

        [Fact]
        public void GetEntitiesWith_IgnoresDestroyed()
        {
            var em = new EntityManager();
            var e1 = em.CreateEntity();
            var e2 = em.CreateEntity();
            em.AddComponent<HealthComponent>(e1);
            em.AddComponent<HealthComponent>(e2);
            em.DestroyEntity(e2);

            var list = new System.Collections.Generic.List<Entity>();
            em.GetEntitiesWith<HealthComponent>(list);

            Assert.Single(list);
        }

        [Fact]
        public void Clear_RemovesAllEntities()
        {
            var em = new EntityManager();
            var e1 = em.CreateEntity();
            var e2 = em.CreateEntity();
            em.AddComponent<HealthComponent>(e1);

            em.Clear();

            Assert.False(em.IsValid(e1));
            Assert.False(em.IsValid(e2));
            Assert.Equal(0, em.ActiveEntityCount);
        }

        [Fact]
        public void ActiveEntityCount_TracksCorrectly()
        {
            var em = new EntityManager();
            Assert.Equal(0, em.ActiveEntityCount);

            em.CreateEntity();
            Assert.Equal(1, em.ActiveEntityCount);

            em.CreateEntity();
            Assert.Equal(2, em.ActiveEntityCount);
        }

        [Fact]
        public void TryGetComponent_ReturnsFalse_WhenMissing()
        {
            var em = new EntityManager();
            var entity = em.CreateEntity();

            bool found = em.TryGetComponent<HealthComponent>(entity, out var hc);

            Assert.False(found);
            Assert.Equal(default, hc);
        }

        [Fact]
        public void TryGetComponent_ReturnsValue_WhenPresent()
        {
            var em = new EntityManager();
            var entity = em.CreateEntity();
            em.AddComponent(entity, new AmmoComponent(15, 30));

            bool found = em.TryGetComponent<AmmoComponent>(entity, out var ammo);

            Assert.True(found);
            Assert.Equal(15, ammo.Current);
        }

        [Fact]
        public void ComponentTypeId_UniquePerType()
        {
            int id1 = ComponentTypeId.Get<HealthComponent>();
            int id2 = ComponentTypeId.Get<AmmoComponent>();
            int id3 = ComponentTypeId.Get<TransformComponent>();

            Assert.NotEqual(id1, id2);
            Assert.NotEqual(id2, id3);
            Assert.NotEqual(id1, id3);
        }

        [Fact]
        public void ComponentMask_DifferentPerType()
        {
            long m1 = ComponentTypeId.Mask<HealthComponent>();
            long m2 = ComponentTypeId.Mask<AmmoComponent>();

            Assert.NotEqual(m1, m2);
            Assert.Equal(0, m1 & m2); // non-overlapping
        }

        [Fact]
        public void Entity_Equality()
        {
            var em = new EntityManager();
            var e1 = em.CreateEntity();
            var e2 = new Entity(e1.Id, e1.Generation);

            Assert.Equal(e1, e2);
            Assert.True(e1 == e2);
        }

        [Fact]
        public void Entity_Inequality()
        {
            var em = new EntityManager();
            var e1 = em.CreateEntity();
            var e2 = em.CreateEntity();

            Assert.NotEqual(e1, e2);
            Assert.True(e1 != e2);
        }
    }
}
