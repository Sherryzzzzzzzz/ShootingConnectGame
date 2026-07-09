using System;

namespace ShootingGame.Shared.GameplayTags
{
    /// <summary>
    /// 层级 GameplayTag 标识符。每个标签有唯一 ID 和 64-bit 掩码中的一个位。
    /// </summary>
    public readonly struct GameplayTag : IEquatable<GameplayTag>
    {
        public readonly int Id;

        public GameplayTag(int id)
        {
            Id = id;
        }

        public bool IsValid => Id >= 0 && Id < GameplayTagManager.MaxTags;
        public long SelfMask => GameplayTagManager.GetSelfMask(Id);
        public long DescendantMask => GameplayTagManager.GetDescendantMask(Id);
        public string Name => GameplayTagManager.GetName(Id);

        /// <summary>
        /// 检查给定的位掩码是否包含此标签或其任意子孙标签。
        /// </summary>
        public bool Matches(long tagMask) => (tagMask & DescendantMask) != 0;

        /// <summary>
        /// 检查给定的位掩码是否精确包含此标签（不含子孙）。
        /// </summary>
        public bool MatchesExact(long tagMask) => (tagMask & SelfMask) != 0;

        public static GameplayTag Invalid => default;

        public static GameplayTag FromName(string name) => new GameplayTag(GameplayTagManager.GetId(name));

        public bool Equals(GameplayTag other) => Id == other.Id;
        public override bool Equals(object obj) => obj is GameplayTag other && Equals(other);
        public override int GetHashCode() => Id;
        public static bool operator ==(GameplayTag a, GameplayTag b) => a.Id == b.Id;
        public static bool operator !=(GameplayTag a, GameplayTag b) => a.Id != b.Id;
        public override string ToString() => IsValid ? Name : "Invalid";
    }
}
