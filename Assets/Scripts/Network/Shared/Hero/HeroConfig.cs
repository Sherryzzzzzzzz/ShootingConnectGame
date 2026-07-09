namespace ShootingGame.Shared.Hero
{
    /// <summary>
    /// 英雄配置（纯数据类）。实例由 HeroRegistry 在运行时创建。
    /// </summary>
    public class HeroConfig
    {
        public int HeroId;
        public string Name;
        public byte MaxHP;
        public float MoveSpeed;
        public float PlayerRadius;
        public float PlayerHeight;
        public Ability.AbilityConfig[] Abilities;

        /// <summary>关联的枪械配置（从 Resources 加载的 ScriptableObject）</summary>
        public GunConfig StartingGun;
    }
}
