using UnityEngine;
using ShootingGame.Shared.ECS;
using ShootingGame.Shared.Math;

/// <summary>
/// 客户端视觉同步系统（替代 NetPlayerController.UpdateVisualSmoothing）。
/// 每帧：读 ECS TransformComponent → 平滑驱动 GameObject transform。
/// 纯表现层：不修改任何 ECS 模拟数据。
/// </summary>
public static class ClientVisualSyncSystem
{
    private const float SmoothingSpeed = 30f;

    /// <summary>将本地玩家 ECS 状态平滑应用到 GameObject（本地玩家每帧调用）。</summary>
    public static void SyncLocalView(EntityManager em, Entity entity, Transform viewTransform)
    {
        if (viewTransform == null) return;
        if (!em.TryGetComponent<TransformComponent>(entity, out var tx)) return;

        Vector3 targetPos = tx.Position.ToUnity();
        Quaternion targetRot = tx.Rotation.ToUnity();

        float t = 1f - Mathf.Exp(-SmoothingSpeed * Time.deltaTime);
        Vector3 pos = Vector3.Lerp(viewTransform.position, targetPos, t);
        Quaternion rot = Quaternion.Slerp(viewTransform.rotation, targetRot, t);
        viewTransform.position = pos;
        viewTransform.rotation = rot;
    }

    /// <summary>直接写入（远程玩家插值输出后调用，避免双重平滑）。</summary>
    public static void ApplyDirect(EntityManager em, Entity entity, Transform viewTransform)
    {
        if (viewTransform == null) return;
        if (!em.TryGetComponent<TransformComponent>(entity, out var tx)) return;
        viewTransform.position = tx.Position.ToUnity();
        viewTransform.rotation = tx.Rotation.ToUnity();
    }
}
