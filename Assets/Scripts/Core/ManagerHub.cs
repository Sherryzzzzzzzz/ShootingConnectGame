using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 统一管理所有 Manager，驱动生命周期。
/// 挂载到场景根节点，DontDestroyOnLoad。
/// </summary>
public class ManagerHub : MonoBehaviour
{
    public static ManagerHub Instance { get; private set; }

    private readonly List<ManagerBase> _managers = new List<ManagerBase>();
    private readonly Dictionary<Type, ManagerBase> _typeMap = new Dictionary<Type, ManagerBase>();

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    /// <summary>注册并初始化一个 Manager。已注册则直接返回。</summary>
    public T Register<T>() where T : ManagerBase, new()
    {
        if (_typeMap.TryGetValue(typeof(T), out var exist))
            return (T)exist;

        var mgr = new T();
        _managers.Add(mgr);
        _typeMap[typeof(T)] = mgr;
        mgr.Init();
        mgr.Initialized = true;
        return mgr;
    }

    /// <summary>获取已注册的 Manager，未注册返回 null。</summary>
    public T Get<T>() where T : ManagerBase
    {
        _typeMap.TryGetValue(typeof(T), out var mgr);
        return mgr as T;
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        for (int i = 0; i < _managers.Count; i++)
            _managers[i].Update(dt);
    }

    private void LateUpdate()
    {
        for (int i = 0; i < _managers.Count; i++)
            _managers[i].LateUpdate();
    }

    private void FixedUpdate()
    {
        for (int i = 0; i < _managers.Count; i++)
            _managers[i].FixedUpdate();
    }

    private void OnDestroy()
    {
        for (int i = _managers.Count - 1; i >= 0; i--)
            _managers[i].Destroy();
        _managers.Clear();
        _typeMap.Clear();
    }
}
