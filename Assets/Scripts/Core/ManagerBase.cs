/// <summary>
/// Manager 基类。由 ManagerHub 统一驱动生命周期。
/// 每个 Manager 是纯 C# 类，不依赖 MonoBehaviour。
/// </summary>
public abstract class ManagerBase
{
    /// <summary>初始化完成后为 true，由 ManagerHub 设置</summary>
    public bool Initialized { get; internal set; }

    public virtual void Init() { }
    public virtual void Update(float dt) { }
    public virtual void LateUpdate() { }
    public virtual void FixedUpdate() { }
    public virtual void Destroy() { }
}
