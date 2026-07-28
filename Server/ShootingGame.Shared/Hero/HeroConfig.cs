namespace ShootingGame.Shared.Hero
{
    public class HeroConfig
    {
        public int HeroId;
        public string Name;
        public byte MaxHP;
        public float MoveSpeed;
        public float PlayerRadius;
        public float PlayerHeight;
        public Ability.AbilityConfig[] Abilities;

        /// <summary>初始枪械 ID（GunConfig SO 资产名），经 GunRegistry 解析</summary>
        public string StartingGunId;

        /// <summary>初始枪械模拟数据（由配置加载器填充，可能为 null——使用前经 GunRegistry.GetGun 兜底）</summary>
        public GunConfigData Gun;
    }
}
