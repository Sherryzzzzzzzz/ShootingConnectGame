using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SingleonPattenBase<T> where T : SingleonPattenBase<T>
{
    protected SingleonPattenBase(){}
    private static volatile T instance;
    public static bool IsExisted { get; private set; } = false;
    private static object locker = new object();
    public static T Instance
    {
        get
        {
            if (instance == null)
            {
                lock (locker)
                {
                    instance = Activator.CreateInstance(typeof(T), true) as T;
                    if (instance != null)
                    {
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