using ShootingGame.Shared.Math;

namespace ShootingGame.Shared.Simulation
{
    public struct InputFrame
    {
        public int Tick;
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
    }
}
