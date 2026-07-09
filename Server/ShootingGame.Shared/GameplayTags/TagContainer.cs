namespace ShootingGame.Shared.GameplayTags
{
    /// <summary>
    /// 运行时标签容器：存储实体当前拥有的标签位掩码。
    /// 支持客户端预测（Add/Remove 掩码）。
    /// 64-bit 位掩码，每个位对应一个 GameplayTag。
    /// </summary>
    public struct TagContainer
    {
        /// <summary>当前生效的标签位掩码。</summary>
        public long EffectiveMask;

        /// <summary>客户端预测将要添加的标签。</summary>
        public long PredictedAddMask;

        /// <summary>客户端预测将要移除的标签。</summary>
        public long PredictedRemoveMask;

        /// <summary>
        /// 获取合并了预测状态的位掩码（用于客户端预测渲染）。
        /// </summary>
        public readonly long PredictedMask => (EffectiveMask | PredictedAddMask) & ~PredictedRemoveMask;

        /// <summary>
        /// 检查是否包含指定标签（层级匹配，包含子孙标签）。
        /// </summary>
        public readonly bool HasTag(in GameplayTag tag) => tag.Matches(EffectiveMask);

        /// <summary>
        /// 检查是否包含指定标签（精确匹配）。
        /// </summary>
        public readonly bool HasTagExact(in GameplayTag tag) => tag.MatchesExact(EffectiveMask);

        /// <summary>
        /// 检查预测状态是否包含指定标签。
        /// </summary>
        public readonly bool HasTagPredicted(in GameplayTag tag) => tag.Matches(PredictedMask);

        /// <summary>
        /// 检查是否包含掩码中的任意标签。
        /// </summary>
        public readonly bool HasAny(long mask) => (EffectiveMask & mask) != 0;

        /// <summary>
        /// 检查是否包含掩码中的全部标签。
        /// </summary>
        public readonly bool HasAll(long mask) => (EffectiveMask & mask) == mask;

        /// <summary>
        /// 添加标签。
        /// </summary>
        public void AddTag(int tagId)
        {
            if (tagId < 0 || tagId >= 64) return;
            EffectiveMask |= (1L << tagId);
        }

        /// <summary>
        /// 添加标签（通过名称）。
        /// </summary>
        public void AddTag(string name)
        {
            int id = GameplayTagManager.GetId(name);
            if (id >= 0) EffectiveMask |= (1L << id);
        }

        /// <summary>
        /// 移除标签。
        /// </summary>
        public void RemoveTag(int tagId)
        {
            if (tagId < 0 || tagId >= 64) return;
            EffectiveMask &= ~(1L << tagId);
        }

        /// <summary>
        /// 移除标签（通过名称）。
        /// </summary>
        public void RemoveTag(string name)
        {
            int id = GameplayTagManager.GetId(name);
            if (id >= 0) EffectiveMask &= ~(1L << id);
        }

        /// <summary>
        /// 设置标签状态（true=添加, false=移除）。
        /// </summary>
        public void SetTag(int tagId, bool value)
        {
            if (value) AddTag(tagId); else RemoveTag(tagId);
        }

        /// <summary>
        /// 预测添加标签（客户端侧）。
        /// </summary>
        public void PredictAddTag(int tagId)
        {
            if (tagId < 0 || tagId >= 64) return;
            PredictedAddMask |= (1L << tagId);
            PredictedRemoveMask &= ~(1L << tagId);
        }

        /// <summary>
        /// 预测移除标签（客户端侧）。
        /// </summary>
        public void PredictRemoveTag(int tagId)
        {
            if (tagId < 0 || tagId >= 64) return;
            PredictedRemoveMask |= (1L << tagId);
            PredictedAddMask &= ~(1L << tagId);
        }

        /// <summary>
        /// 确认预测（服务端认可），将预测状态合并到 EffectiveMask。
        /// </summary>
        public void ConfirmPrediction()
        {
            EffectiveMask |= PredictedAddMask;
            EffectiveMask &= ~PredictedRemoveMask;
            PredictedAddMask = 0;
            PredictedRemoveMask = 0;
        }

        /// <summary>
        /// 拒绝预测（服务端否定），清除所有预测状态。
        /// </summary>
        public void RejectPrediction()
        {
            PredictedAddMask = 0;
            PredictedRemoveMask = 0;
        }

        /// <summary>
        /// 清空所有标签。
        /// </summary>
        public void Clear()
        {
            EffectiveMask = 0;
            PredictedAddMask = 0;
            PredictedRemoveMask = 0;
        }

        public override readonly string ToString()
        {
            return $"Effective=0x{EffectiveMask:X} PredAdd=0x{PredictedAddMask:X} PredRemove=0x{PredictedRemoveMask:X}";
        }
    }
}
