using ShootingGame.Shared.Math;

namespace ShootingGame.Shared.ECS
{
    /// <summary>
    /// 输入组件：当前 tick 的玩家输入。
    /// </summary>
    public struct InputComponent
    {
        public Vec2 Movement;
        public bool Jump;
        public bool Run;
        public bool Aim;
        public bool Fire;
        public bool Reload;
        public bool Crouch;
        public bool Ability1;
        public bool Ability2;
        public bool Ability3;
        public bool Ability4;
        public float AimYaw;
        public float AimPitch;
        public int Tick;
    }
}
