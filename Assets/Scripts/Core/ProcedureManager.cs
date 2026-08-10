using System.Collections.Generic;

/// <summary>
/// 流程状态机。管理游戏从启动到退出的全生命周期。
/// StartMenu → Lobby → Loading → Battle → Result → StartMenu ...
///
/// 每个 Procedure 是一个状态，可以被子状态机嵌套。
/// </summary>
public class ProcedureManager : ManagerBase
{
    public static ProcedureManager Instance => ManagerHub.Instance.Get<ProcedureManager>();

    private readonly Dictionary<string, ProcedureBase> _procedures = new Dictionary<string, ProcedureBase>();
    private ProcedureBase _current;

    /// <summary>注册流程</summary>
    public void Register(ProcedureBase procedure)
    {
        _procedures[procedure.Name] = procedure;
    }

    /// <summary>切换到指定流程</summary>
    public void SwitchTo(string name)
    {
        if (_procedures.TryGetValue(name, out var next))
        {
            _current?.OnExit();
            _current = next;
            _current.OnEnter();
        }
    }

    public ProcedureBase Current => _current;

    public override void Update(float dt) => _current?.OnUpdate(dt);
}

/// <summary>
/// 流程基类。每个流程是游戏的一个阶段（主菜单、大厅、加载、战斗、结算）。
/// </summary>
public abstract class ProcedureBase
{
    public abstract string Name { get; }
    public virtual void OnEnter() { }
    public virtual void OnUpdate(float dt) { }
    public virtual void OnExit() { }
}
