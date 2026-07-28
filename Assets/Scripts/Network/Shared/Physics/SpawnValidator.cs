using UnityEngine;
using ShootingGame.Shared.Math;

namespace ShootingGame.Shared.Physics
{
    /// <summary>
    /// 出生点合法性检测 + 最近合法点搜索。
    /// 替代随机出生逻辑——先检查预设点，不合法就螺旋搜索最近可用位置。
    /// </summary>
    public static class SpawnValidator
    {
        private const float PlayerRadius = PhysicsConstants.PlayerRadius;
        private const float PlayerHeight = PhysicsConstants.PlayerHeight;
        private const float SearchStep = 0.5f;   // 螺旋搜索步长（米）
        private const int MaxSearchRings = 40;    // 最大搜索圈数 = 20m 半径
        private const float CapsuleBottomOffset = 0.0f; // 胶囊底部偏移（贴地）

        /// <summary>
        /// 检查出生点是否合法（玩家胶囊不穿模）。
        /// </summary>
        public static bool IsSpawnValid(Vector3 position, CollisionWorld world)
        {
            if (world == null) return true;
            Vec3 pos = new Vec3(position.x, position.y, position.z);
            var capsule = new Capsule(
                pos + new Vec3(0, 0.01f, 0),  // 底部中心
                PlayerHeight,                    // 高度
                PlayerRadius                     // 半径
            );
            return !world.OverlapCapsule(capsule);
        }

        /// <summary>
        /// 从初始点出发，螺旋搜索最近的合法出生位置。
        /// 找不到合适位置时返回原始点并打 warning。
        /// </summary>
        public static Vector3 FindNearestValidSpawn(Vector3 desiredPos, CollisionWorld world)
        {
            if (IsSpawnValid(desiredPos, world)) return desiredPos;

            // 螺旋搜索
            for (int ring = 1; ring <= MaxSearchRings; ring++)
            {
                float radius = ring * SearchStep;
                int stepsPerRing = Mathf.Max(8, ring * 8); // 每圈检测点数随半径增大
                float angleStep = 360f / stepsPerRing;

                for (int i = 0; i < stepsPerRing; i++)
                {
                    float angle = i * angleStep * Mathf.Deg2Rad;
                    var candidate = new Vector3(
                        desiredPos.x + Mathf.Cos(angle) * radius,
                        desiredPos.y,
                        desiredPos.z + Mathf.Sin(angle) * radius
                    );

                    if (IsSpawnValid(candidate, world))
                    {
                        Debug.Log($"[Spawn] 从 ({desiredPos.x:F1},{desiredPos.z:F1}) 偏移到合法点 ({candidate.x:F1},{candidate.z:F1}) 距离={radius:F1}m 圈数={ring}");
                        return candidate;
                    }
                }
            }

            Debug.LogWarning($"[Spawn] 未找到合法出生点！({desiredPos.x:F1},{desiredPos.z:F1}) 附近 {MaxSearchRings * SearchStep:F0}m 内无可用位置，回退使用原位置");
            return desiredPos;
        }
    }
}
