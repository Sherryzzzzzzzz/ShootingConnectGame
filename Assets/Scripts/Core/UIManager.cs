using UnityEngine;

/// <summary>
/// UI 面板基类。所有 UI 面板继承此类，统一生命周期。
/// </summary>
public abstract class UIPanel : MonoBehaviour
{
    /// <summary>面板是否可见</summary>
    public bool IsVisible => gameObject.activeSelf;

    /// <summary>显示面板</summary>
    public virtual void Show()
    {
        gameObject.SetActive(true);
        OnShow();
    }

    /// <summary>隐藏面板</summary>
    public virtual void Hide()
    {
        OnHide();
        gameObject.SetActive(false);
    }

    protected virtual void OnShow() { }
    protected virtual void OnHide() { }
}

/// <summary>
/// UI 管理器。管理所有 UIPanel 的显示/隐藏，处理层级遮挡。
/// 注册为 ManagerHub 的 Manager。
/// </summary>
public class UIManager : ManagerBase
{
    public static UIManager Instance => ManagerHub.Instance.Get<UIManager>();

    private Transform _root;
    private UIPanel[] _panels;

    public override void Init()
    {
        _root = GameObject.Find("[UI]")?.transform;
        if (_root == null)
        {
            _root = new GameObject("[UI]").transform;
            Object.DontDestroyOnLoad(_root.gameObject);
        }
        _panels = _root.GetComponentsInChildren<UIPanel>(true);
    }

    /// <summary>显示指定面板，自动隐藏互斥面板</summary>
    public T Show<T>() where T : UIPanel
    {
        foreach (var p in _panels)
        {
            if (p is T)
            {
                p.Show();
                return p as T;
            }
        }
        return null;
    }

    /// <summary>隐藏指定面板</summary>
    public void Hide<T>() where T : UIPanel
    {
        foreach (var p in _panels)
            if (p is T) p.Hide();
    }

    /// <summary>隐藏所有面板</summary>
    public void HideAll()
    {
        foreach (var p in _panels) p.Hide();
    }

    /// <summary>获取面板实例</summary>
    public T Get<T>() where T : UIPanel
    {
        foreach (var p in _panels)
            if (p is T t) return t;
        return null;
    }
}
