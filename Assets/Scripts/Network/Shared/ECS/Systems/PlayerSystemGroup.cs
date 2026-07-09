using System.Collections.Generic;
using ShootingGame.Shared.Ability;
using ShootingGame.Shared.Physics;
using ShootingGame.Shared.Simulation;

namespace ShootingGame.Shared.ECS
{
    public static class PlayerSystemGroup
    {
        public static void TickPlayer(EntityManager em, Entity entity, InputFrame input, float dt, CollisionWorld world = null)
        {
            InputSystem.Execute(em, entity, input);
            FireCooldownSystem.Execute(em, entity, dt);
            ReloadSystem.Execute(em, entity, dt);

            // 移动 + 跳跃 + 重力 + 地面检测
            MovementSystem.Execute(em, entity, dt, world);
            GravitySystem.Execute(em, entity, dt, world);
            GroundDetectionSystem.Execute(em, entity, world);

            RotationSystem.Execute(em, entity, dt);
            AmmoConsumptionSystem.Execute(em, entity);
            AbilityCooldownSystem.Execute(em, entity, dt);
            AbilityTagCheckSystem.AutoCancelBlocked(em, entity);
            AbilityLifecycleSystem.Tick(em, entity, dt);
        }

        public static void TickAll(EntityManager em, float dt, CollisionWorld world = null)
        {
            var entities = new List<Entity>();
            em.GetEntitiesWith<InputComponent>(entities);

            foreach (var entity in entities)
            {
                var input = em.GetComponent<InputComponent>(entity);
                var inputFrame = new InputFrame
                {
                    Tick = input.Tick,
                    Movement = input.Movement,
                    Jump = input.Jump,
                    Run = input.Run,
                    Aim = input.Aim,
                    Fire = input.Fire,
                    Reload = input.Reload,
                    AimYaw = input.AimYaw,
                    AimPitch = input.AimPitch
                };
                TickPlayer(em, entity, inputFrame, dt, world);
            }
        }
    }
}
