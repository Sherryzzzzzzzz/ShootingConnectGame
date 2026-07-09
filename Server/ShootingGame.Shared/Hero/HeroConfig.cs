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
    }
}
