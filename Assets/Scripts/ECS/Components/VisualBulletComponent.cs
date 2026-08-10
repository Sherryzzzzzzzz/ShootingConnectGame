using UnityEngine;

/// <summary>
/// 视觉子弹组件（客户端专用）。
/// 纯表现子弹：方向/速度/生命周期/飞行距离。碰撞判定由服务端负责。
/// GameObject 从 VisualBulletManager 对象池取得，系统驱动更新。
/// </summary>
public struct VisualBulletComponent
{
    /// <summary>子弹 GameObject（池中对象）。</summary>
    public GameObject GameObject;
    public Transform Transform;

    public Vector3 Direction;
    public float Speed;
    public float Time;
    public float TraveledDistance;
    public float MaxDistance;
    public float Lifetime;

    /// <summary>攻击 ID（去重/命中移除用）。</summary>
    public int AttackId;
    public int AttackerId;
}
