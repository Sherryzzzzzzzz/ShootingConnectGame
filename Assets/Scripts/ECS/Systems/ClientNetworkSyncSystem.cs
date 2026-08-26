using System;
using System.Collections.Generic;
using UnityEngine;
using ShootingGame.Network;
using ShootingGame.Shared.Ability;
using ShootingGame.Shared.ECS;
using ShootingGame.Shared.GameplayTags;
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
    private static Func<ClientECSWorld> _worldProvider;
    private static Func<BattleClient> _battleClientProvider;
    private static Action<int> _serverFrameConsumer;
    private static readonly Dictionary<ushort, int> AbilityEffectIds = new Dictionary<ushort, int>();
    private static int _lastMissingSelfWarningFrame = -60;

    public static void Configure(
        Func<ClientECSWorld> worldProvider,
        Func<BattleClient> battleClientProvider,
        Action<int> serverFrameConsumer = null)
    {
        _worldProvider = worldProvider;
        _battleClientProvider = battleClientProvider;
        _serverFrameConsumer = serverFrameConsumer;
        AbilityEffectIds.Clear();
        _lastMissingSelfWarningFrame = -60;
    }

    private static ClientECSWorld GetWorld() => _worldProvider != null ? _worldProvider() : null;
    private static BattleClient GetBattleClient() => _battleClientProvider != null ? _battleClientProvider() : null;

    /// <summary>每 tick 发送本地玩家操作包。</summary>
    public static void SendLocalOperation(int tick, InputFrame input, PlayerSnapshot currentSnapshot)
    {
        var battleClient = GetBattleClient();
        if (battleClient == null || !battleClient.IsInBattle) return;

        var world = GetWorld();
        if (world == null) return;
        var em = world.EntityManager;
        var entity = world.GetLocalPlayerEntity();
        if (!em.IsValid(entity)) return;

        var operation = new PlayerOperation
        {
            PlayerId = battleClient.BattlePlayerId,
            MoveX = input.Movement.x, MoveY = input.Movement.y,
            AimYaw = input.AimYaw, AimPitch = input.AimPitch,
            Fire = false, Jump = input.Jump,
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
        var world = GetWorld();
        if (world == null) return;
        var em = world.EntityManager;
        var entity = world.GetLocalPlayerEntity();
        if (!em.IsValid(entity)) return;

        var gun = world.GetPlayerGun(world.LocalPlayerId);
        var fireMode = gun != null ? gun.FireMode : FireMode.Single;
        var fireState = em.TryGetComponent<ClientWeaponFireState>(entity, out var existingFireState)
            ? existingFireState
            : default;
        bool shouldFire = ClientWeaponFireGate.ShouldFire(
            ref fireState, fireMode, current.Fire, current.Aim, current.Tick);

        if (em.HasComponent<ClientWeaponFireState>(entity))
            em.SetComponent(entity, fireState);
        else
            em.AddComponent(entity, fireState);

        if (!shouldFire || !ClientAttackSystem.CanFire(em, entity)) return;

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
            bool weaponReady = fireState.WeaponDrawn && current.Tick >= fireState.WeaponReadyTick;
            bool moving = current.Movement.x * current.Movement.x
                + current.Movement.y * current.Movement.y > 0.01f;
            ClientWeaponFireGate.CommitFire(ref fireState, fireMode);
            em.SetComponent(entity, fireState);
            operation.Fire = true;
            attack.SpawnPos = new Vec3(fireOrigin.x, fireOrigin.y, fireOrigin.z);
            operation.AttackOperations.Add(attack);
            ClientAttackSystem.MarkAttackPredicted(em, entity, attack.AttackId);

            // 开火表现（动画/枪口/弹道/视觉子弹）
            if (view != null)
            {
                view.OnShoot(
                    current.Crouch,
                    weaponReady && moving,
                    fireOrigin,
                    fireDir,
                    attack.AttackId);
            }
        }
    }

    /// <summary>处理技能激活：读 ECS 输入 → 本地预测激活 → RPC/AbilityEvents。</summary>
    private static void ProcessAbilities(InputFrame current, PlayerOperation operation)
    {
        if (!current.Ability1 && !current.Ability2 && !current.Ability3 && !current.Ability4)
            return;

        var world = GetWorld();
        if (world == null) return;
        var heroConfig = world.GetHeroConfig(world.LocalPlayerId);
        if (heroConfig?.Abilities == null) return;

        var skills = new List<AbilityConfig>(4);
        foreach (var ability in heroConfig.Abilities)
            if (ability != null && ability.AssetId >= 10)
                skills.Add(ability);

        for (int i = 0; i < 4 && i < skills.Count; i++)
        {
            bool pressed = i switch
            {
                0 => current.Ability1, 1 => current.Ability2,
                2 => current.Ability3, 3 => current.Ability4,
                _ => false
            };
            if (!pressed) continue;

            var abilityCfg = skills[i];
            ushort instanceId = world.TryActivateAbility(world.LocalPlayerId, abilityCfg.AssetId);
            if (instanceId <= 0) continue;

            var localAnimation = world.GetLocalPlayerView();
            if (localAnimation != null)
                localAnimation.CameraDirector?.PlayAbilityCue(abilityCfg.AssetId, abilityCfg.Duration);

            int predictedId = 0;
            if (ProceduralEffectManager.Instance != null)
            {
                var view = world.GetLocalPlayerView();
                var pos = view != null ? view.transform.position + view.transform.up * 1.0f : Vector3.zero;
                var fwd = view != null ? view.transform.forward : Vector3.forward;
                predictedId = ProceduralEffectManager.Instance.PlayPredictedAbilityEffect(pos, fwd, new Color(0.3f, 0.8f, 1f));
            }
            if (predictedId > 0)
                AbilityEffectIds[instanceId] = predictedId;

            // Ability activation travels with PlayerOperation on every transport.  The
            // dedicated server already consumes this path, and using it for the editor
            // host prevents the request from being lost in an unbound ServerRpc view.
            operation.AbilityEvents.Add(new AbilityEventData
            {
                PlayerId = (byte)world.LocalPlayerId,
                InstanceId = instanceId,
                AssetId = abilityCfg.AssetId,
                EventType = AbilityEventType.RequestActivate
            });
        }
    }

    /// <summary>
    /// 处理服务端帧（BattleClient.OnFrameReceived 驱动）。
    /// 包括：本地玩家和解、HP 强制同步、死亡处理、技能确认。
    /// </summary>
    public static void OnFrameReceived(AllPlayerOperation frame)
    {
        if (frame == null) return;

        var world = GetWorld();
        var battleClient = GetBattleClient();
        if (world == null || battleClient == null) return;
        var em = world.EntityManager;
        var entity = world.GetLocalPlayerEntity();
        int myId = battleClient.BattlePlayerId;

        bool foundSelf = false;
        foreach (var state in frame.PlayerStates ?? new List<PlayerStateMsg>())
        {
            if (state.PlayerId == myId)
            {
                foundSelf = true;
                bool wasDead = world.IsDead;
                int oldHp = ReadHealth(em, entity, state.Hp);

                world.HasReceivedServerFrame = true;
                _serverFrameConsumer?.Invoke(frame.FrameId);

                // 和解（委托给 ECS 和解系统）
                world.ReconcileWithServer(state, frame.FrameId);

                // 死亡/复活处理：服务端权威判定。
                if (state.IsDead && !wasDead)
                {
                    world.SetDead();
                }
                else if (!state.IsDead && wasDead)
                {
                    world.Revive(new Vector3(state.Position.x, state.Position.y, state.Position.z));
                    RequestViewSnap(em, entity);
                }

                // 强制 HP 同步
                if (state.Hp != world.CurrentSnapshot.Health)
                {
                    world.SetCurrentSnapshotHealth((byte)state.Hp);
                    if (em.IsValid(entity) && em.TryGetComponent<HealthComponent>(entity, out var hp))
                    {
                        hp.Current = (byte)state.Hp;
                        em.SetComponent(entity, hp);
                    }
                }

                SyncTags(em, entity, state.TagBitmask);
                PublishPlayerStateChanges(state, oldHp, wasDead);

                continue;
            }

            var remoteEntity = world.GetPlayerEntity(state.PlayerId);
            if (!em.IsValid(remoteEntity))
                continue;

            int oldRemoteHp = ReadHealth(em, remoteEntity, state.Hp);
            bool remoteWasDead = ReadRemoteDeath(em, remoteEntity, oldRemoteHp);
            ClientRemoteInterpolationSystem.CacheFrame(em, remoteEntity, state);
            ClientRemoteInterpolationSystem.SyncHp(em, remoteEntity, state.Hp);
            SyncTags(em, remoteEntity, state.TagBitmask);
            if (em.HasComponent<PlayerStateComponent>(remoteEntity))
            {
                em.SetComponent(remoteEntity,
                    new PlayerStateComponent((PlayerStateEnum)state.StateEnum));
            }
            PublishPlayerStateChanges(state, oldRemoteHp, remoteWasDead);
        }

        if (!foundSelf && frame.FrameId - _lastMissingSelfWarningFrame >= 60)
        {
            _lastMissingSelfWarningFrame = frame.FrameId;
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
                        ResolveAbility(evt.InstanceId, 0, true); break;
                    case AbilityEventType.RejectActivate:
                        ResolveAbility(evt.InstanceId, 0, false); break;
                }
            }
        }

        ProcessAuthoritativeAttacks(frame, em, entity, myId);
    }

    public static void ResolveAbility(ushort localInstanceId, ushort serverInstanceId, bool accepted)
    {
        var world = GetWorld();
        if (world == null || localInstanceId == 0)
            return;

        if (accepted)
            world.ConfirmActivate(world.LocalPlayerId, localInstanceId);
        else
            world.RejectActivate(world.LocalPlayerId, localInstanceId);

        if (!AbilityEffectIds.TryGetValue(localInstanceId, out int effectId))
            return;

        AbilityEffectIds.Remove(localInstanceId);
        ClientPresentationEventBus.PublishAbilityResolved(effectId, serverInstanceId, accepted);
    }

    private static void ProcessAuthoritativeAttacks(
        AllPlayerOperation frame,
        EntityManager em,
        Entity localEntity,
        int localPlayerId)
    {
        if (frame.Operations == null)
            return;

        foreach (var operation in frame.Operations)
        {
            if (operation?.AttackOperations == null)
                continue;

            foreach (var attack in operation.AttackOperations)
            {
                bool wasPredicted = false;
                if (operation.PlayerId == localPlayerId && em.IsValid(localEntity))
                {
                    wasPredicted = ClientAttackSystem.TryConsumePredictedAttack(
                        em, localEntity, attack.AttackId);
                    ClientAttackSystem.ConfirmAttack(em, localEntity, attack.AttackId);
                }

                if (wasPredicted)
                    continue;

                bool hasFallbackOrigin = TryGetFrameFireOrigin(
                    frame, operation.PlayerId, out var fallbackOrigin);
                ClientPresentationEventBus.PublishAuthorityAttack(
                    attack,
                    operation.PlayerId,
                    frame.FrameId,
                    fallbackOrigin,
                    hasFallbackOrigin);
            }
        }
    }

    private static int ReadHealth(EntityManager em, Entity entity, int fallback)
    {
        return em.IsValid(entity)
            && em.TryGetComponent<HealthComponent>(entity, out var health)
            ? health.Current
            : fallback;
    }

    private static bool ReadRemoteDeath(EntityManager em, Entity entity, int health)
    {
        if (em.TryGetComponent<PlayerViewComponent>(entity, out var view) && view.HasTarget)
            return !view.LastKnownAlive;
        return health <= 0;
    }

    private static void PublishPlayerStateChanges(PlayerStateMsg state, int oldHp, bool wasDead)
    {
        if (oldHp != state.Hp)
            ClientPresentationEventBus.PublishPlayerHealthChanged(state.PlayerId, state.Hp);
        if (!wasDead && state.IsDead)
            ClientPresentationEventBus.PublishPlayerDied(state.PlayerId);
    }

    private static void SyncTags(EntityManager em, Entity entity, long tagBitmask)
    {
        if (!em.IsValid(entity))
            return;

        if (em.TryGetComponent<TagComponent>(entity, out var tags))
        {
            tags.TagBitMask = tagBitmask;
            em.SetComponent(entity, tags);
        }
        else
        {
            em.AddComponent(entity, new TagComponent(new TagContainer
            {
                EffectiveMask = tagBitmask
            }));
        }
    }

    private static void RequestViewSnap(EntityManager em, Entity entity)
    {
        if (!em.IsValid(entity)
            || !em.TryGetComponent<PlayerViewComponent>(entity, out var view))
            return;

        view.SnapTransform = true;
        em.SetComponent(entity, view);
    }

    private static bool TryGetFrameFireOrigin(
        AllPlayerOperation frame,
        int playerId,
        out Vec3 origin)
    {
        if (frame.PlayerStates != null)
        {
            foreach (var state in frame.PlayerStates)
            {
                if (state.PlayerId != playerId)
                    continue;

                origin = new Vec3(
                    state.Position.x,
                    state.Position.y + GameConstants.PlayerHeight * 0.85f,
                    state.Position.z);
                return true;
            }
        }

        origin = default;
        return false;
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
