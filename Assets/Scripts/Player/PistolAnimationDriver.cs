using UnityEngine;
using Animancer;
using ShootingGame.Shared.Simulation;

/// <summary>
/// 远程玩家 Animancer 驱动。和本地玩家用同一套 AnimSet，从 RemotePlayerController 读同步状态。
/// </summary>
[RequireComponent(typeof(Animator))]
public class PistolAnimationDriver : MonoBehaviour
{
    public PlayerAnimationSet animSet;
    private AnimancerComponent _animancer;
    private RemotePlayerController _remote;
    private ClipTransition _idle, _walk, _run, _shoot, _die, _hit1, _hit2, _jump, _turnL, _turnR, _crouchIdle, _crouchWalk;
    private ClipTransition _lastClip;
    private bool _wasMoving, _wasGrounded = true, _wasAlive = true;
    private float _lastYaw;
    private int _lastHp = 100;

    private void Awake()
    {
        _animancer = GetComponentInChildren<AnimancerComponent>(true);
        _remote = GetComponentInParent<RemotePlayerController>();
        if (animSet == null) animSet = Resources.Load<PlayerAnimationSet>("PistolGirl_AnimSet");
        // 清空 Animator Controller，防止默认状态（如 Death）被播放
        var anim = GetComponentInChildren<Animator>(true);
        if (anim != null) { anim.runtimeAnimatorController = null; anim.applyRootMotion = false; }
        LoadClips();
    }

    private ClipTransition GetByName(string n) { foreach (var e in animSet.animations) if (e.name == n) return e.clip; return null; }

    private void LoadClips()
    {
        if (animSet == null) return;
        _idle = GetByName("Rifle_Idle"); _walk = GetByName("Rifle_WalkFwdLoop"); _run = GetByName("Rifle_RunFwdLoop");
        _shoot = GetByName("Rifle_Shoot"); _die = GetByName("Rifle_Death");
        _hit1 = GetByName("Rifle_Hit1"); _hit2 = GetByName("Rifle_Hit2");
        _jump = GetByName("Rifle_JumpUp"); _turnL = GetByName("Rifle_TurnL90"); _turnR = GetByName("Rifle_TurnR90");
        _crouchIdle = GetByName("Rifle_CrouchIdle"); _crouchWalk = GetByName("Rifle_CrouchWalk");
    }

    private void Update()
    {
        if (_animancer == null || _remote == null || _idle == null) return;
        var snap = _remote.CurrentSnapshot;
        // 快照尚未收到（Health 是 byte 默认值 0，Position 也是零向量），播 idle 等待
        bool snapshotReceived = snap.Health > 0 || snap.Position.x != 0f || snap.Position.y != 0f || snap.Position.z != 0f;
        if (!snapshotReceived) { PlayClip(_idle); return; }
        bool alive = snap.Health > 0, grounded = snap.IsGrounded;
        Vector3 vl = new Vector3(snap.Velocity.x, 0f, snap.Velocity.z);
        bool moving = vl.magnitude > 0.5f, fast = vl.magnitude > 5f;
        bool crouching = _remote.IsCrouching, aiming = _remote.IsAiming;

        if (!alive) { PlayOnce(_die); return; }
        if (snap.Health < _lastHp) { PlayOnce(Random.value < 0.5f ? _hit1 : _hit2); _lastHp = snap.Health; return; }
        _lastHp = snap.Health;
        if (_remote.FireTrigger) { _remote.FireTrigger = false; PlayOnce(_shoot); }

        if (!grounded && _wasGrounded) { PlayOnce(_jump); }
        else if (crouching && moving) PlayClip(_crouchWalk);
        else if (crouching) PlayClip(_crouchIdle);
        else if (moving && fast) PlayClip(_run);
        else if (moving) PlayClip(_walk);
        else PlayClip(_idle);

        float dYaw = Mathf.DeltaAngle(_lastYaw, transform.eulerAngles.y);
        if (!moving && Mathf.Abs(dYaw) > 10f) PlayOnce(dYaw > 0 ? _turnR : _turnL);
        _lastYaw = transform.eulerAngles.y;

        _wasMoving = moving; _wasGrounded = grounded;
    }

    private void PlayClip(ClipTransition c) { if (c == _lastClip) return; if (c != null) { _animancer.Play(c, 0.12f); _lastClip = c; } }
    private void PlayOnce(ClipTransition c) { if (c != null) _animancer.Play(c, 0.08f); }
}
