using System.Collections.Generic;
using ShootingGame.Shared.ECS;
using ShootingGame.Shared.Protocol;
using UnityEngine;

/// <summary>
/// 客户端攻击系统（替代 AttackManager）。
/// 数据存于本地玩家实体的 PendingAttackComponent：待确认攻击队列 + 预测去重集合。
/// 由 ClientECSWorld 每个 tick 驱动重传。
/// </summary>
public static class ClientAttackSystem
{
    private const int MaxPendingAttacks = 32;
    private const float ResendInterval = 0.05f;   // 50ms
    private const int MaxResendAttempts = 20;     // ~1 second at 50ms

    /// <summary>确保本地玩家实体带有 PendingAttackComponent。</summary>
    public static PendingAttackComponent Ensure(EntityManager em, Entity entity)
    {
        if (!em.HasComponent<PendingAttackComponent>(entity))
        {
            em.AddComponent(entity, new PendingAttackComponent
            {
                Attacks = new List<PendingAttackData>(),
                PredictedBulletAttackIds = new HashSet<int>(),
                NextAttackId = 1
            });
        }
        return em.GetComponent<PendingAttackComponent>(entity);
    }

    /// <summary>射击间隔（秒），由枪械配置注入。</summary>
    public static float FireInterval = 0.15f;

    /// <summary>当前是否有本地玩家实体处于开火可用状态（含首帧闸门 + 队列上限）。</summary>
    public static bool CanFire(EntityManager em, Entity entity)
    {
        if (ClientECSWorld.Instance != null && !ClientECSWorld.Instance.HasReceivedServerFrame)
            return false;
        if (!em.HasComponent<PendingAttackComponent>(entity)) return false;
        var pending = em.GetComponent<PendingAttackComponent>(entity);
        return pending.Attacks.Count < MaxPendingAttacks;
    }

    /// <summary>
    /// 尝试创建新攻击（遵守射击间隔由 FireCooldownComponent 保证，这里只做队列上限）。
    /// </summary>
    public static bool TryCreateAttack(EntityManager em, Entity entity,
        float aimYaw, float aimPitch, int clientFrameId, out AttackOperation attack)
    {
        attack = null;
        var pending = Ensure(em, entity);
        if (pending.Attacks.Count >= MaxPendingAttacks)
        {
            Debug.LogWarning("[ClientAttackSystem] Too many pending attacks");
            return false;
        }

        int attackId = pending.NextAttackId++;
        attack = new AttackOperation
        {
            AttackId = attackId,
            TowardX = Mathf.Sin(aimYaw * Mathf.Deg2Rad),
            TowardY = Mathf.Cos(aimYaw * Mathf.Deg2Rad),
            AimPitch = aimPitch,
            ClientFrameId = clientFrameId
        };

        pending.Attacks.Add(new PendingAttackData
        {
            Attack = attack,
            AimYaw = aimYaw,
            AimPitch = aimPitch,
            SendTime = Time.unscaledTime,
            ResendAttempts = 0
        });
        em.SetComponent(entity, pending);
        return true;
    }

    /// <summary>确认攻击已被服务端处理，从队列移除。</summary>
    public static void ConfirmAttack(EntityManager em, Entity entity, int attackId)
    {
        if (!em.HasComponent<PendingAttackComponent>(entity)) return;
        var pending = em.GetComponent<PendingAttackComponent>(entity);
        for (int i = pending.Attacks.Count - 1; i >= 0; i--)
        {
            if (pending.Attacks[i].Attack.AttackId == attackId)
            {
                pending.Attacks.RemoveAt(i);
                break;
            }
        }
        em.SetComponent(entity, pending);
    }

    /// <summary>获取所有待确认攻击（组包发送用）。</summary>
    public static List<AttackOperation> GetPendingAttacks(EntityManager em, Entity entity)
    {
        var result = new List<AttackOperation>();
        if (!em.HasComponent<PendingAttackComponent>(entity)) return result;
        var pending = em.GetComponent<PendingAttackComponent>(entity);
        foreach (var p in pending.Attacks)
            result.Add(p.Attack);
        return result;
    }

    /// <summary>标记攻击为本地预测（权威帧将跳过该攻击的子弹生成）。</summary>
    public static void MarkAttackPredicted(EntityManager em, Entity entity, int attackId)
    {
        var pending = Ensure(em, entity);
        pending.PredictedBulletAttackIds.Add(attackId);
        em.SetComponent(entity, pending);
    }

    /// <summary>检查并消费预测攻击。返回 true 表示该攻击已在本地预测生成（权威帧应跳过）。</summary>
    public static bool TryConsumePredictedAttack(EntityManager em, Entity entity, int attackId)
    {
        if (!em.HasComponent<PendingAttackComponent>(entity)) return false;
        var pending = em.GetComponent<PendingAttackComponent>(entity);
        if (pending.PredictedBulletAttackIds.Contains(attackId))
        {
            pending.PredictedBulletAttackIds.Remove(attackId);
            em.SetComponent(entity, pending);
            return true;
        }
        return false;
    }

    /// <summary>每 tick 重传超时的待确认攻击。</summary>
    public static void TickResend(EntityManager em, Entity entity)
    {
        if (!em.HasComponent<PendingAttackComponent>(entity)) return;
        var pending = em.GetComponent<PendingAttackComponent>(entity);
        if (pending.Attacks.Count == 0) return;

        float now = Time.unscaledTime;
        var toResend = new List<AttackOperation>();
        var toRemove = new List<int>();

        for (int i = 0; i < pending.Attacks.Count; i++)
        {
            var data = pending.Attacks[i];
            if (now - data.SendTime < ResendInterval) continue;
            if (data.ResendAttempts >= MaxResendAttempts)
            {
                toRemove.Add(i);
                Debug.LogWarning($"[ClientAttackSystem] Attack {data.Attack.AttackId} timed out after {data.ResendAttempts} attempts");
                continue;
            }
            data.SendTime = now;
            data.ResendAttempts++;
            pending.Attacks[i] = data;
            toResend.Add(data.Attack);
        }

        for (int i = toRemove.Count - 1; i >= 0; i--)
            pending.Attacks.RemoveAt(toRemove[i]);
        em.SetComponent(entity, pending);

        if (toResend.Count > 0 && BattleClient.Instance != null && BattleClient.Instance.IsInBattle)
        {
            var operation = new PlayerOperation
            {
                PlayerId = BattleClient.Instance.BattlePlayerId,
                AttackOperations = toResend
            };
            BattleClient.Instance.SendOperation(operation, BattleClient.Instance.ClientFrameId);
        }
    }

    /// <summary>清空攻击状态（战斗重置）。</summary>
    public static void Clear(EntityManager em, Entity entity)
    {
        if (!em.HasComponent<PendingAttackComponent>(entity)) return;
        var pending = em.GetComponent<PendingAttackComponent>(entity);
        pending.Attacks.Clear();
        pending.PredictedBulletAttackIds.Clear();
        pending.NextAttackId = 1;
        em.SetComponent(entity, pending);
    }
}
