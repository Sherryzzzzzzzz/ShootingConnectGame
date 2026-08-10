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
            cd.Cooldown = cd.Rate > 0f ? cd.Rate : ShootingGame.Shared.Simulation.GameConstants.FireRate;
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

    /// <summary>
    /// ServerRpc：技能预测确认链——客户端预测施法后请求服务器权威验证。
    /// .NET 生产服务器经 BattleRoom 注册处理器执行；编辑器 Host 执行本方法体。
    /// </summary>
    [ServerRpc]
    public void RequestActivateAbility(int assetId, int predictedId)
    {
        // === 编辑器 Host 模式执行 ===
        if (EntityManager == null || !Entity.IsValid) return;
        // 客户端预测已在本地激活；此处服务器侧再验证（Host 模式用 AbilityLifecycleSystem）
        UnityEngine.Debug.Log($"[PlayerCombatBehaviour] ServerRpc RequestActivateAbility assetId={assetId} pred={predictedId}");
    }

    /// <summary>
    /// ClientRpc：服务器确认技能激活 → 客户端保留预测特效（predictedId 匹配预测）。
    /// </summary>
    [ClientRpc]
    public void ConfirmAbility(int predictedId, int instanceId)
    {
        ProceduralEffectManager.Instance?.OnAbilityConfirmed(predictedId);
    }

    /// <summary>
    /// ClientRpc：服务器拒绝技能激活 → 客户端回滚预测特效。
    /// </summary>
    [ClientRpc]
    public void RejectAbility(int predictedId)
    {
        ProceduralEffectManager.Instance?.OnAbilityRejected(predictedId);
    }
}
