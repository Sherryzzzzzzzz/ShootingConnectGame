using System;
using System.Collections.Generic;

namespace ShootingGame.Shared.ECS
{
    /// <summary>
    /// 轻量 ECS 实体管理器。使用密集数组存储组件，按实体 ID 索引。
    /// 最大 256 实体、64 种组件类型。
    /// </summary>
    public class EntityManager
    {
        public const int MaxEntities = 256;
        public const int MaxComponentTypes = 64;

        private int[] _generations = new int[MaxEntities];
        private long[] _componentMasks = new long[MaxEntities];
        private bool[] _alive = new bool[MaxEntities];
        private object[] _componentStores = new object[MaxComponentTypes];
        private Stack<int> _freeSlots = new Stack<int>();

        private List<int> _allEntities = new List<int>(MaxEntities);
        private List<int> _tempResult = new List<int>(64);

        public int ActiveEntityCount => _allEntities.Count;

        public EntityManager()
        {
            for (int i = MaxEntities - 1; i >= 0; i--)
                _freeSlots.Push(i);
        }

        /// <summary>
        /// 创建新实体。
        /// </summary>
        public Entity CreateEntity()
        {
            if (_freeSlots.Count == 0)
                throw new InvalidOperationException("EntityManager: max entities reached");

            int id = _freeSlots.Pop();
            int gen = ++_generations[id];
            _componentMasks[id] = 0;
            _alive[id] = true;
            _allEntities.Add(id);
            return new Entity(id, gen);
        }

        /// <summary>
        /// 销毁实体，释放其槽位。
        /// </summary>
        public void DestroyEntity(Entity entity)
        {
            if (!IsValid(entity)) return;

            // 清除所有组件的存储
            long mask = _componentMasks[entity.Id];
            for (int i = 0; i < MaxComponentTypes && mask != 0; i++)
            {
                if ((mask & (1L << i)) != 0)
                {
                    ClearComponentData(i, entity.Id);
                    mask &= ~(1L << i);
                }
            }

            _componentMasks[entity.Id] = 0;
            _alive[entity.Id] = false;
            _allEntities.Remove(entity.Id);
            _freeSlots.Push(entity.Id);
        }

        /// <summary>
        /// 检查实体是否仍然有效（未销毁且 generation 匹配）。
        /// </summary>
        public bool IsValid(Entity entity)
        {
            return entity.Id >= 0 && entity.Id < MaxEntities
                && _generations[entity.Id] == entity.Generation
                && _alive[entity.Id];
        }

        /// <summary>
        /// 检查实体是否拥有指定组件类型。
        /// </summary>
        public bool HasComponent<T>(Entity entity)
        {
            if (!IsValid(entity)) return false;
            int typeId = ComponentTypeId.Get<T>();
            return (_componentMasks[entity.Id] & (1L << typeId)) != 0;
        }

        /// <summary>
        /// 获取实体的组件引用。实体必须拥有该组件。
        /// </summary>
        public ref T GetComponent<T>(Entity entity)
        {
            int typeId = ComponentTypeId.Get<T>();
            var store = GetOrCreateStore<T>(typeId);
            return ref store[entity.Id];
        }

        /// <summary>
        /// 尝试获取组件，返回是否成功。
        /// </summary>
        public bool TryGetComponent<T>(Entity entity, out T value)
        {
            if (HasComponent<T>(entity))
            {
                value = GetComponent<T>(entity);
                return true;
            }
            value = default;
            return false;
        }

        /// <summary>
        /// 为实体添加组件（初始化为默认值）。
        /// </summary>
        public void AddComponent<T>(Entity entity)
        {
            AddComponent(entity, default(T));
        }

        /// <summary>
        /// 为实体添加组件并设置初始值。
        /// </summary>
        public void AddComponent<T>(Entity entity, T initialValue)
        {
            if (!IsValid(entity))
                throw new ArgumentException("Entity is not valid");
            if (HasComponent<T>(entity))
                return;

            int typeId = ComponentTypeId.Get<T>();
            var store = GetOrCreateStore<T>(typeId);
            store[entity.Id] = initialValue;
            _componentMasks[entity.Id] |= (1L << typeId);
        }

        /// <summary>
        /// 移除实体的组件。
        /// </summary>
        public void RemoveComponent<T>(Entity entity)
        {
            if (!IsValid(entity)) return;

            int typeId = ComponentTypeId.Get<T>();
            if ((_componentMasks[entity.Id] & (1L << typeId)) == 0) return;

            ClearComponentData(typeId, entity.Id);
            _componentMasks[entity.Id] &= ~(1L << typeId);
        }

        /// <summary>
        /// 设置组件值（如果不存在则自动添加）。
        /// </summary>
        public void SetComponent<T>(Entity entity, T value)
        {
            if (!HasComponent<T>(entity))
                AddComponent(entity, value);
            else
                GetComponent<T>(entity) = value;
        }

        /// <summary>
        /// 获取所有拥有指定组件的实体列表。
        /// </summary>
        public void GetEntitiesWith<T>(List<Entity> result)
        {
            result.Clear();
            int typeId = ComponentTypeId.Get<T>();
            long typeMask = 1L << typeId;
            for (int i = 0; i < _allEntities.Count; i++)
            {
                int id = _allEntities[i];
                if ((_componentMasks[id] & typeMask) != 0)
                    result.Add(new Entity(id, _generations[id]));
            }
        }

        /// <summary>
        /// 获取所有同时拥有多个组件的实体列表。
        /// </summary>
        public void GetEntitiesWith(long requiredMask, List<Entity> result)
        {
            result.Clear();
            for (int i = 0; i < _allEntities.Count; i++)
            {
                int id = _allEntities[i];
                if ((_componentMasks[id] & requiredMask) == requiredMask)
                    result.Add(new Entity(id, _generations[id]));
            }
        }

        /// <summary>
        /// 获取实体的组件位掩码。
        /// </summary>
        public long GetComponentMask(Entity entity)
        {
            if (!IsValid(entity)) return 0;
            return _componentMasks[entity.Id];
        }

        /// <summary>
        /// 清空所有实体。
        /// </summary>
        public void Clear()
        {
            _allEntities.Clear();
            _freeSlots.Clear();
            for (int i = MaxEntities - 1; i >= 0; i--)
                _freeSlots.Push(i);
            Array.Clear(_generations, 0, MaxEntities);
            Array.Clear(_componentMasks, 0, MaxEntities);
            Array.Clear(_alive, 0, MaxEntities);
            for (int i = 0; i < MaxComponentTypes; i++)
                _componentStores[i] = null;
        }

        private T[] GetOrCreateStore<T>(int typeId)
        {
            var store = _componentStores[typeId] as T[];
            if (store == null)
            {
                store = new T[MaxEntities];
                _componentStores[typeId] = store;
            }
            return store;
        }

        private void ClearComponentData(int typeId, int entityId)
        {
            var store = _componentStores[typeId];
            if (store != null)
            {
                // 使用 Array.Clear 对引用类型有效；值类型设为 default
                Array.Clear((Array)store, entityId, 1);
            }
        }
    }
}
