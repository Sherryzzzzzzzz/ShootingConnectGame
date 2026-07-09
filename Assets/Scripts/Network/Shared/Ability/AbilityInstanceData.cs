namespace ShootingGame.Shared.Ability
{
    /// <summary>
    /// 运行时能力实例数据。存储在 AbilityInstanceComponent 中。
    /// </summary>
    public struct AbilityInstanceData
    {
        /// <summary>此实例的唯一 ID（每次激活分配新 ID）</summary>
        public ushort InstanceId;

        /// <summary>能力配置的 AssetId</summary>
        public byte AssetId;

        /// <summary>当前状态</summary>
        public AbilityState State;

        /// <summary>剩余冷却时间（秒）</summary>
        public float CooldownRemaining;

        /// <summary>剩余持续时间（秒），Instant 能力为 0</summary>
        public float DurationRemaining;

        /// <summary>此能力添加的标签位掩码（用于撤销时移除）</summary>
        public long AppliedTagsMask;

        public bool IsActive => State == AbilityState.Active || State == AbilityState.Predicting;
        public bool IsFinished => State == AbilityState.Inactive || State == AbilityState.Cancelled;

        public static AbilityInstanceData Create(byte assetId, ushort instanceId, float cooldown, float duration)
        {
            return new AbilityInstanceData
            {
                InstanceId = instanceId,
                AssetId = assetId,
                State = AbilityState.Inactive,
                CooldownRemaining = cooldown,
                DurationRemaining = duration,
                AppliedTagsMask = 0,
            };
        }
    }
}
