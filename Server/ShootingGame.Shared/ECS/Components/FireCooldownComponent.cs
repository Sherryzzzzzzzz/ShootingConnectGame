namespace ShootingGame.Shared.ECS
{
    /// <summary>
    /// 开火冷却组件。
    /// </summary>
    public struct FireCooldownComponent
    {
        public float Cooldown;
        /// <summary>射击间隔(秒)，由枪械配置注入</summary>
        public float Rate;

        public FireCooldownComponent(float cooldown, float rate = 0.15f)
        {
            Cooldown = cooldown;
            Rate = rate;
        }

        public bool CanFire => Cooldown <= 0f;
    }
}
