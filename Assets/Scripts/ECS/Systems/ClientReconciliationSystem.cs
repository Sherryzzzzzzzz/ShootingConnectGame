using ShootingGame.Shared.ECS;
using ShootingGame.Shared.Math;
using ShootingGame.Shared.Physics;
using ShootingGame.Shared.Protocol;
using ShootingGame.Shared.Simulation;
using UnityEngine;

/// <summary>
/// 客户端和解系统：对比预测快照与服务端权威状态，必要时修正并重新模拟。
/// </summary>
public static class ClientReconciliationSystem
{
    /// <summary>
    /// 执行服务端和解。返回 true 表示执行了重新模拟。
    /// </summary>
    public static bool Reconcile(
        EntityManager em, Entity entity,
        PlayerStateMsg serverState, int serverTick,
        ref PlayerSnapshot currentSnapshot,
        RingBuffer<InputFrame> inputHistory,
        RingBuffer<PlayerSnapshot> snapshotHistory,
        int currentTick, float dt, CollisionWorld world,
        ref int lastServerTick)
    {
        if (serverTick <= lastServerTick) return false;
        lastServerTick = serverTick;

        // 服务端领先客户端：不用 serverTick 严格匹配历史（历史里没有未来帧），
        // 直接用当前预测位置和服务端权威位置对比校准（参考 SpaceBuilder CommonPredictionStrategy）。
        var predicted = currentSnapshot;
        bool needsResim = false;

        // HP 和解：同步快照 + 写入 ECS，防止下一帧 BuildSnapshot 覆盖
        if (predicted.Health != serverState.Hp)
        {
            currentSnapshot.Health = (byte)serverState.Hp;
            if (em.TryGetComponent<HealthComponent>(entity, out var hp))
            {
                hp.Current = (byte)serverState.Hp;
                em.SetComponent(entity, hp);
            }
        }

        // 弹药和解
        if (predicted.CurrentAmmo != serverState.CurrentAmmo)
        {
            currentSnapshot.CurrentAmmo = serverState.CurrentAmmo;
            needsResim = true;
        }

        // 换弹状态和解
        if (predicted.IsReloading != serverState.IsReloading)
        {
            currentSnapshot.IsReloading = serverState.IsReloading;
            needsResim = true;
        }

        // 位置和解：直接对比当前预测位置和服务端权威位置
        float posDist = Vec3.Distance(predicted.Position, serverState.Position);
        if (posDist > 3f)
        {
            // 大偏差：回滚到服务端权威位置，用历史输入重演到当前 tick（SpaceBuilder 回滚重演）
            currentSnapshot.Position = serverState.Position;
            currentSnapshot.Velocity = serverState.Velocity;
            currentSnapshot.VerticalVelocity = serverState.Velocity.y;
            currentSnapshot.IsGrounded = serverState.IsGrounded;
            needsResim = true;
        }
        else if (posDist > 0.3f)
        {
            float blend = 0.1f;
            currentSnapshot.Position = Vec3.Lerp(predicted.Position, serverState.Position, blend);
        }

        // 大偏差 → 回滚重演：从最近历史起点（服务端领先，无法用 serverTick，用当前往前 15 帧）
        if (needsResim || posDist > 10f)
        {
            ECSBridge.ApplyServerCorrection(em, entity, currentSnapshot);
            int resimFrom = Mathf.Max(1, currentTick - 15);
            ClientPredictionSystem.Resimulate(em, entity, resimFrom, currentTick, dt, world, inputHistory, snapshotHistory);

            var latestSnap = ECSBridge.BuildSnapshot(em, entity, currentTick - 1);
            if (latestSnap.Tick > 0)
                currentSnapshot = latestSnap;
        }

        return needsResim;
    }
}
