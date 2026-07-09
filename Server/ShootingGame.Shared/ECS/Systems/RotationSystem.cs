using ShootingGame.Shared.Math;
using ShootingGame.Shared.Simulation;

namespace ShootingGame.Shared.ECS
{
    /// <summary>
    /// 旋转系统：平滑旋转实体朝向瞄准方向。
    /// </summary>
    public static class RotationSystem
    {
        public static void Execute(EntityManager em, Entity entity, float dt)
        {
            if (!em.HasComponent<InputComponent>(entity)) return;
            if (!em.HasComponent<TransformComponent>(entity)) return;

            var input = em.GetComponent<InputComponent>(entity);
            var transform = em.GetComponent<TransformComponent>(entity);

            Quat targetRot = Quat.Euler(0f, input.AimYaw, 0f);
            transform.Rotation = Quat.RotateTowards(transform.Rotation, targetRot, GameConstants.RotationSpeed * dt);

            em.SetComponent(entity, transform);
        }
    }
}
