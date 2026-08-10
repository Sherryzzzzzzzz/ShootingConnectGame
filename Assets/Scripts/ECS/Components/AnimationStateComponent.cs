using UnityEngine;

/// <summary>
/// 动画决策状态组件（客户端专用）。
/// ClientAnimationSystem 每帧据此决策应播放的动画，PlayerAnimationView 执行播放。
/// 本地与远程共用同一套决策状态机（简化自 PistolGirlStateMachine / PistolAnimationDriver）。
/// </summary>
public struct AnimationStateComponent
{
    // ---- 通用状态 ----
    /// <summary>是否已完成首帧初始化（清空 Controller + 加载 Clips + 建射击层）。</summary>
    public bool Started;
    /// <summary>拔枪状态（瞄准时拔枪，松开收枪）。</summary>
    public bool GunDrawn;
    public bool WasAiming;
    public bool WasCrouching;
    public bool WasGrounded;
    /// <summary>上一帧朝向（转向检测）。</summary>
    public float LastYaw;
    /// <summary>上一帧 HP（受击检测）。</summary>
    public int LastHp;
    /// <summary>受击/拔枪/收枪动画锁定剩余帧数。</summary>
    public int HitLockFrames;
    public int DrawHolsterFrames;
    /// <summary>射击动画锁定截止时间（unscaledTime）。</summary>
    public float ShootLockUntil;
    /// <summary>开火后预热帧（防止出生即死亡误判）。</summary>
    public int WarmupFrames;
    /// <summary>远程：死亡动画已锁定（持续播放死亡）。</summary>
    public bool DeathLocked;

    // ---- 本地状态机（State 枚举值见 AnimationState 常量）----
    public int State;
    public int PrevState;
    public int FrameCount;

    /// <summary>本地状态机枚举（与旧 PistolGirlStateMachine.State 对齐）。</summary>
    public const int StateIdle = 0;
    public const int StateMoving = 1;
    public const int StateAiming = 2;
    public const int StateCrouching = 3;
    public const int StateDrawing = 4;
    public const int StateHolstering = 5;
    public const int StateHit = 6;
}
