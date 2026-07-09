using ShootingGame.Network;
using ShootingGame.Network.Server;
using ShootingGame.Shared.ECS;
using ShootingGame.Shared.Math;
using ShootingGame.Shared.Protocol;

/// <summary>
/// 玩家战斗 NetworkBehaviour。处理射击 RPC 和伤害同步。
/// 这是新框架落地的第一个 NetworkBehaviour 子类。
/// </summary>
public partial class PlayerCombatBehaviour : NetworkBehaviour
{
    /// <summary>
    /// ServerRpc：客户端请求射击。服务端执行弹药检查，交给 HostBattleServer 做 hitscan。
    /// Source Generator 自动生成 InvokeServerRpc_RequestShoot() 客户端代理方法。
    /// </summary>
    [ServerRpc]
    public void RequestShoot(float towardX, float towardY, float aimPitch, int clientFrameId)
    {
        // === 运行在服务端 ===
        if (EntityManager == null || !Entity.IsValid) return;

        // 检查弹药
        if (EntityManager.TryGetComponent<AmmoComponent>(Entity, out var ammo) &&
            EntityManager.TryGetComponent<ReloadComponent>(Entity, out var reload))
        {
            if (ammo.Current <= 0 || reload.IsReloading) return;
            ammo.Current--;
            EntityManager.SetComponent(Entity, ammo);
        }

        // 检查冷却
        if (EntityManager.TryGetComponent<FireCooldownComponent>(Entity, out var cd))
        {
            if (cd.Cooldown > 0f) return;
            cd.Cooldown = ShootingGame.Shared.Simulation.GameConstants.FireRate;
            EntityManager.SetComponent(Entity, cd);
        }

        // 入队待 HostBattleServer 处理 hitscan
        // ClientId 用 NetId 低 16 位 = 匹配的 BattlePlayerId (1, 2, 3...)
        HostBattleServer.PendingAttacks.Enqueue(new AttackEntry
        {
            Entity = Entity,
            ClientId = (int)(NetId & 0xFFFF),
            TowardX = towardX,
            TowardY = towardY,
            AimPitch = aimPitch,
            AttackId = clientFrameId
        });
    }

    /// <summary>
    /// ClientRpc：服务端通知所有客户端命中事件。
    /// </summary>
    [ClientRpc]
    public void OnHitReceived(int victimId, int damage, Vec3 hitPoint)
    {
        var ui = UnityEngine.Object.FindFirstObjectByType<HitFeedbackUI>();
        if (ui != null)
            ui.ShowHitFeedback(new HitEventMsg
            {
                VictimId = victimId,
                Damage = damage,
                HitPoint = hitPoint
            });
    }

    /// <summary>
    /// ClientRpc：服务端通知客户端击杀。
    /// </summary>
    [ClientRpc]
    public void OnKill(int victimId)
    {
        UnityEngine.Debug.Log($"[PlayerCombatBehaviour] Player {victimId} was killed!");
    }
}
