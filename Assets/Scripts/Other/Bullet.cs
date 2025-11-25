using UnityEngine;

public class Bullet : MonoBehaviour
{
    [Header("Basic Settings")]
    public float speed = 100f;       // 子弹飞行速度
    public float lifeTime = 5f;      // 最大生存时间
    public LayerMask hitMask;        // 碰撞检测层级

    [Header("Visual Settings")]
    // 关键：命中点向法线方向偏移的距离，防止特效生成在墙里
    private const float HIT_OFFSET = 0.05f; 

    private Vector3 _startPos;
    private Vector3 _direction;
    private float _timer;
    private bool _isActive;

    // --- 初始化/发射方法 ---
    public void Launch(Vector3 position, Vector3 direction)
    {
        // 1. 设置初始位置和方向
        transform.position = position;
        _direction = direction.normalized;
        
        // 2. 调整子弹模型朝向
        if (_direction != Vector3.zero)
        {
            transform.forward = _direction;
        }

        // 3. 重置状态
        _timer = 0;
        _isActive = true;
        gameObject.SetActive(true);
    }

    private void Update()
    {
        if (!_isActive) return;

        // 1. 生命周期管理
        _timer += Time.deltaTime;
        if (_timer >= lifeTime)
        {
            Release();
            return;
        }

        // 2. 移动与碰撞检测
        MoveAndCheckCollision();
    }

    private void MoveAndCheckCollision()
    {
        float moveDistance = speed * Time.deltaTime;
        
        // 使用 Raycast 预判这一帧的移动路径
        // 如果想检测更粗的物体，可以将 Physics.Raycast 改为 Physics.SphereCast
        if (Physics.Raycast(transform.position, _direction, out RaycastHit hit, moveDistance, hitMask))
        {
            // 命中逻辑
            transform.position = hit.point; // 移动到确切的击中点
            Hit(hit);
        }
        else
        {
            // 未命中，正常移动
            transform.position += _direction * moveDistance;
        }
    }

    private void Hit(RaycastHit hit)
    {
        // 计算特效生成位置（稍微偏离墙面）
        Vector3 effectPos = hit.point + (hit.normal * HIT_OFFSET);
        Quaternion effectRot = Quaternion.LookRotation(hit.normal);

        // 播放特效
        // 注意：这里保留了你原有的调用方式，请确保 EffectPool 存在
        if (EffectPool.Instance != null)
        {
            EffectPool.Instance.SpawnEffect("Explosion", effectPos, effectRot);
        }
        else
        {
            // 如果场景里暂时没有 EffectPool，用 Debug 线条代替，防止报错
            Debug.DrawLine(hit.point, hit.point + hit.normal, Color.red, 1.0f);
        }

        // 销毁/回收子弹
        Release();
    }

    private void Release()
    {
        if (!_isActive) return;
        _isActive = false;

        // 归还给对象池
        if (BulletManager.Instance != null)
        {
            BulletManager.Instance.ReturnBullet(this);
        }
        else
        {
            Destroy(gameObject); // 如果没有管理器，则直接销毁（兜底）
        }
    }
}