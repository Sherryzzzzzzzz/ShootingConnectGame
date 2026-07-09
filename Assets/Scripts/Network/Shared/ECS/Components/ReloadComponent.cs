namespace ShootingGame.Shared.ECS
{
    /// <summary>
    /// 换弹组件：换弹状态和计时器。
    /// </summary>
    public struct ReloadComponent
    {
        public bool IsReloading;
        public float Timer;

        public ReloadComponent(bool isReloading, float timer)
        {
            IsReloading = isReloading;
            Timer = timer;
        }
    }
}
