namespace ShootingGame.Shared.Ability
{
    /// <summary>
    /// 能力实例的运行时状态。
    /// </summary>
    public enum AbilityState : byte
    {
        /// <summary>未激活</summary>
        Inactive = 0,

        /// <summary>客户端预测激活中，等待服务端确认</summary>
        Predicting = 1,

        /// <summary>活跃中（服务端已确认）</summary>
        Active = 2,

        /// <summary>正在结束（有持续时间的能力到期）</summary>
        Ending = 3,

        /// <summary>被取消（被其他能力打断或服务端拒绝）</summary>
        Cancelled = 4,
    }
}
