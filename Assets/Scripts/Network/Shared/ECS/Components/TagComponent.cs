using ShootingGame.Shared.GameplayTags;

namespace ShootingGame.Shared.ECS
{
    /// <summary>
    /// 标签组件：实体的 GameplayTag 容器。
    /// </summary>
    public struct TagComponent
    {
        public TagContainer Tags;

        public TagComponent(TagContainer tags)
        {
            Tags = tags;
        }

        // 便捷属性：直接读写位掩码（兼容旧代码）
        public long TagBitMask
        {
            readonly get => Tags.EffectiveMask;
            set => Tags.EffectiveMask = value;
        }
    }
}
