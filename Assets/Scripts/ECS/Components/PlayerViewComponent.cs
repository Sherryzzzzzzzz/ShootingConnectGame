using UnityEngine;
using Vec3 = ShootingGame.Shared.Math.Vec3;

/// <summary>
/// 玩家表现层桥接组件（客户端专用）。
/// 持有玩家 GameObject 及渲染相关引用，表现层系统据此驱动渲染。
/// 不含任何游戏逻辑状态——所有模拟状态从共享 ECS 组件读取。
/// </summary>
public struct PlayerViewComponent
{
    /// <summary>玩家 GameObject（预制体实例）。</summary>
    public GameObject View;
    /// <summary>是否本地玩家（本地多驱动相机/射击锁定层，远程走插值）。</summary>
    public bool IsLocal;
    /// <summary>玩家 ID（BattlePlayerId）。</summary>
    public int PlayerId;

    /// <summary>Animancer 播放器（表现层薄壳持有）。</summary>
    public PlayerAnimationView AnimationView;
    /// <summary>枪口位置（子弹/特效出生点）。</summary>
    public Transform FirePoint;

    /// <summary>远程插值缓冲（仅远程玩家使用）。</summary>
    public InterpolationBuffer InterpBuffer;
    /// <summary>远程玩家最新快照时间（FrameId * TickDelta）。</summary>
    public float LatestFrameTime;
    /// <summary>远程玩家目标状态（插值输出）。</summary>
    public Vector3 TargetPosition;
    public Quaternion TargetRotation;
    public Vec3 TargetVelocity;
    public float TargetVerticalVelocity;
    public bool TargetIsGrounded;
    public bool HasTarget;
    /// <summary>远程渲染状态（动画据此判断移动/跳跃）。</summary>
    public Vec3 RenderedVelocity;
    public bool RenderedIsGrounded;
    /// <summary>远程同步标志（由帧数据累积，动画系统消费后重置）。</summary>
    public bool FireTrigger;
    public bool HitTrigger;
    public bool DeathTrigger;
    public bool JumpTrigger;
    /// <summary>远程瞄准/蹲伏（协议同步）。</summary>
    public bool IsAiming;
    public bool IsCrouching;

    /// <summary>远程触发器状态跟踪（帧数据累积，动画消费后重置）。</summary>
    public int LastKnownHp;
    public bool LastKnownAlive;
    public bool LastKnownGrounded;
}
