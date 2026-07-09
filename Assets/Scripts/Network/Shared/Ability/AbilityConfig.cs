using System;

namespace ShootingGame.Shared.Ability
{
    /// <summary>
    /// 能力配置：定义一种能力的所有参数。
    /// 在共享库中用纯数据类表示，Unity 端用 ScriptableObject 表示。
    /// </summary>
    public class AbilityConfig
    {
        /// <summary>唯一 AssetId（用于网络同步）</summary>
        public byte AssetId;

        /// <summary>显示名称</summary>
        public string Name;

        /// <summary>冷却时间（秒）</summary>
        public float Cooldown;

        /// <summary>持续时间（秒）。0 表示瞬时能力。</summary>
        public float Duration;

        /// <summary>激活所需的标签（位掩码）。必须全部满足。</summary>
        public long RequiredTags;

        /// <summary>阻止激活的标签（位掩码）。任意一个存在则不能激活。</summary>
        public long BlockedByTags;

        /// <summary>自动取消的标签（位掩码）。激活后若任意一个出现，强制取消。</summary>
        public long CancelledByTags;

        /// <summary>激活时添加的标签（位掩码）。</summary>
        public long AppliedTags;

        /// <summary>激活时移除的标签（位掩码）。</summary>
        public long RemovedTags;

        /// <summary>行为类型全名（用于反射创建 IAbilityBehavior）。</summary>
        public string BehaviorTypeName;

        public bool IsInstant => Duration <= 0f;

        /// <summary>
        /// 创建默认配置（Fire/Reload/Jump/Sprint 预设）。
        /// </summary>
        public static AbilityConfig[] CreateDefaults()
        {
            return new AbilityConfig[]
            {
                // Fire: requires alive, not reloading/stunned/dead
                new AbilityConfig
                {
                    AssetId = 1, Name = "Fire", Cooldown = 0.15f, Duration = 0f,
                    RequiredTags = 0, // checked in behavior
                    BlockedByTags = 0, // set at runtime
                    AppliedTags = 0, // set at runtime from tag config
                    RemovedTags = 0,
                    BehaviorTypeName = "ShootingGame.Shared.Ability.Abilities.FireWeaponAbility",
                },
                // Reload: requires alive, not reloading/stunned/dead, ammo not full
                new AbilityConfig
                {
                    AssetId = 2, Name = "Reload", Cooldown = 0f, Duration = 2.0f,
                    RequiredTags = 0,
                    BlockedByTags = 0,
                    AppliedTags = 0,
                    RemovedTags = 0,
                    BehaviorTypeName = "ShootingGame.Shared.Ability.Abilities.ReloadWeaponAbility",
                },
                // Jump: requires grounded, alive, not stunned
                new AbilityConfig
                {
                    AssetId = 3, Name = "Jump", Cooldown = 0f, Duration = 0f,
                    RequiredTags = 0,
                    BlockedByTags = 0,
                    AppliedTags = 0,
                    RemovedTags = 0,
                    BehaviorTypeName = "ShootingGame.Shared.Ability.Abilities.JumpAbility",
                },
                // Sprint: requires alive, grounded, not aiming
                new AbilityConfig
                {
                    AssetId = 4, Name = "Sprint", Cooldown = 0f, Duration = 0f,
                    RequiredTags = 0,
                    BlockedByTags = 0,
                    AppliedTags = 0,
                    RemovedTags = 0,
                    BehaviorTypeName = "ShootingGame.Shared.Ability.Abilities.SprintAbility",
                },
            };
        }
    }
}
