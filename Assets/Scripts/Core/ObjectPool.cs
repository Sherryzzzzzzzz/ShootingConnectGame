using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 通用对象池。线程不安全，仅主线程使用。
/// </summary>
public class ObjectPool<T> where T : class, new()
{
    private readonly Stack<T> _pool = new Stack<T>();
    private readonly int _maxSize;

    public int Count => _pool.Count;
    public int MaxSize => _maxSize;

    public ObjectPool(int preload = 0, int maxSize = 256)
    {
        _maxSize = maxSize;
        for (int i = 0; i < preload && i < maxSize; i++)
            _pool.Push(new T());
    }

    public T Get()
    {
        return _pool.Count > 0 ? _pool.Pop() : new T();
    }

    public void Return(T obj)
    {
        if (_pool.Count < _maxSize)
            _pool.Push(obj);
    }

    public void Clear() => _pool.Clear();
}

/// <summary>
/// GameObject 对象池。管理预制体实例的创建/回收。
/// </summary>
public class GameObjectPool
{
    private readonly Queue<GameObject> _pool = new Queue<GameObject>();
    private readonly GameObject _prefab;
    private readonly Transform _parent;
    private readonly int _maxSize;

    public GameObjectPool(GameObject prefab, Transform parent = null, int preload = 0, int maxSize = 64)
    {
        _prefab = prefab;
        _parent = parent;
        _maxSize = maxSize;
        for (int i = 0; i < preload && i < maxSize; i++)
        {
            var go = CreateNew();
            go.SetActive(false);
            _pool.Enqueue(go);
        }
    }

    private GameObject CreateNew()
    {
        var go = _parent ? Object.Instantiate(_prefab, _parent) : Object.Instantiate(_prefab);
        var pooled = go.GetComponent<PooledObject>() ?? go.AddComponent<PooledObject>();
        pooled.Pool = this;
        return go;
    }

    public GameObject Get()
    {
        var go = _pool.Count > 0 ? _pool.Dequeue() : CreateNew();
        go.SetActive(true);
        return go;
    }

    public GameObject Get(Vector3 position, Quaternion rotation)
    {
        var go = Get();
        go.transform.position = position;
        go.transform.rotation = rotation;
        return go;
    }

    public void Return(GameObject go)
    {
        go.SetActive(false);
        if (_pool.Count < _maxSize)
            _pool.Enqueue(go);
        else
            Object.Destroy(go);
    }

    public void Clear()
    {
        while (_pool.Count > 0)
            Object.Destroy(_pool.Dequeue());
    }
}

/// <summary>
/// 挂载到池化 GameObject 上，用于自动归还或延迟归还。
/// </summary>
public class PooledObject : MonoBehaviour
{
    public GameObjectPool Pool { get; set; }

    /// <summary>延迟 seconds 秒后自动归还</summary>
    public void ReturnAfter(float seconds)
    {
        Invoke(nameof(ReturnToPool), seconds);
    }

    public void ReturnToPool()
    {
        CancelInvoke();
        Pool?.Return(gameObject);
    }
}
