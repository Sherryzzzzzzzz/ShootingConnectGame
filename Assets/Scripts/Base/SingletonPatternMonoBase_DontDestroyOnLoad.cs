using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SingletonPatternMonoBase_DontDestroyOnLoad<T> : MonoBehaviour where T  : MonoBehaviour
{
    protected  SingletonPatternMonoBase_DontDestroyOnLoad(){}

    public static bool IsExisted { get; private set; } = false;
    private static object locker = new object();
    private static volatile T instance;

    public static T Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindObjectOfType<T>();
                if (instance == null)
                {
                    lock (locker)
                    {
                        GameObject obj = new GameObject(typeof(T).Name);
                        instance = obj.AddComponent<T>(); 
                        DontDestroyOnLoad(obj);
                        IsExisted = true;
                    }
                    
                }
            }

            return instance;
        }
    }

    protected virtual void OnDestroy()
    {
        IsExisted = false;
    }
    
}
