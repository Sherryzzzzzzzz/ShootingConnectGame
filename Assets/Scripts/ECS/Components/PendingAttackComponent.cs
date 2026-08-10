using System.Collections.Generic;
using ShootingGame.Shared.Protocol;

/// <summary>
/// 待确认攻击队列组件（客户端专用，挂在本地玩家实体）。
/// 攻击创建后入队，按间隔重传，直到服务端确认（HitEvent / 权威帧）或超时。
/// 替代原 AttackManager 的 pending 队列。
/// </summary>
public struct PendingAttackComponent
{
    /// <summary>待重传的攻击队列。</summary>
    public List<PendingAttackData> Attacks;

    /// <summary>本地预测的子弹攻击 ID（去重：权威帧跳过已预测生成的子弹）。</summary>
    public HashSet<int> PredictedBulletAttackIds;

    /// <summary>下一个攻击 ID。</summary>
    public int NextAttackId;
}

/// <summary>单条待重传攻击。</summary>
public struct PendingAttackData
{
    public AttackOperation Attack;
    public float AimYaw;
    public float AimPitch;
    public float SendTime;
    public int ResendAttempts;
}
