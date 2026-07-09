namespace ShootingGame.Shared.ECS
{
    /// <summary>
    /// 生命值组件。
    /// </summary>
    public struct HealthComponent
    {
        public byte Current;
        public byte Max;

        public HealthComponent(byte current, byte max)
        {
            Current = current;
            Max = max;
        }

        public bool IsDead => Current == 0;
        public bool IsAlive => Current > 0;
    }
}
