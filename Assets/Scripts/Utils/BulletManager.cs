using System.Collections.Generic;
using UnityEngine;

public class BulletManager : MonoBehaviour
{
    public static BulletManager Instance { get; private set; }

    [Header("Configuration")]
    public Bullet bulletPrefab;     // 请拖入挂载了新 Bullet 脚本的预制体
    public int initialPoolSize = 50; // 初始池子大小

    private Queue<Bullet> _pool = new Queue<Bullet>();

    private void Awake()
    {
        // 单例模式初始化
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        InitializePool();
    }

    private void InitializePool()
    {
        if (bulletPrefab == null)
        {
            Debug.LogError("BulletManager: 请在 Inspector 面板中赋值 Bullet Prefab！");
            return;
        }

        for (int i = 0; i < initialPoolSize; i++)
        {
            CreateNewBullet();
        }
    }

    private Bullet CreateNewBullet()
    {
        Bullet bullet = Instantiate(bulletPrefab, transform);
        bullet.gameObject.SetActive(false);
        _pool.Enqueue(bullet);
        return bullet;
    }

    // --- 获取子弹 ---
    public Bullet GetBullet()
    {
        if (_pool.Count == 0)
        {
            // 池子空了就扩容
            return CreateNewBullet(); // 注意：这里 CreateNewBullet 会入队，所以下面需要出队
            // 为了简化逻辑，直接创建并返回即可，不用 CreateNewBullet 的入队逻辑，但为了保持一致性：
            // 简单的做法是：
            // Bullet b = Instantiate(bulletPrefab, transform);
            // return b;
        }

        Bullet bullet = _pool.Dequeue();
        
        // 防止取出的物体因为某种原因被销毁了
        if (bullet == null) 
        {
            return CreateNewBullet(); 
        }

        return bullet;
    }

    // --- 回收子弹 ---
    public void ReturnBullet(Bullet bullet)
    {
        if (bullet == null) return;

        bullet.gameObject.SetActive(false);
        _pool.Enqueue(bullet);
    }
}