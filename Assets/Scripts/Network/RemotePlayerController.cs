using System.Collections.Generic;
using UnityEngine;
using ShootingGame.Shared.ECS;
using ShootingGame.Shared.Hero;
using ShootingGame.Shared.Protocol;
using ShootingGame.Shared.Simulation;
using SharedVec3 = ShootingGame.Shared.Math.Vec3;

/// <summary>
/// 远程玩家表现薄壳（客户端专用）。
/// ECS 化后瘦身：插值/状态同步已迁入 ClientRemoteInterpolationSystem + PlayerViewComponent。
/// 本类只保留纯表现职责：队伍颜色、可见性、死亡标记、调试查询 API。
/// 渲染位置/旋转由 ClientECSWorld.UpdatePresentation 从 ECS TransformComponent 驱动。
/// </summary>
public class RemotePlayerController : MonoBehaviour
{
    [Header("玩家标识")] [SerializeField] private int playerId = -1;
    [SerializeField] private int teamId = 0;
    [Header("引用")] [SerializeField] private Renderer[] renderers;

    private MaterialPropertyBlock _propBlock;
    private bool _isDead;
    private int _currentHp = 100;
    private static readonly int ColorProp = Shader.PropertyToID("_Color");
    private static readonly Dictionary<int, RemotePlayerController> _all = new();
    public static IReadOnlyDictionary<int, RemotePlayerController> All => _all;

    public int PlayerId => playerId; public int TeamId => teamId;
    public int CurrentHp => _currentHp; public bool IsDead => _isDead;
    public bool IsAiming => GetRemoteViewFlag(v => v.IsAiming);
    public bool IsCrouching => GetRemoteViewFlag(v => v.IsCrouching);
    public bool FireTrigger { get; set; }

    /// <summary>当前渲染快照（从 ECS PlayerViewComponent 构建，调试用）。</summary>
    public PlayerSnapshot CurrentSnapshot
    {
        get
        {
            var world = ClientECSWorld.Instance;
            if (world == null) return default;
            var entity = world.GetPlayerEntity(playerId);
            if (!world.EntityManager.IsValid(entity)) return default;
            return ECSBridge.BuildSnapshot(world.EntityManager, entity, 0);
        }
    }

    public SharedVec3 RenderedVelocity => GetRemoteViewValue(v => v.RenderedVelocity, SharedVec3.Zero);
    public bool RenderedIsGrounded => GetRemoteViewFlag(v => v.RenderedIsGrounded);

    public static RemotePlayerController GetPlayer(int id) => _all.TryGetValue(id, out var c) ? c : null;

    private bool GetRemoteViewFlag(System.Func<PlayerViewComponent, bool> getter)
    {
        var world = ClientECSWorld.Instance;
        if (world == null) return false;
        var entity = world.GetPlayerEntity(playerId);
        if (!world.EntityManager.TryGetComponent(entity, out PlayerViewComponent pv)) return false;
        return getter(pv);
    }

    private SharedVec3 GetRemoteViewValue(System.Func<PlayerViewComponent, SharedVec3> getter, SharedVec3 fallback)
    {
        var world = ClientECSWorld.Instance;
        if (world == null) return fallback;
        var entity = world.GetPlayerEntity(playerId);
        if (!world.EntityManager.TryGetComponent(entity, out PlayerViewComponent pv)) return fallback;
        return getter(pv);
    }

    private void Awake()
    {
        _propBlock = new MaterialPropertyBlock();
        var anim = GetComponent<Animator>();
        if (anim != null) anim.applyRootMotion = false;
    }

    private void OnDestroy()
    {
        if (playerId >= 0) { _all.Remove(playerId); ClientECSWorld.Instance?.UnregisterPlayer(playerId); }
    }

    /// <summary>初始化（由表现层调用；ECS 实体由 ClientECSWorld.RegisterRemotePlayer 创建）。</summary>
    public void Initialize(int id, int team, Vector3 spawnPos, HeroConfig hc = null)
    {
        playerId = id; teamId = team;
        int maxHp = hc?.MaxHP ?? GameConstants.MaxHealth;
        _currentHp = maxHp;
        _isDead = false;
        transform.position = spawnPos;
        if (playerId >= 0) _all[playerId] = this;
        if (renderers == null || renderers.Length == 0)
            renderers = GetComponentsInChildren<Renderer>();
    }

    public void SetTargetPosition(Vector3 pos) { /* 插值已迁入 ECS 系统，保持空实现兼容旧调用 */ }
    public void SetHp(int hp) => _currentHp = Mathf.Max(0, hp);
    public void SetDead() => _isDead = true;
    public void SetVisible(bool v) { if (renderers != null) foreach (var r in renderers) if (r != null) r.enabled = v; }
    public void SetTeamColor(Color c)
    {
        if (_propBlock == null) _propBlock = new MaterialPropertyBlock();
        _propBlock.SetColor(ColorProp, c);
        if (renderers != null) foreach (var r in renderers) if (r != null) r.SetPropertyBlock(_propBlock);
    }
    public void Revive(Vector3 pos)
    {
        _isDead = false;
        _currentHp = 100;
        transform.position = pos;
    }
    public Vector3 GetFireOrigin()
    {
        var world = ClientECSWorld.Instance;
        if (world != null)
        {
            var entity = world.GetPlayerEntity(playerId);
            if (world.EntityManager.TryGetComponent(entity, out PlayerViewComponent pv) && pv.View != null)
            {
                var animView = pv.AnimationView ?? pv.View.GetComponent<PlayerAnimationView>();
                if (animView != null && animView.firePoint != null) return animView.firePoint.position;
            }
        }
        return transform.position + Vector3.up * 1.5f;
    }
}
