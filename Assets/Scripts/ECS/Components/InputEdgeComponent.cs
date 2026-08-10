/// <summary>
/// 输入边缘检测状态组件（客户端专用）。
/// 记录上一帧按键状态，用于生成 Jump/Reload/Ability 的上升沿脉冲。
/// 替代 NetPlayerController 中的 _lastJumpPressed 等字段。
/// </summary>
public struct InputEdgeComponent
{
    public bool LastJump;
    public bool LastReload;
    public bool LastAbility1;
    public bool LastAbility2;
    public bool LastAbility3;
    public bool LastAbility4;
}
