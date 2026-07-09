using ShootingGame.Network;
using ShootingGame.Shared.ECS.Components;

namespace ShootingGame.Shared.ECS
{
    /// <summary>
    /// 生命值组件。NetVar 同步 Current 字段。
    /// </summary>
    [SyncComponent]
    public partial struct HealthComponent
    {
        [SyncVar]
        public byte Current;
        public byte Max;

        /// <summary>Dirty tracker for network incremental sync.</summary>
        public DirtyTracker Dirty;

        public HealthComponent(byte current, byte max) : this()
        {
            Current = current;
            Max = max;
        }

        public bool IsDead => Current == 0;
        public bool IsAlive => Current > 0;
    }
}
