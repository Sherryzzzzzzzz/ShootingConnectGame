using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 对象池管理器。按 prefab/类型 管理多个对象池。
/// 注册为 ManagerHub 的 Manager。
/// </summary>
public class PoolManager : ManagerBase
{
    private readonly Dictionary<GameObject, GameObjectPool> _goPools = new Dictionary<GameObject, GameObjectPool>();
    private readonly Dictionary<string, GameObjectPool> _namedPools = new Dictionary<string, GameObjectPool>();
    private Transform _root;

    public override void Init()
    {
        _root = new GameObject("[PoolManager]").transform;
        Object.DontDestroyOnLoad(_root.gameObject);
    }

    /// <summary>注册一个 GameObject 池（按 prefab 索引）</summary>
    public GameObjectPool Register(GameObject prefab, int preload = 0, int maxSize = 64)
    {
        if (_goPools.TryGetValue(prefab, out var pool))
            return pool;

        pool = new GameObjectPool(prefab, _root, preload, maxSize);
        _goPools[prefab] = pool;
        return pool;
    }

    /// <summary>注册并命名一个池（用于特效等按名称查找的场景）</summary>
    public GameObjectPool Register(string name, GameObject prefab, int preload = 0, int maxSize = 64)
    {
        if (_namedPools.TryGetValue(name, out var pool))
            return pool;

        pool = new GameObjectPool(prefab, _root, preload, maxSize);
        _namedPools[name] = pool;
        return pool;
    }

    /// <summary>从池中获取实例</summary>
    public GameObject Get(GameObject prefab) => _goPools.TryGetValue(prefab, out var p) ? p.Get() : Object.Instantiate(prefab);

    public GameObject Get(string name) => _namedPools.TryGetValue(name, out var p) ? p.Get() : null;

    public GameObject Get(GameObject prefab, Vector3 pos, Quaternion rot)
    {
        if (_goPools.TryGetValue(prefab, out var p)) return p.Get(pos, rot);
        return Object.Instantiate(prefab, pos, rot);
    }

    public void Return(GameObject prefab, GameObject go)
    {
        if (_goPools.TryGetValue(prefab, out var p)) p.Return(go);
        else Object.Destroy(go);
    }

    public override void Destroy()
    {
        foreach (var p in _goPools.Values) p.Clear();
        foreach (var p in _namedPools.Values) p.Clear();
        _goPools.Clear();
        _namedPools.Clear();
    }
}
