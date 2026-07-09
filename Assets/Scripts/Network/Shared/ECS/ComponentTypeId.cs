namespace ShootingGame.Shared.ECS
{
    /// <summary>
    /// 组件类型 ID 注册表。每种组件类型分配唯一 ID（0-63），支持快速位掩码查询。
    /// </summary>
    public static class ComponentTypeId
    {
        private static int _nextId;

        /// <summary>
        /// 获取或注册组件类型的唯一 ID。
        /// </summary>
        public static int Get<T>()
        {
            return TypeCache<T>.Id;
        }

        /// <summary>
        /// 已注册的组件类型总数。
        /// </summary>
        public static int Count => _nextId;

        /// <summary>
        /// 获取组件类型的位掩码（1 &lt;&lt; typeId）。
        /// </summary>
        public static long Mask<T>()
        {
            return 1L << TypeCache<T>.Id;
        }

        private static class TypeCache<T>
        {
            public static readonly int Id;

            static TypeCache()
            {
                Id = System.Threading.Interlocked.Increment(ref _nextId) - 1;
                if (Id >= 64)
                {
                    throw new System.InvalidOperationException(
                        $"ECS component type limit exceeded (max 64). Cannot register {typeof(T).Name}");
                }
            }
        }
    }
}
