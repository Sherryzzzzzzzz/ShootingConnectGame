using UnityEngine;
using ShootingGame.Shared.ECS;
using ShootingGame.Shared.Math;
using ShootingGame.Shared.Protocol;
using ShootingGame.Shared.Simulation;

/// <summary>
/// 客户端远程玩家插值系统（替代 RemotePlayerController 的插值逻辑）。
/// 职责：
///  - 帧接收：缓存远程玩家快照到 InterpolationBuffer（PlayerViewComponent 持有）
///  - 每帧：按 RTT 延迟采样插值 → 写入 ECS TransformComponent（表现层读取）
/// </summary>
public static class ClientRemoteInterpolationSystem
{
    /// <summary>服务端帧到达时缓存远程玩家快照。</summary>
    public static void CacheFrame(EntityManager em, Entity entity, PlayerStateMsg state)
    {
        if (!em.HasComponent<PlayerViewComponent>(entity)) return;
        var pv = em.GetComponent<PlayerViewComponent>(entity);
        if (pv.InterpBuffer == null)
            pv.InterpBuffer = new InterpolationBuffer();

        var snap = new PlayerSnapshot
        {
            Position = state.Position,
            Rotation = Quat.Euler(0f, state.RotationY, 0f),
            Velocity = state.Velocity,
            VerticalVelocity = state.VerticalVelocity,
            IsGrounded = state.IsGrounded,
            State = (PlayerStateEnum)state.StateEnum,
            Health = (byte)state.Hp
        };
        pv.LatestFrameTime = BattleClient.Instance?.ServerFrameId * GameConstants.TickDelta ?? 0f;
        pv.InterpBuffer.Add(pv.LatestFrameTime, snap);

        // 边缘触发器（动画消费后重置）：HP 下降→受击；死亡→死亡；落地→跳跃
        int lastHp = pv.LastKnownHp;
        if (state.Hp < lastHp) pv.HitTrigger = true;
        if (state.IsDead && !pv.LastKnownAlive) pv.DeathTrigger = true;
        if (!state.IsGrounded && pv.LastKnownGrounded) pv.JumpTrigger = true;
        pv.LastKnownHp = state.Hp;
        pv.LastKnownAlive = !state.IsDead;
        pv.LastKnownGrounded = state.IsGrounded;
        pv.TargetVelocity = state.Velocity;
        pv.TargetVerticalVelocity = state.VerticalVelocity;
        pv.TargetIsGrounded = state.IsGrounded;
        pv.IsAiming = state.IsAiming;
        pv.IsCrouching = state.IsCrouching;
        pv.HasTarget = true;
        em.SetComponent(entity, pv);
    }

    /// <summary>每帧：采样插值 → 写回 ECS TransformComponent。</summary>
    public static void UpdateRemoteTransform(EntityManager em, Entity entity)
    {
        if (!em.HasComponent<PlayerViewComponent>(entity)) return;
        var pv = em.GetComponent<PlayerViewComponent>(entity);
        if (!pv.HasTarget || pv.InterpBuffer == null) return;

        float rtt = BattleClient.Instance != null ? BattleClient.Instance.SmoothedRtt : 0.05f;
        float delay = Mathf.Clamp(rtt * 0.5f + 0.02f, 0.02f, 0.1f);
        float renderTime = pv.LatestFrameTime - delay;

        bool sampled = pv.InterpBuffer.Sample(renderTime, out var from, out var to, out float t);
        if (sampled)
        {
            pv.TargetPosition = Vec3.Lerp(from.Position, to.Position, t).ToUnity();
            pv.TargetRotation = Quat.Slerp(from.Rotation, to.Rotation, t).ToUnity();
            pv.RenderedVelocity = Vec3.Lerp(from.Velocity, to.Velocity, t);
            pv.RenderedIsGrounded = to.IsGrounded;
        }

        // 平滑逼近目标（防止跳变）
        float smoothT = 1f - Mathf.Exp(-30f * Time.deltaTime);

        var view = pv.View != null ? pv.View.transform : null;
        Vector3 viewPos = view != null ? view.position : pv.TargetPosition;
        Quaternion viewRot = view != null ? view.rotation : Quaternion.identity;

        viewPos = Vector3.Lerp(viewPos, pv.TargetPosition, smoothT);
        viewRot = Quaternion.Slerp(viewRot, pv.TargetRotation, Time.deltaTime * 720f);

        // 写回 ECS（表现位置作为远程玩家权威视觉位置）
        if (em.TryGetComponent<TransformComponent>(entity, out var tx))
        {
            tx.Position = viewPos.ToShared();
            tx.Rotation = viewRot.ToShared();
            em.SetComponent(entity, tx);
        }
        if (em.TryGetComponent<MovementComponent>(entity, out var mv))
        {
            mv.Velocity = pv.RenderedVelocity;
            mv.VerticalVelocity = pv.TargetVerticalVelocity;
            mv.IsGrounded = pv.TargetIsGrounded;
            em.SetComponent(entity, mv);
        }
        if (view != null)
        {
            view.position = viewPos;
            view.rotation = viewRot;
        }

        em.SetComponent(entity, pv);
    }

    /// <summary>远程玩家 HP 同步（服务端权威）。</summary>
    public static void SyncHp(EntityManager em, Entity entity, int hp)
    {
        if (!em.TryGetComponent<HealthComponent>(entity, out var hc)) return;
        hc.Current = (byte)Mathf.Max(0, hp);
        em.SetComponent(entity, hc);
    }
}
