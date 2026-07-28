using UnityEngine;

namespace ShootingGame.Shared.Ability
{
    /// <summary>
    /// 技能配置 ScriptableObject。在 Assets 右键 → Create → ShootingGame → Ability Config 创建。
    /// 存放在 Resources/Abilities/ 下，运行时由 HeroRegistry 按 AssetId 加载。
    /// 服务器侧使用同一份数据（经 GameConfigExporter 导出 abilities.json）。
    /// </summary>
    [CreateAssetMenu(menuName = "ShootingGame/Ability Config", fileName = "AbilityConfig")]
    public class AbilityConfigSO : ScriptableObject
    {
        [Header("基础")]
        [Tooltip("唯一 AssetId（用于网络同步与英雄引用），1-255")]
        public int AssetId = 1;
        public string AbilityName = "New Ability";

        [Header("时间")]
        public float Cooldown = 0f;
        [Tooltip("持续时间（秒）。0 表示瞬时技能")]
        public float Duration = 0f;

        [Header("行为")]
        [Tooltip("行为类全名，如 ShootingGame.Shared.Ability.Abilities.DashAbility")]
        public string BehaviorTypeName;

        [Header("标签约束（位掩码，0=不约束）")]
        public long RequiredTags;
        public long BlockedByTags;
        public long CancelledByTags;
        public long AppliedTags;
        public long RemovedTags;

        public AbilityConfig ToAbilityConfig()
        {
            return new AbilityConfig
            {
                AssetId = (byte)Mathf.Clamp(AssetId, 0, 255),
                Name = AbilityName,
                Cooldown = Cooldown,
                Duration = Duration,
                RequiredTags = RequiredTags,
                BlockedByTags = BlockedByTags,
                CancelledByTags = CancelledByTags,
                AppliedTags = AppliedTags,
                RemovedTags = RemovedTags,
                BehaviorTypeName = BehaviorTypeName,
            };
        }
    }
}
