using ShootingGame.Shared.ECS;
using ShootingGame.Shared.Physics;
using ShootingGame.Shared.Simulation;

/// <summary>
/// 客户端预测系统：使用 ECS 运行客户端预测 tick。
/// </summary>
public static class ClientPredictionSystem
{
    /// <summary>
    /// 运行一个预测 tick：写入输入、执行 ECS 模拟、存储历史并返回快照。
    /// </summary>
    public static PlayerSnapshot PredictTick(
        EntityManager em, Entity entity, InputFrame input, float dt,
        CollisionWorld world,
        RingBuffer<InputFrame> inputHistory,
        RingBuffer<PlayerSnapshot> snapshotHistory,
        int currentTick)
    {
        ECSBridge.WriteInput(em, entity, input);
        inputHistory.Store(currentTick, input);

        PlayerSystemGroup.TickPlayer(em, entity, input, dt, world);

        var snap = ECSBridge.BuildSnapshot(em, entity, currentTick);
        snapshotHistory.Store(currentTick, snap);
        return snap;
    }

    /// <summary>
    /// 重新模拟从 fromTick 到 toTick（不包含）的所有帧。
    /// </summary>
    public static void Resimulate(
        EntityManager em, Entity entity,
        int fromTick, int toTick, float dt, CollisionWorld world,
        RingBuffer<InputFrame> inputHistory,
        RingBuffer<PlayerSnapshot> snapshotHistory)
    {
        for (int tick = fromTick; tick < toTick; tick++)
        {
            var input = inputHistory.Get(tick);
            if (input.Tick == tick)
                PredictTick(em, entity, input, dt, world, inputHistory, snapshotHistory, tick);
        }
    }
}
