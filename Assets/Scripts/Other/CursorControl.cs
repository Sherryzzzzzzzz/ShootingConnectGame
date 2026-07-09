using UnityEngine;
public class CursorControl : MonoBehaviour
{
    // 在初始化时隐藏鼠标并锁定光标
    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void OnDestroy()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    // 当应用重新获得焦点时，重新锁定光标
    void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }
}