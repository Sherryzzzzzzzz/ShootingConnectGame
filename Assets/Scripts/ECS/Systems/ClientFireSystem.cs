using ShootingGame.Shared.ECS;
using ShootingGame.Shared.Hero;
using ShootingGame.Shared.Math;
using ShootingGame.Shared.Simulation;
using UnityEngine;

/// <summary>
/// 客户端射击系统：基于 ECS 组件状态验证并执行预测射击。
/// 只负责 ECS 层面的校验和消耗，不创建 AttackOperation。
/// </summary>
public static class ClientFireSystem
{
    /// <summary>
    /// 验证 ECS 状态可否开火，如果可以则消耗弹药、设置冷却、计算弹道。
    /// 返回 true 表示已消耗 ECS 弹药，调用方应通过 AttackManager 创建 AttackOperation。
    /// </summary>
    public static bool ValidateAndConsume(
        EntityManager em, Entity entity,
        float aimYaw, float aimPitch, int clientFrameId,
        GunConfigData gun, int playerId,
        ref float bloomHeat, float dt,
        out Vector3 fireDir, out float spreadDeg, out Vec3 spawnPos)
    {
        fireDir = Vector3.forward;
        spreadDeg = 0f;
        spawnPos = Vec3.Zero;

        if (!em.IsValid(entity)) return false;
        if (!em.TryGetComponent<AmmoComponent>(entity, out var ammo)) return false;
        if (!em.TryGetComponent<FireCooldownComponent>(entity, out var cd)) return false;
        if (!em.TryGetComponent<ReloadComponent>(entity, out var reload)) return false;

        if (ammo.Current <= 0 || cd.Cooldown > 0f || reload.IsReloading)
            return false;

        // 计算弹道方向
        var aimRot = Quaternion.Euler(aimPitch, aimYaw, 0f);
        fireDir = aimRot * Vector3.forward;

        // 扩散计算
        if (gun != null)
        {
            bool isMoving = false;
            if (em.TryGetComponent<MovementComponent>(entity, out var mv))
                isMoving = (mv.Velocity.x * mv.Velocity.x + mv.Velocity.z * mv.Velocity.z) > 1f;

            spreadDeg = SpreadUtility.ComputeTotalSpread(gun, isMoving, bloomHeat);
            var sd = SpreadUtility.ApplyConeSpread(
                new Vec3(fireDir.x, fireDir.y, fireDir.z), spreadDeg,
                SpreadUtility.MakeSeed(clientFrameId, playerId));
            fireDir = new Vector3(sd.x, sd.y, sd.z);

            bloomHeat = Mathf.Min(bloomHeat + gun.BloomPerShot,
                gun.BloomMax > 0f ? gun.BloomMax : bloomHeat + gun.BloomPerShot);
        }

        // 消耗 ECS 弹药并设置冷却（+dt 补偿同 tick 内 FireCooldownSystem 的递减）
        ammo.Current--;
        float rate = cd.Rate > 0f ? cd.Rate : GameConstants.FireRate;
        cd.Cooldown = rate + dt;
        em.SetComponent(entity, ammo);
        em.SetComponent(entity, cd);

        return true;
    }
}
