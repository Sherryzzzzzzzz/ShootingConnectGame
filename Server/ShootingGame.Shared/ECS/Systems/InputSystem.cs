using ShootingGame.Shared.Simulation;

namespace ShootingGame.Shared.ECS
{
    /// <summary>
    /// 将外部 InputFrame 写入实体的 InputComponent。
    /// </summary>
    public static class InputSystem
    {
        public static void Execute(EntityManager em, Entity entity, InputFrame input)
        {
            em.SetComponent(entity, new InputComponent
            {
                Movement = input.Movement,
                Jump = input.Jump,
                Run = input.Run,
                Aim = input.Aim,
                Fire = input.Fire,
                Reload = input.Reload,
                Ability1 = input.Ability1,
                Ability2 = input.Ability2,
                Ability3 = input.Ability3,
                Ability4 = input.Ability4,
                AimYaw = input.AimYaw,
                AimPitch = input.AimPitch,
                Tick = input.Tick
            });
        }
    }
}
