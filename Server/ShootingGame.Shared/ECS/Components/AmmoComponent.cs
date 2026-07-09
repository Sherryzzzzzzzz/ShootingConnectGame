namespace ShootingGame.Shared.ECS
{
    /// <summary>
    /// 弹药组件：当前弹药和最大弹药。
    /// </summary>
    public struct AmmoComponent
    {
        public int Current;
        public int Max;

        public AmmoComponent(int current, int max)
        {
            Current = current;
            Max = max;
        }

        public bool IsEmpty => Current <= 0;
        public bool IsFull => Current >= Max;
    }
}
