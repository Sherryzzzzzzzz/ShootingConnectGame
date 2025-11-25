using System.Collections;
using System;
using System.Collections.Generic;
using UnityEngine;

public class MonoManager : SingletonPatternMonoBase<MonoManager>
{
    private Action updateAction;

    public void AddUpdateAction(Action task)
    {
        updateAction += task;
    }

    public void RemoveUpdateAction(Action task)
    {
        updateAction -= task;
    }

    private void Update()
    { 
        updateAction?.Invoke();
    }
}
