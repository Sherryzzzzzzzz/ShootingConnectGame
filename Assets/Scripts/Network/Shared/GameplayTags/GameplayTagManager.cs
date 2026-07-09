using System;
using System.Collections.Generic;

namespace ShootingGame.Shared.GameplayTags
{
    /// <summary>
    /// GameplayTag 运行时管理器：注册层级标签，预计算父子位掩码。
    /// 线程安全：仅初始化阶段写入，运行时只读。
    /// </summary>
    public static class GameplayTagManager
    {
        public const int MaxTags = 64;

        private static readonly List<TagEntry> _entries = new List<TagEntry>(MaxTags);
        private static readonly Dictionary<string, int> _nameToId = new Dictionary<string, int>(MaxTags);
        private static bool _baked;

        private struct TagEntry
        {
            public string Name;
            public string ParentName; // null for root tags
            public int BitIndex;
            public long SelfMask;
            public long DescendantMask; // self + all descendants
        }

        /// <summary>
        /// 注册一个标签。必须在游戏启动时调用，所有标签注册完毕后调用 Bake()。
        /// </summary>
        public static int Register(string name, string parent = null)
        {
            if (_baked)
                throw new InvalidOperationException("Cannot register tags after Bake()");
            if (_entries.Count >= MaxTags)
                throw new InvalidOperationException($"Max {MaxTags} tags exceeded");
            if (_nameToId.ContainsKey(name))
                throw new ArgumentException($"Tag '{name}' already registered");

            int id = _entries.Count;
            _nameToId[name] = id;
            _entries.Add(new TagEntry
            {
                Name = name,
                ParentName = parent,
                BitIndex = id,
                SelfMask = 1L << id,
                DescendantMask = 1L << id // will be computed in Bake()
            });

            return id;
        }

        /// <summary>
        /// 完成标签注册，预计算所有标签的子孙位掩码。
        /// </summary>
        public static void Bake()
        {
            if (_baked) return;

            // Build parent→child mapping
            var children = new List<int>[MaxTags];
            for (int i = 0; i < _entries.Count; i++)
            {
                var parentName = _entries[i].ParentName;
                if (parentName != null && _nameToId.TryGetValue(parentName, out int parentId))
                {
                    children[parentId] = children[parentId] ?? new List<int>();
                    children[parentId].Add(i);
                }
            }

            // Compute descendant masks bottom-up (post-order)
            ComputeDescendantMasks(children);

            _baked = true;
        }

        private static void ComputeDescendantMasks(List<int>[] children)
        {
            var visited = new bool[MaxTags];
            var order = new List<int>(MaxTags);

            // Post-order traversal starting from root tags (no parent)
            for (int i = 0; i < _entries.Count; i++)
            {
                if (!visited[i])
                    PostOrder(i, children, visited, order);
            }

            // Process in post-order: children before parents
            foreach (int id in order)
            {
                long mask = _entries[id].SelfMask;
                if (children[id] != null)
                {
                    foreach (int childId in children[id])
                        mask |= _entries[childId].DescendantMask;
                }
                var entry = _entries[id];
                entry.DescendantMask = mask;
                _entries[id] = entry;
            }
        }

        private static void PostOrder(int id, List<int>[] children, bool[] visited, List<int> order)
        {
            visited[id] = true;
            if (children[id] != null)
            {
                foreach (int childId in children[id])
                {
                    if (!visited[childId])
                        PostOrder(childId, children, visited, order);
                }
            }
            order.Add(id);
        }

        /// <summary>
        /// 重置管理器（用于测试或热重载）。
        /// </summary>
        public static void Reset()
        {
            _entries.Clear();
            _nameToId.Clear();
            _baked = false;
        }

        // --- 查询 API ---

        public static int GetId(string name)
        {
            return _nameToId.TryGetValue(name, out int id) ? id : -1;
        }

        public static string GetName(int id)
        {
            return (id >= 0 && id < _entries.Count) ? _entries[id].Name : "Invalid";
        }

        public static long GetSelfMask(int id)
        {
            return (id >= 0 && id < _entries.Count) ? _entries[id].SelfMask : 0;
        }

        public static long GetDescendantMask(int id)
        {
            return (id >= 0 && id < _entries.Count) ? _entries[id].DescendantMask : 0;
        }

        public static long GetMask(string name)
        {
            int id = GetId(name);
            return id >= 0 ? _entries[id].DescendantMask : 0;
        }

        /// <summary>
        /// 检查给定的位掩码是否包含指定名称的标签（层级匹配）。
        /// </summary>
        public static bool Matches(long tagMask, string name)
        {
            int id = GetId(name);
            return id >= 0 && (tagMask & _entries[id].DescendantMask) != 0;
        }

        /// <summary>
        /// 检查位掩码是否包含指定标签（精确匹配）。
        /// </summary>
        public static bool MatchesExact(long tagMask, int id)
        {
            return id >= 0 && id < _entries.Count && (tagMask & _entries[id].SelfMask) != 0;
        }

        public static int TagCount => _entries.Count;
        public static bool IsBaked => _baked;
    }
}
