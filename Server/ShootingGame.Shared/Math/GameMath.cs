using System;

namespace ShootingGame.Shared.Math
{
    public static class GameMath
    {
        public const float PI = 3.14159265358979f;
        public const float Deg2Rad = PI / 180f;
        public const float Rad2Deg = 180f / PI;
        public const float Epsilon = 1e-6f;

        public static float Sqrt(float v) => (float)System.Math.Sqrt(v);
        public static float Abs(float v) => v < 0 ? -v : v;
        public static float Sin(float v) => (float)System.Math.Sin(v);
        public static float Cos(float v) => (float)System.Math.Cos(v);
        public static float Tan(float v) => (float)System.Math.Tan(v);
        public static float Asin(float v) => (float)System.Math.Asin(v);
        public static float Acos(float v) => (float)System.Math.Acos(Clamp(v, -1f, 1f));
        public static float Atan2(float y, float x) => (float)System.Math.Atan2(y, x);

        public static float Min(float a, float b) => a < b ? a : b;
        public static float Max(float a, float b) => a > b ? a : b;
        public static int Min(int a, int b) => a < b ? a : b;
        public static int Max(int a, int b) => a > b ? a : b;

        public static float Clamp(float v, float min, float max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        public static float Clamp01(float v) => Clamp(v, 0f, 1f);

        public static int Clamp(int v, int min, int max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static float LerpUnclamped(float a, float b, float t) => a + (b - a) * t;

        public static float InverseLerp(float a, float b, float value)
        {
            if (Abs(b - a) < Epsilon) return 0f;
            return Clamp01((value - a) / (b - a));
        }

        public static float MoveTowards(float current, float target, float maxDelta)
        {
            if (Abs(target - current) <= maxDelta) return target;
            return current + (target > current ? maxDelta : -maxDelta);
        }

        public static float Repeat(float t, float length)
        {
            return t - (float)System.Math.Floor(t / length) * length;
        }

        public static float DeltaAngle(float current, float target)
        {
            float delta = Repeat(target - current, 360f);
            if (delta > 180f) delta -= 360f;
            return delta;
        }
    }
}
