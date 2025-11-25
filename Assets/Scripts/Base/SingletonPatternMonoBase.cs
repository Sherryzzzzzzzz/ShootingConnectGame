using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SingletonPatternMonoBase<T> : MonoBehaviour where T  : MonoBehaviour
{
    protected  SingletonPatternMonoBase(){}
    public static bool IsExisted { get; private set; } = false;
    
    private static  volatile T instance;
    private static object locker = new object();
    public static T Instance
    {
        get
        {
            if (instance == null)
            {
                lock (locker)
                {
                    instance = FindObjectOfType<T>();
                    if (instance == null)
                    {
                        GameObject obj = new GameObject(typeof(T).Name);
                        instance = obj.AddComponent<T>();
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
