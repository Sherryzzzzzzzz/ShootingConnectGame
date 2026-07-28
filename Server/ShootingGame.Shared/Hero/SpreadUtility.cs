using ShootingGame.Shared.Math;

namespace ShootingGame.Shared.Hero
{
    /// <summary>
    /// 射击扩散工具（双端共用）。
    /// 服务器用它决定真实弹道，客户端用同样的种子/算法生成预测视觉弹道，保证一致。
    /// 注意：不能用 System.Random —— Mono(Unity) 与 .NET(Core) 的实现不同，
    /// 跨端结果不一致。这里用 xorshift32 保证双端逐位一致。
    /// </summary>
    public static class SpreadUtility
    {
        private const float Deg2Rad = 0.017453292f;

        /// <summary>xorshift32 确定性 PRNG</summary>
        public static uint NextUInt(ref uint state)
        {
            if (state == 0) state = 0x9E3779B9u;
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state;
        }

        public static float NextFloat01(ref uint state)
        {
            return (NextUInt(ref state) & 0xFFFFFF) / 16777216f;
        }

        /// <summary>
        /// 计算当前总散射角（度）：基础 + 移动惩罚 + 连发 bloom。
        /// </summary>
        public static float ComputeTotalSpread(GunConfigData gun, bool isMoving, float bloomHeat)
        {
            if (gun == null) return 0f;
            float total = gun.SpreadAngle + bloomHeat;
            if (isMoving) total += gun.MoveSpreadAdd;
            return total;
        }

        /// <summary>
        /// 在 dir 周围的圆锥内按 seed 确定性偏转（spreadDeg 为最大偏转角）。
        /// 半径开方分布（中心密集），与常见 FPS 手感一致。
        /// </summary>
        public static Vec3 ApplyConeSpread(Vec3 dir, float spreadDeg, int seed)
        {
            if (spreadDeg <= 0.0001f) return dir;

            uint state = (uint)seed;
            float angle = NextFloat01(ref state) * 6.2831853f;
            float radius = GameMath.Sqrt(NextFloat01(ref state));
            float tilt = spreadDeg * radius * Deg2Rad;

            // 构造与 dir 正交的基
            Vec3 refUp = GameMath.Abs(dir.y) < 0.99f ? Vec3.Up : Vec3.Right;
            Vec3 right = Vec3.Cross(refUp, dir).Normalized;
            Vec3 up2 = Vec3.Cross(dir, right).Normalized;

            float tanTilt = GameMath.Tan(tilt);
            Vec3 offset = right * (GameMath.Cos(angle) * tanTilt) + up2 * (GameMath.Sin(angle) * tanTilt);
            return (dir + offset).Normalized;
        }

        /// <summary>生成双端一致的弹道种子</summary>
        public static int MakeSeed(int attackId, int playerId)
        {
            return unchecked(attackId * 397 ^ (playerId * 31 + 17));
        }
    }
}
