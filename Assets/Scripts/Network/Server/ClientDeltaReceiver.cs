using System;
using System.Collections.Generic;
using ShootingGame.Shared.ECS;
using ShootingGame.Shared.Protocol;
using ShootingGame.Shared.Simulation;
using UnityEngine;

namespace ShootingGame.Network.Server
{
    /// <summary>
    /// 客户端侧 I/P帧 接收器。
    ///
    /// 职责：
    /// - 接收服务端发送的 DeltaStateMsg（I帧或P帧）
    /// - I帧：全量覆盖本地 ECS 状态
    /// - P帧：根据 baseFrameId 判断是否可以应用增量
    /// - 状态不一致时自动请求 I帧
    /// </summary>
    public class ClientDeltaReceiver
    {
        /// <summary>上次成功应用的 I帧 tick</summary>
        public int LastFullFrameId { get; private set; } = -1;

        /// <summary>连续 P帧 mismatch 计数</summary>
        public int MismatchCount { get; private set; }

        /// <summary>P帧 mismatch 阈值（超过此值自动请求 I帧）</summary>
        public int MismatchThreshold { get; set; } = 3;

        /// <summary>关联的 ECS EntityManager</summary>
        private readonly EntityManager _entityManager;

        /// <summary>I帧请求回调（发送给服务端）</summary>
        private Action? _onRequestIFrame;

        /// <summary>NetId → Entity 映射</summary>
        private readonly Dictionary<uint, Entity> _netIdToEntity = new Dictionary<uint, Entity>();

        /// <summary>Entity → NetId 映射</summary>
        private readonly Dictionary<Entity, uint> _entityToNetId = new Dictionary<Entity, uint>();

        public ClientDeltaReceiver(EntityManager entityManager)
        {
            _entityManager = entityManager;
        }

        /// <summary>
        /// 设置 I帧请求回调。
        /// </summary>
        public void SetIFrameRequestCallback(Action callback) => _onRequestIFrame = callback;

        /// <summary>
        /// 注册本地 Entity → NetId 映射。
        /// </summary>
        public void RegisterEntity(Entity entity, uint netId)
        {
            _netIdToEntity[netId] = entity;
            _entityToNetId[entity] = netId;
        }

        /// <summary>
        /// 移除映射。
        /// </summary>
        public void UnregisterEntity(uint netId)
        {
            if (_netIdToEntity.TryGetValue(netId, out var entity))
            {
                _entityToNetId.Remove(entity);
                _netIdToEntity.Remove(netId);
            }
        }

        /// <summary>
        /// 处理收到的 DeltaStateMsg。
        /// </summary>
        public void OnDeltaStateReceived(DeltaStateMsg msg)
        {
            if (msg.IsFull)
            {
                ApplyIFrame(msg);
            }
            else
            {
                ApplyPFrame(msg);
            }
        }

        /// <summary>
        /// 应用 I帧（全量覆盖）。
        /// </summary>
        private void ApplyIFrame(DeltaStateMsg msg)
        {
            LastFullFrameId = msg.ServerTick;
            MismatchCount = 0;

            foreach (var entityDelta in msg.Entities)
            {
                if (!_netIdToEntity.TryGetValue(entityDelta.NetId, out var entity))
                {
                    // 新 Entity —— 远程玩家首次出现
                    entity = _entityManager.CreateEntity();
                    _netIdToEntity[entityDelta.NetId] = entity;
                    _entityToNetId[entity] = entityDelta.NetId;
                }

                foreach (var compDelta in entityDelta.Components)
                {
                    ApplyComponentFull(entity, compDelta);
                }
            }
        }

        /// <summary>
        /// 应用 P帧（增量变更）。
        /// </summary>
        private void ApplyPFrame(DeltaStateMsg msg)
        {
            if (msg.BaseFrameId != LastFullFrameId)
            {
                // P帧基准帧不匹配 → 丢弃，等待下一个 I帧
                MismatchCount++;

                if (MismatchCount >= MismatchThreshold)
                {
                    Debug.Log($"[ClientDeltaReceiver] {MismatchCount} consecutive P-frame mismatches, requesting I-frame");
                    _onRequestIFrame?.Invoke();
                }
                return;
            }

            MismatchCount = 0;

            foreach (var entityDelta in msg.Entities)
            {
                if (!_netIdToEntity.TryGetValue(entityDelta.NetId, out var entity))
                    continue; // 未知 Entity，跳过（等 I帧）

                foreach (var compDelta in entityDelta.Components)
                {
                    ApplyComponentDelta(entity, compDelta);
                }
            }
        }

        /// <summary>
        /// 应用全量 Component 数据（简化实现，后续由 Source Generator 生成的 ReadFull 替代）。
        /// </summary>
        private void ApplyComponentFull(Entity entity, ComponentDelta compDelta)
        {
            // 当前简化实现：解析 PlayerSnapshot 格式的整包数据
            if (compDelta.ComponentTypeId == 0 && compDelta.Data != null)
            {
                var r = new PacketReader(compDelta.Data);
                // 解析临时 PlayerSnapshot 格式
                var snap = new PlayerSnapshot
                {
                    Tick = r.ReadInt32(),
                    Position = r.ReadVec3(),
                    Rotation = r.ReadQuat(),
                    Velocity = r.ReadVec3(),
                    VerticalVelocity = r.ReadFloat(),
                    IsGrounded = r.ReadBool(),
                    State = (PlayerStateEnum)r.ReadByte(),
                    FireCooldown = r.ReadFloat(),
                    Health = r.ReadByte(),
                    CurrentAmmo = r.ReadInt32(),
                    IsReloading = r.ReadBool(),
                    ReloadTimer = r.ReadFloat(),
                    TagBitmask = r.ReadInt64()
                };
                ECSBridge.ApplyServerCorrection(_entityManager, entity, snap);
            }
        }

        /// <summary>
        /// 应用增量 Component 数据（后续由 Source Generator 生成的 ReadDelta 替代）。
        /// </summary>
        private void ApplyComponentDelta(Entity entity, ComponentDelta compDelta)
        {
            // 占位：后续由 Source Generator 生成的 Component.ReadDelta() 处理
            // 当前 P帧 暂不处理 Component 级别增量
        }

        /// <summary>
        /// 重置状态。
        /// </summary>
        public void Reset()
        {
            LastFullFrameId = -1;
            MismatchCount = 0;
            _netIdToEntity.Clear();
            _entityToNetId.Clear();
        }
    }
}
