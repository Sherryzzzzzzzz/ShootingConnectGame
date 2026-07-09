using ShootingGame.Shared.Ability;

namespace ShootingGame.Shared.ECS
{
    /// <summary>
    /// 能力实例组件：记录实体当前活跃的能力实例。
    /// 最多 4 个并发实例（FPS 游戏通常 1-2 个）。
    /// </summary>
    public struct AbilityInstanceComponent
    {
        public const int MaxInstances = 4;

        public AbilityInstanceData Slot0;
        public AbilityInstanceData Slot1;
        public AbilityInstanceData Slot2;
        public AbilityInstanceData Slot3;
        public byte ActiveCount;

        private static ushort _nextInstanceId = 1;

        public static ushort NextInstanceId()
        {
            if (_nextInstanceId == 0) _nextInstanceId = 1;
            return _nextInstanceId++;
        }

        public AbilityInstanceData GetSlot(int index)
        {
            switch (index)
            {
                case 0: return Slot0;
                case 1: return Slot1;
                case 2: return Slot2;
                case 3: return Slot3;
                default: return default;
            }
        }

        public void SetSlot(int index, AbilityInstanceData data)
        {
            switch (index)
            {
                case 0: Slot0 = data; break;
                case 1: Slot1 = data; break;
                case 2: Slot2 = data; break;
                case 3: Slot3 = data; break;
            }
        }

        /// <summary>
        /// 查找指定 AssetId 的第一个活跃实例槽位。返回 -1 表示未找到。
        /// </summary>
        public int FindActiveSlot(byte assetId)
        {
            if (Slot0.IsActive && Slot0.AssetId == assetId) return 0;
            if (Slot1.IsActive && Slot1.AssetId == assetId) return 1;
            if (Slot2.IsActive && Slot2.AssetId == assetId) return 2;
            if (Slot3.IsActive && Slot3.AssetId == assetId) return 3;
            return -1;
        }

        /// <summary>
        /// 查找第一个空闲槽位。返回 -1 表示已满。
        /// </summary>
        public int FindFreeSlot()
        {
            if (!Slot0.IsActive && Slot0.State == AbilityState.Inactive) return 0;
            if (!Slot1.IsActive && Slot1.State == AbilityState.Inactive) return 1;
            if (!Slot2.IsActive && Slot2.State == AbilityState.Inactive) return 2;
            if (!Slot3.IsActive && Slot3.State == AbilityState.Inactive) return 3;
            return -1;
        }

        /// <summary>
        /// 检查是否有指定 AssetId 的活跃能力。
        /// </summary>
        public bool HasActive(byte assetId) => FindActiveSlot(assetId) >= 0;

        /// <summary>
        /// 检查是否有任意能力的预测状态。
        /// </summary>
        public bool HasAnyPredicting()
        {
            return (Slot0.State == AbilityState.Predicting) ||
                   (Slot1.State == AbilityState.Predicting) ||
                   (Slot2.State == AbilityState.Predicting) ||
                   (Slot3.State == AbilityState.Predicting);
        }

        /// <summary>
        /// 清除所有已完成/取消的实例，释放槽位。
        /// </summary>
        public void CleanupFinished()
        {
            if (Slot0.IsFinished) { Slot0 = default; ActiveCount = (byte)(ActiveCount > 0 ? ActiveCount - 1 : 0); }
            if (Slot1.IsFinished) { Slot1 = default; ActiveCount = (byte)(ActiveCount > 1 ? ActiveCount - 1 : 0); }
            if (Slot2.IsFinished) { Slot2 = default; ActiveCount = (byte)(ActiveCount > 2 ? ActiveCount - 1 : 0); }
            if (Slot3.IsFinished) { Slot3 = default; ActiveCount = (byte)(ActiveCount > 3 ? ActiveCount - 1 : 0); }
        }
    }
}
