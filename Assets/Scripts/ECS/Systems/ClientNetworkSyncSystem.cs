using System.Collections.Generic;
using UnityEngine;
using ShootingGame.Network;
using ShootingGame.Shared.Ability;
using ShootingGame.Shared.ECS;
using ShootingGame.Shared.Hero;
using ShootingGame.Shared.Math;
using ShootingGame.Shared.Protocol;
using ShootingGame.Shared.Simulation;

/// <summary>
/// 客户端网络同步系统（替代 NetPlayerController 的网络 IO 部分）。
/// 职责：
///  - 每 tick：读本地玩家 ECS 输入 → 组 PlayerOperation → BattleClient.SendOperation
///  - 开火处理：校验 ECS（ClientFireSystem）→ 创建攻击（ClientAttackSystem）→ 触发开火表现
///  - 技能激活：读 ECS 输入 → ClientECSWorld.TryActivateAbility → RPC/AbilityEvents
///  - 帧接收分发：BattleClient.OnFrameReceived → 和解 / HP 同步 / 死亡 / 技能确认
/// </summary>
public static class ClientNetworkSyncSystem
{
    /// <summary>每 tick 发送本地玩家操作包。</summary>
    public static void SendLocalOperation(int tick, InputFrame input, PlayerSnapshot currentSnapshot)
    {
        var battleClient = BattleClient.Instance;
        if (battleClient == null || !battleClient.IsInBattle) return;

        var world = ClientECSWorld.Instance;
        if (world == null) return;
        var em = world.EntityManager;
        var entity = world.GetLocalPlayerEntity();
        if (!em.IsValid(entity)) return;

        var operation = new PlayerOperation
        {
            PlayerId = battleClient.BattlePlayerId,
            MoveX = input.Movement.x, MoveY = input.Movement.y,
            AimYaw = input.AimYaw, AimPitch = input.AimPitch,
            Fire = input.Fire, Jump = input.Jump,
            Run = input.Run, Aim = input.Aim,
            Reload = input.Reload,
            ClientFrameId = tick,
            PosX = currentSnapshot.Position.x,
            PosY = currentSnapshot.Position.y,
            PosZ = currentSnapshot.Position.z,
            VelX = currentSnapshot.Velocity.x,
            VelZ = currentSnapshot.Velocity.z,
            IsGrounded = currentSnapshot.IsGrounded,
            Crouch = input.Crouch
        };

        // 射击处理（校验 ECS 状态 + 创建攻击 + 触发表现）
        ProcessFire(input, currentSnapshot, operation);

        // 技能激活
        ProcessAbilities(input, operation);

        battleClient.SendOperation(operation, tick);
    }

    /// <summary>处理开火：校验/消耗 ECS → 创建 AttackOperation → 触发开火表现。</summary>
    private static void ProcessFire(InputFrame current, PlayerSnapshot snapshot, PlayerOperation operation)
    {
        if (!current.Fire) return;

        var world = ClientECSWorld.Instance;
        if (world == null) return;
        var em = world.EntityManager;
        var entity = world.GetLocalPlayerEntity();
        if (!em.IsValid(entity)) return;

        if (!ClientAttackSystem.CanFire(em, entity)) return;

        var heroConfig = world.GetHeroConfig(world.LocalPlayerId);
        var gun = heroConfig?.Gun;
        var view = GetLocalView(em, entity);

        var fireOrigin = view != null && view.firePoint != null
            ? view.firePoint.position
            : new Vector3(snapshot.Position.x, snapshot.Position.y + GameConstants.PlayerHeight * 0.85f, snapshot.Position.z);

        int frameId = current.Tick;
        float bloomHeat = GetBloomHeat(em, entity);

        // ECS 校验 + 弹药消耗 + 弹道/扩散计算
        if (!ClientFireSystem.ValidateAndConsume(em, entity,
                current.AimYaw, current.AimPitch, frameId,
                gun, world.LocalPlayerId,
                ref bloomHeat, GameConstants.TickDelta,
                out var fireDir, out float spreadDeg, out _))
        {
            SetBloomHeat(em, entity, bloomHeat);
            return;
        }
        SetBloomHeat(em, entity, bloomHeat);

        // 创建攻击（统一分配 AttackId）
        if (ClientAttackSystem.TryCreateAttack(em, entity,
                current.AimYaw, current.AimPitch, frameId, out var attack))
        {
            attack.SpawnPos = new Vec3(fireOrigin.x, fireOrigin.y, fireOrigin.z);
            operation.AttackOperations.Add(attack);
            ClientAttackSystem.MarkAttackPredicted(em, entity, attack.AttackId);

            // 开火表现（动画/枪口/弹道/视觉子弹）
            if (view != null)
            {
                view.OnShoot(current.Crouch, fireOrigin, fireDir, attack.AttackId);
            }
        }
    }

    /// <summary>处理技能激活：读 ECS 输入 → 本地预测激活 → RPC/AbilityEvents。</summary>
    private static void ProcessAbilities(InputFrame current, PlayerOperation operation)
    {
        if (!current.Ability1 && !current.Ability2 && !current.Ability3 && !current.Ability4)
            return;

        var world = ClientECSWorld.Instance;
        if (world == null) return;
        var heroConfig = world.GetHeroConfig(world.LocalPlayerId);
        if (heroConfig?.Abilities == null) return;

        for (int i = 0; i < 4 && i < heroConfig.Abilities.Length; i++)
        {
            bool pressed = i switch
            {
                0 => current.Ability1, 1 => current.Ability2,
                2 => current.Ability3, 3 => current.Ability4,
                _ => false
            };
            if (!pressed) continue;

            var abilityCfg = heroConfig.Abilities[i];
            ushort instanceId = world.TryActivateAbility(world.LocalPlayerId, abilityCfg.AssetId);
            if (instanceId <= 0) continue;

            int predictedId = 0;
            if (ProceduralEffectManager.Instance != null)
            {
                var view = world.GetLocalPlayerView();
                var pos = view != null ? view.transform.position + view.transform.up * 1.0f : Vector3.zero;
                var fwd = view != null ? view.transform.forward : Vector3.forward;
                predictedId = ProceduralEffectManager.Instance.PlayPredictedAbilityEffect(pos, fwd, new Color(0.3f, 0.8f, 1f));
            }

            var combat = world.LocalCombatBehaviour;
            if (combat != null && NetworkBehaviour.SendServerRpcTransport != null)
            {
                combat.InvokeServerRpc_RequestActivateAbility(abilityCfg.AssetId, predictedId);
            }
            else
            {
                operation.AbilityEvents.Add(new AbilityEventData
                {
                    PlayerId = (byte)world.LocalPlayerId,
                    InstanceId = instanceId,
                    AssetId = abilityCfg.AssetId,
                    EventType = AbilityEventType.RequestActivate
                });
            }
        }
    }

    /// <summary>
    /// 处理服务端帧（BattleClient.OnFrameReceived 驱动）。
    /// 包括：本地玩家和解、HP 强制同步、死亡处理、技能确认。
    /// </summary>
    public static void OnFrameReceived(AllPlayerOperation frame)
    {
        var world = ClientECSWorld.Instance;
        var battleClient = BattleClient.Instance;
        if (world == null || battleClient == null) return;
        var em = world.EntityManager;
        var entity = world.GetLocalPlayerEntity();
        int myId = battleClient.BattlePlayerId;

        bool foundSelf = false;
        foreach (var state in frame.PlayerStates)
        {
            if (state.PlayerId != myId) continue;
            foundSelf = true;

            world.HasReceivedServerFrame = true;
            DynamicTickSystem.Instance?.UpdateServerFrame(frame.FrameId);

            // 和解（委托给 ECS 和解系统）
            world.ReconcileWithServer(state, frame.FrameId);

            // 死亡处理：服务端权威判定
            if (state.IsDead && !world.IsDead)
            {
                world.SetDead();
            }

            // 强制 HP 同步
            if (state.Hp != world.CurrentSnapshot.Health)
            {
                world.CurrentSnapshot.Health = (byte)state.Hp;
                if (em.IsValid(entity) && em.TryGetComponent<HealthComponent>(entity, out var hp))
                {
                    hp.Current = (byte)state.Hp;
                    em.SetComponent(entity, hp);
                }
            }
            break;
        }

        if (!foundSelf)
        {
            var ids = string.Join(",", System.Linq.Enumerable.Select(
                frame.PlayerStates ?? new List<PlayerStateMsg>(), s => s.PlayerId));
            Debug.LogWarning($"<color=red>[RECONCILE] myId={myId} NOT FOUND! stateIds=[{ids}]</color>");
        }

        // 技能事件确认
        if (frame.AbilityEvents != null)
        {
            foreach (var evt in frame.AbilityEvents)
            {
                if (evt.PlayerId != myId) continue;
                switch (evt.EventType)
                {
                    case AbilityEventType.ConfirmActivate:
                        world.ConfirmActivate(myId, evt.InstanceId); break;
                    case AbilityEventType.RejectActivate:
                        world.RejectActivate(myId, evt.InstanceId); break;
                }
            }
        }
    }

    // ==================== 辅助 ====================

    private static PlayerAnimationView GetLocalView(EntityManager em, Entity entity)
    {
        if (!em.HasComponent<PlayerViewComponent>(entity)) return null;
        var pv = em.GetComponent<PlayerViewComponent>(entity);
        if (pv.AnimationView != null) return pv.AnimationView;
        return pv.View != null ? pv.View.GetComponent<PlayerAnimationView>() : null;
    }

    private static float GetBloomHeat(EntityManager em, Entity entity)
    {
        if (!em.HasComponent<ClientBloomComponent>(entity)) return 0f;
        return em.GetComponent<ClientBloomComponent>(entity).BloomHeat;
    }

    private static void SetBloomHeat(EntityManager em, Entity entity, float heat)
    {
        if (!em.HasComponent<ClientBloomComponent>(entity))
            em.AddComponent(entity, new ClientBloomComponent { BloomHeat = heat });
        else
        {
            var c = em.GetComponent<ClientBloomComponent>(entity);
            c.BloomHeat = heat;
            em.SetComponent(entity, c);
        }
    }
}

/// <summary>客户端扩散热度组件（Bloom，非网络同步）。</summary>
public struct ClientBloomComponent
{
    public float BloomHeat;
}
