using System.Collections.Generic;
using ShootingGame.Shared.Ability;

namespace ShootingGame.Shared.ECS
{
    /// <summary>
    /// 能力拥有者组件：记录该实体被授予的能力配置列表。
    /// </summary>
    public struct AbilityOwnerComponent
    {
        /// <summary>已授予的能力 AssetId 列表。</summary>
        public List<AbilityConfig> GrantedAbilities;

        /// <summary>已授予的能力 AssetId 位掩码（快速查找）。</summary>
        public long GrantedMask;

        public bool HasAbility(byte assetId) => (GrantedMask & (1L << assetId)) != 0;

        public AbilityConfig GetConfig(byte assetId)
        {
            if (GrantedAbilities == null) return null;
            foreach (var cfg in GrantedAbilities)
                if (cfg.AssetId == assetId) return cfg;
            return null;
        }
    }
}
