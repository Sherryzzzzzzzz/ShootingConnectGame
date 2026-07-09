namespace ShootingGame.Shared.ECS
{
    /// <summary>
    /// 开火冷却组件。
    /// </summary>
    public struct FireCooldownComponent
    {
        public float Cooldown;

        public FireCooldownComponent(float cooldown)
        {
            Cooldown = cooldown;
        }

        public bool CanFire => Cooldown <= 0f;
    }
}
