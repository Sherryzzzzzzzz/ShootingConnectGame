namespace ShootingGame.Shared.ECS
{
    /// <summary>
    /// 换弹组件：换弹状态和计时器。
    /// </summary>
    public struct ReloadComponent
    {
        public bool IsReloading;
        public float Timer;
        /// <summary>换弹总时长(秒)，由枪械配置注入</summary>
        public float Duration;

        public ReloadComponent(bool isReloading, float timer, float duration = 2f)
        {
            IsReloading = isReloading;
            Timer = timer;
            Duration = duration;
        }
    }
}
