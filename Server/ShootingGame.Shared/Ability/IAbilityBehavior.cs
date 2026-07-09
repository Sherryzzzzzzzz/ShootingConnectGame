using ShootingGame.Shared.ECS;

namespace ShootingGame.Shared.Ability
{
    /// <summary>
    /// 能力行为接口。每种能力类型实现一个具体的 Behavior。
    /// 在共享库中以纯 C# 实现，服务端和客户端共享。
    /// </summary>
    public interface IAbilityBehavior
    {
        /// <summary>
        /// 检查当前是否可以激活（弹药、状态等额外条件）。
        /// 标签检查已由 AbilityTagCheckSystem 完成。
        /// </summary>
        bool CanActivate(EntityManager em, Entity entity, AbilityConfig config);

        /// <summary>
        /// 能力激活时调用。
        /// </summary>
        void OnActivate(EntityManager em, Entity entity, AbilityConfig config);

        /// <summary>
        /// 每 tick 更新（仅 Active 状态）。
        /// </summary>
        void OnUpdate(EntityManager em, Entity entity, AbilityConfig config, float dt);

        /// <summary>
        /// 能力正常结束时调用（Duration 到期或主动结束）。
        /// </summary>
        void OnDeactivate(EntityManager em, Entity entity, AbilityConfig config);

        /// <summary>
        /// 能力被取消时调用（被打断或预测拒绝）。
        /// </summary>
        void OnCancel(EntityManager em, Entity entity, AbilityConfig config);
    }
}
