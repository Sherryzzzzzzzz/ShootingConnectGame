using UnityEngine;
using Animancer;
using ShootingGame.Shared.ECS;
using ShootingGame.Shared.Simulation;

/// <summary>
/// 客户端动画决策系统（替代 PistolGirlStateMachine + PistolAnimationDriver）。
/// 从 ECS 组件状态推导应播放的动画，调用 PlayerAnimationView 执行播放。
/// 本地玩家：读 InputComponent/TransformComponent/MovementComponent/HealthComponent。
/// 远程玩家：读 PlayerViewComponent 的插值状态与触发器。
/// </summary>
public static class ClientAnimationSystem
{
    /// <summary>每帧更新玩家动画（本地 + 远程共用）。</summary>
    public static void UpdatePlayer(EntityManager em, Entity entity)
    {
        if (!em.HasComponent<AnimationStateComponent>(entity)) return;
        if (!em.HasComponent<PlayerViewComponent>(entity)) return;

        var view = em.GetComponent<PlayerViewComponent>(entity);
        if (view.View == null) return;
        var animView = view.AnimationView;
        if (animView == null)
        {
            animView = view.View.GetComponent<PlayerAnimationView>();
            if (animView == null) return;
            view.AnimationView = animView;
            em.SetComponent(entity, view);
        }

        var state = em.GetComponent<AnimationStateComponent>(entity);

        // 首次初始化（清空 Controller + 加载 Clips + 建射击层 + 播放 Idle）
        if (!state.Started)
        {
            animView.EnsureInitialized();
            if (animView.Animancer == null) return;
            state.Started = true;
            em.SetComponent(entity, state);
            return;
        }

        if (view.IsLocal)
            UpdateLocal(em, entity, view, animView, ref state);
        else
            UpdateRemote(em, entity, view, animView, ref state);

        em.SetComponent(entity, state);
    }

    // ==================== 本地玩家 ====================

    private static void UpdateLocal(EntityManager em, Entity entity,
        PlayerViewComponent view, PlayerAnimationView animView, ref AnimationStateComponent s)
    {
        var input = em.TryGetComponent<InputComponent>(entity, out var ic) ? ic : default;
        var tx = em.TryGetComponent<TransformComponent>(entity, out var t) ? t : default;
        var mv = em.TryGetComponent<MovementComponent>(entity, out var m) ? m : default;
        var hp = em.TryGetComponent<HealthComponent>(entity, out var h) ? h : default;

        bool grounded = mv.IsGrounded;
        Vector3 vel = new Vector3(mv.Velocity.x, 0f, mv.Velocity.z);
        float speed = vel.magnitude;
        bool moving = speed > 0.3f;
        bool fast = speed > 5f;
        bool aiming = input.Aim;
        bool crouching = input.Crouch;

        s.WarmupFrames++;
        var frame = new AnimationFrame
        {
            Health = hp.Current,
            IsGrounded = grounded,
            VerticalVelocity = mv.VerticalVelocity,
            Aiming = aiming,
            Crouching = crouching,
            AimYaw = input.AimYaw,
            Moving = moving,
            Fast = fast
        };

        // ---- 死亡 ----
        if (s.WarmupFrames > 10 && frame.Health <= 0)
        {
            animView.PlayOnce(animView.Die1);
            return;
        }

        // ---- 受击 ----
        if (frame.Health < s.LastHp)
        {
            animView.PlayOnce(Random.value < 0.5f ? animView.Hit1 : animView.Hit2);
            s.PrevState = s.State;
            s.State = AnimationStateComponent.StateHit;
            s.LastHp = frame.Health;
            return;
        }
        s.LastHp = frame.Health;
        if (s.State == AnimationStateComponent.StateHit)
        {
            s.FrameCount++;
            if (s.FrameCount < 20) return;
            s.FrameCount = 0;
            s.State = s.PrevState;
        }

        // ---- 蹲伏碰撞体 ----
        if (crouching != s.WasCrouching)
        {
            animView.ApplyCrouchCollider(crouching);
        }

        // ---- 拔枪（右键瞄准时拔枪；蹲下时直接进入蹲伏瞄准）----
        if (aiming && !s.WasAiming && !s.GunDrawn)
        {
            s.GunDrawn = true;
            if (!crouching)
            {
                animView.PlayOnce(animView.DrawGun);
                s.State = AnimationStateComponent.StateDrawing;
                s.FrameCount = 0;
                return;
            }
            s.State = AnimationStateComponent.StateAiming;
        }
        // ---- 收枪 ----
        if (!aiming && s.WasAiming && s.GunDrawn)
        {
            animView.PlayOnce(animView.HolsterGun);
            s.State = AnimationStateComponent.StateHolstering;
            s.GunDrawn = false;
            s.FrameCount = 0;
            return;
        }
        // ---- 拔枪/收枪动画播完 ----
        if (s.State == AnimationStateComponent.StateDrawing || s.State == AnimationStateComponent.StateHolstering)
        {
            s.FrameCount++;
            if (s.FrameCount < 15) return;
            s.FrameCount = 0;
            s.State = AnimationStateComponent.StateIdle;
        }

        // ---- 转向 ----
        float deltaYaw = Mathf.DeltaAngle(s.LastYaw, frame.AimYaw);
        if (!moving && Mathf.Abs(deltaYaw) > 15f)
        {
            animView.PlayOnce(deltaYaw > 0 ? animView.TurnR90 : animView.TurnL90);
            s.LastYaw = frame.AimYaw;
            return;
        }
        s.LastYaw = frame.AimYaw;

        // ---- 主状态 ----
        if (Time.unscaledTime < s.ShootLockUntil)
        {
            animView.PlayClip(crouching ? animView.CrouchShoot : animView.Shoot);
            s.WasAiming = aiming; s.WasCrouching = crouching;
            return;
        }
        if (crouching && aiming)
        {
            animView.PlayClip(moving ? animView.CrouchAimWalk : animView.CrouchAimIdle);
        }
        else if (crouching)
        {
            if (moving && fast) animView.PlayClip(animView.CrouchJog);
            else if (moving) animView.PlayClip(animView.CrouchWalk);
            else animView.PlayClip(animView.CrouchIdle);
        }
        else if (aiming)
        {
            if (moving && fast) animView.PlayClip(animView.AimJog);
            else if (moving) animView.PlayClip(animView.AimWalkF);
            else animView.PlayClip(animView.AimIdle);
            s.State = AnimationStateComponent.StateAiming;
        }
        else if (moving)
        {
            animView.PlayClip(fast ? animView.Run : animView.Walk);
            s.State = AnimationStateComponent.StateMoving;
        }
        else if (!grounded)
        {
            animView.PlayClip(frame.VerticalVelocity > 0 ? animView.Jump : animView.Fall);
        }
        else
        {
            animView.PlayClip(animView.Idle);
            s.State = AnimationStateComponent.StateIdle;
        }

        // 摄像机切换（crouch > aim > normal）+ FOV
        if (crouching) animView.SwitchCamera("crouch");
        else if (aiming) animView.SwitchCamera("aim");
        else animView.SwitchCamera("normal");
        animView.AdjustCamera(fast, crouching);

        s.WasAiming = aiming;
        s.WasCrouching = crouching;
    }

    // ==================== 远程玩家 ====================

    private static void UpdateRemote(EntityManager em, Entity entity,
        PlayerViewComponent view, PlayerAnimationView animView, ref AnimationStateComponent s)
    {
        var hp = em.TryGetComponent<HealthComponent>(entity, out var h) ? h : default;

        // ---- 死亡（最高优先级）----
        if (s.DeathLocked)
        {
            animView.PlayClip(animView.Die1);
            return;
        }
        if (hp.Current <= 0)
        {
            s.DeathLocked = true;
            animView.PlayOnce(animView.Die1);
            return;
        }

        // 等待有效快照
        if (!view.HasTarget)
        {
            animView.PlayClip(animView.Idle);
            return;
        }

        // ---- 触发器（帧数据累积）----
        if (view.DeathTrigger)
        {
            view.DeathTrigger = false;
            s.DeathLocked = true;
            animView.PlayOnce(animView.Die1);
            return;
        }
        if (view.FireTrigger)
        {
            view.FireTrigger = false;
            animView.PlayOnce(animView.Shoot);
            return;
        }
        if (view.HitTrigger)
        {
            view.HitTrigger = false;
            animView.PlayOnce(Random.value < 0.5f ? animView.Hit1 : animView.Hit2);
            s.HitLockFrames = 60;
            return;
        }
        if (s.HitLockFrames > 0)
        {
            s.HitLockFrames--;
            return;
        }

        // ---- 运动状态（用插值后的渲染状态）----
        Vector3 vl = new Vector3(view.RenderedVelocity.x, 0f, view.RenderedVelocity.z);
        bool moving = vl.magnitude > 0.5f;
        bool fast = vl.magnitude > 5f;
        bool grounded = view.RenderedIsGrounded;
        bool crouching = view.IsCrouching;
        bool aiming = view.IsAiming;

        // ---- 拔枪 / 收枪 ----
        if (aiming && !s.WasAiming && !s.GunDrawn && animView.DrawGun != null)
        {
            animView.PlayOnce(animView.DrawGun);
            s.GunDrawn = true;
            s.DrawHolsterFrames = 15;
            s.WasAiming = aiming;
            return;
        }
        if (!aiming && s.WasAiming && s.GunDrawn && animView.HolsterGun != null)
        {
            animView.PlayOnce(animView.HolsterGun);
            s.GunDrawn = false;
            s.DrawHolsterFrames = 15;
            s.WasAiming = aiming;
            return;
        }
        if (s.DrawHolsterFrames > 0)
        {
            s.DrawHolsterFrames--;
            s.WasAiming = aiming;
            return;
        }
        s.WasAiming = aiming;

        // ---- 跳跃 / 下落 ----
        if (!grounded)
        {
            if (s.WasGrounded)
                animView.PlayOnce(animView.Jump);
            else
                animView.PlayClip(animView.Fall);
            s.WasGrounded = grounded;
            return;
        }
        s.WasGrounded = grounded;

        // ---- 转向 ----
        float dYaw = Mathf.DeltaAngle(s.LastYaw, view.View.transform.eulerAngles.y);
        if (!moving && Mathf.Abs(dYaw) > 10f)
        {
            animView.PlayOnce(dYaw > 0 ? animView.TurnR90 : animView.TurnL90);
            s.LastYaw = view.View.transform.eulerAngles.y;
            return;
        }
        s.LastYaw = view.View.transform.eulerAngles.y;

        // ---- 主移动 ----
        if (crouching && moving) animView.PlayClip(animView.CrouchWalk);
        else if (crouching) animView.PlayClip(animView.CrouchIdle);
        else if (aiming && s.GunDrawn && moving && animView.AimWalkF != null) animView.PlayClip(animView.AimWalkF);
        else if (aiming && s.GunDrawn && animView.AimIdle != null) animView.PlayClip(animView.AimIdle);
        else if (moving && fast) animView.PlayClip(animView.Run);
        else if (moving) animView.PlayClip(animView.Walk);
        else animView.PlayClip(animView.Idle);
    }

    /// <summary>动画决策输入帧（本地玩家用）。</summary>
    private struct AnimationFrame
    {
        public byte Health;
        public bool IsGrounded;
        public float VerticalVelocity;
        public bool Aiming;
        public bool Crouching;
        public float AimYaw;
        public bool Moving;
        public bool Fast;
    }
}
