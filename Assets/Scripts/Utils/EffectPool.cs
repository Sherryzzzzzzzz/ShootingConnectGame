using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EffectPool : MonoBehaviour
{
    public static EffectPool Instance;

    [System.Serializable]
    public class EffectPrefab
    {
        public string name;
        public GameObject prefab;
        public int preloadCount = 5;
    }

    public List<EffectPrefab> effects = new List<EffectPrefab>();
    private Dictionary<string, Queue<GameObject>> pool = new Dictionary<string, Queue<GameObject>>();

    private void Awake()
    {
        Instance = this;

        // 初始化对象池
        foreach (var effect in effects)
        {
            Queue<GameObject> q = new Queue<GameObject>();

            for (int i = 0; i < effect.preloadCount; i++)
            {
                GameObject obj = Instantiate(effect.prefab);
                obj.SetActive(false);
                q.Enqueue(obj);
            }

            pool.Add(effect.name, q);
        }
    }

    public GameObject SpawnEffect(string name, Vector3 position, Quaternion rotation)
    {
        if (!pool.ContainsKey(name))
        {
            Debug.LogWarning($"EffectPool: 没有找到名为 {name} 的特效");
            return null;
        }

        GameObject obj;
        if (pool[name].Count > 0)
        {
            obj = pool[name].Dequeue();
        }
        else
        {
            // 对象池空了，动态创建
            var prefab = effects.Find(e => e.name == name).prefab;
            obj = Instantiate(prefab);
        }

        obj.transform.position = position;
        obj.transform.rotation = rotation;
        obj.SetActive(true);

        ParticleSystem ps = obj.GetComponent<ParticleSystem>();
        if (ps != null)
        {
            ps.Clear();
            ps.Play();
            float totalTime = ps.main.duration + ps.main.startLifetime.constantMax;
            StartCoroutine(ReturnAfterTime(name, obj, totalTime));
        }
        else
        {
            // 没有粒子系统时的安全回收
            StartCoroutine(ReturnAfterTime(name, obj, 1f));
        }

        return obj;
    }

    private IEnumerator ReturnAfterTime(string name, GameObject obj, float time)
    {
        yield return new WaitForSeconds(time);
        obj.SetActive(false);
        pool[name].Enqueue(obj);
    }
}
