using System;

namespace ShootingGame.Shared.ECS
{
    /// <summary>
    /// 实体标识符。组合 ID 与 Generation 防止悬空引用。
    /// </summary>
    public readonly struct Entity : IEquatable<Entity>
    {
        public readonly int Id;
        public readonly int Generation;

        public Entity(int id, int generation)
        {
            Id = id;
            Generation = generation;
        }

        public bool IsValid => Id >= 0;
        public static readonly Entity Invalid = new Entity(-1, -1);

        public bool Equals(Entity other) => Id == other.Id && Generation == other.Generation;
        public override bool Equals(object obj) => obj is Entity other && Equals(other);
        public override int GetHashCode() => Id;
        public static bool operator ==(Entity a, Entity b) => a.Id == b.Id && a.Generation == b.Generation;
        public static bool operator !=(Entity a, Entity b) => !(a == b);

        public override string ToString() => IsValid ? $"Entity({Id}:{Generation})" : "Entity(Invalid)";
    }
}
