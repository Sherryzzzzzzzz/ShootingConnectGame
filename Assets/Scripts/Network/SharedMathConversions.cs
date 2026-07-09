using UnityEngine;
using SharedVec3 = ShootingGame.Shared.Math.Vec3;
using SharedVec2 = ShootingGame.Shared.Math.Vec2;
using SharedQuat = ShootingGame.Shared.Math.Quat;

/// <summary>
/// Extension methods for converting between Unity and Shared math types.
/// </summary>
public static class SharedMathConversions
{
    public static SharedVec3 ToShared(this Vector3 v) => new SharedVec3(v.x, v.y, v.z);
    public static Vector3 ToUnity(this SharedVec3 v) => new Vector3(v.x, v.y, v.z);

    public static SharedVec2 ToShared(this Vector2 v) => new SharedVec2(v.x, v.y);
    public static Vector2 ToUnity(this SharedVec2 v) => new Vector2(v.x, v.y);

    public static SharedQuat ToShared(this Quaternion q) => new SharedQuat(q.x, q.y, q.z, q.w);
    public static Quaternion ToUnity(this SharedQuat q) => new Quaternion(q.x, q.y, q.z, q.w);
}
