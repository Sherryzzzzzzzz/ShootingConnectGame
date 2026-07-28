using UnityEngine;
using Animancer;
using Unity.Cinemachine;
using ShootingGame.Shared.Simulation;

[RequireComponent(typeof(CapsuleCollider))]
public class PistolGirlStateMachine : MonoBehaviour
{
    [Header("引用")]
    public PlayerAnimationSet animSet;
    public CapsuleCollider capsule;

    [Header("武器")]
    public Transform firePoint;
    public ParticleSystem muzzleFlash;
    public AudioClip fireSoundClip;

    [Header("摄像机")]
    public CinemachineFreeLook normalCam;
    public CinemachineFreeLook aimCam;
    public CinemachineFreeLook crouchCam;

    private AnimancerComponent _animancer;

    // Clips
    private ClipTransition _idle, _walk, _run;
    private ClipTransition _aimIdle, _aimWalkF, _aimWalkL, _aimWalkR, _aimWalkB, _aimJog;
    private ClipTransition _crouchIdle, _crouchWalk, _crouchJog;
    private ClipTransition _crouchAimIdle, _crouchAimWalk;
    private ClipTransition _drawGun, _holsterGun;
    private ClipTransition _shoot, _crouchShoot;
    private ClipTransition _hit1, _hit2, _die1;
    private ClipTransition _turnL90, _turnR90;
    private ClipTransition _jump, _fall, _evade, _stun;

    private enum State { Idle, Moving, Aiming, Crouching, Drawing, Holstering, Hit }
    private State _state = State.Idle;
    private State _prevState;
    private bool _gunDrawn; // 右键瞄准时拔枪，松开时收枪
    private bool _wasAiming, _wasCrouching;
    private float _lastShotTime;
    private int _lastHp = 100;
    private float _lastYaw;
    private int _frameCount;
    private int _tickLog;
    private bool _started;
    private float _shootLockUntil;
    private int _warmupFrames; // 启动保护：前若干帧不检测死亡，防止快照未就绪时误播 death

    /// <summary>由 NetPlayerController 调用，触发开枪动画。子弹在动画关键帧（~25%）由 Coroutine 触发。</summary>
    public void OnShoot(bool isCrouching, Vector3 fireOrigin, Vector3 fireDir, int attackId)
    {
        var clip = isCrouching ? _crouchShoot : _shoot;
        if (clip == null || clip.Clip == null)
        {
            Debug.LogWarning($"[PG-SHOOT] clip is null! isCrouching={isCrouching} _shoot={_shoot?.Clip?.name} _crouchShoot={_crouchShoot?.Clip?.name}");
            return;
        }
        Debug.Log($"[PG-SHOOT] Playing {clip.Clip.name} atkId={attackId}");
        // FadeMode.FromStart 确保每次射击从动画开头开始播放
        _animancer.Play(clip, 0.03f, FadeMode.FromStart);
        _lastClip = clip; // 防止 PlayClip 在下一帧重复切入
        _shootLockUntil = Time.unscaledTime + clip.Clip.length * 0.9f;
        // 在动画 25% 处触发子弹生成（对齐开枪关键帧），用闭包捕获参数防连发覆盖
        float delay = clip.Clip.length * 0.25f;
        StartCoroutine(FireBulletAtKeyframe(delay, fireOrigin, fireDir, attackId));
    }

    private System.Collections.IEnumerator FireBulletAtKeyframe(float delay, Vector3 fireOrigin, Vector3 fireDir, int attackId)
    {
        yield return new WaitForSeconds(delay);
        // 枪口火焰
        if (muzzleFlash != null) muzzleFlash.Play();
        // 音效
        if (fireSoundClip != null && AudioPoolManager.Instance != null)
            AudioPoolManager.Instance.PlaySound(fireSoundClip, transform.position);
        // 弹道特效
        if (TracerVFX.Instance != null)
            TracerVFX.Instance.SpawnTracer(fireOrigin, fireDir);
        // 视觉子弹
        if (VisualBulletManager.Instance != null)
            VisualBulletManager.Instance.SpawnLocalBullet(fireOrigin, fireDir, attackId);
    }

    private const float StandH = 1.8f, StandCY = 0.9f;
    private const float CrouchH = 1.0f, CrouchCY = 0.5f;

    public bool IsGunDrawn => _gunDrawn;
    public void PlayDeath() { if (_die1 != null) _animancer?.Play(_die1, 0.2f); }

    private void Awake()
    {
        _animancer = GetComponentInChildren<AnimancerComponent>(true);
        var anim = GetComponentsInChildren<Animator>(true)[0];
        if (anim != null) { anim.runtimeAnimatorController = null; anim.applyRootMotion = false; anim.enabled = false; }
        if (capsule == null) capsule = GetComponent<CapsuleCollider>();
        if (normalCam == null) normalCam = GameObject.Find("FreeLook Camera")?.GetComponent<CinemachineFreeLook>();
        if (aimCam == null) aimCam = GameObject.Find("AimCamera")?.GetComponent<CinemachineFreeLook>();
        if (crouchCam == null) crouchCam = GameObject.Find("CrouchCamera")?.GetComponent<CinemachineFreeLook>();
    }

    private ClipTransition GetByName(string name)
    {
        if (animSet == null) return null;
        foreach (var e in animSet.animations)
            if (e.name == name) return e.clip;
        return null;
    }

    private bool LoadClips()
    {
        if (animSet == null) return false;
        _idle        = GetByName("Rifle_Idle");
        _walk        = GetByName("Rifle_WalkFwdLoop");
        _run         = GetByName("Rifle_RunFwdLoop");
        _aimIdle     = GetByName("Rifle_AimIdle");
        _aimWalkF    = GetByName("Rifle_AimWalkF");
        _aimWalkL    = GetByName("Rifle_AimWalkL");
        _aimWalkR    = GetByName("Rifle_AimWalkR");
        _aimWalkB    = GetByName("Rifle_AimWalkB");
        _aimJog      = GetByName("Rifle_AimJog");
        _crouchIdle  = GetByName("Rifle_CrouchIdle");
        _crouchWalk  = GetByName("Rifle_CrouchWalk");
        _crouchJog   = GetByName("Rifle_CrouchJog");
        _crouchAimIdle = GetByName("Rifle_CrouchAimIdle");
        _crouchAimWalk = GetByName("Rifle_CrouchAimWalk");
        _drawGun     = GetByName("Rifle_DrawGun");
        _holsterGun  = GetByName("Rifle_HolsterGun");
        _shoot       = GetByName("Rifle_Shoot");
        _crouchShoot = GetByName("Rifle_CrouchShoot");
        _hit1        = GetByName("Rifle_Hit1");
        _hit2        = GetByName("Rifle_Hit2");
        _die1        = GetByName("Rifle_Death");
        _turnL90     = GetByName("Rifle_TurnL90");
        _turnR90     = GetByName("Rifle_TurnR90");
        _jump        = GetByName("Rifle_JumpUp");
        _fall        = GetByName("Rifle_FallingLoop");
        _evade       = GetByName("Rifle_Evade");
        _stun        = GetByName("Rifle_Stun");
        return _idle != null;
    }

    public void StateMachineTick(InputFrame input, float dt, PlayerSnapshot snap)
    {
        if (_animancer == null) _animancer = GetComponentInChildren<AnimancerComponent>(true);
        if (_animancer == null) { if (++_tickLog <= 3) Debug.LogWarning("[PG] _animancer 为 null"); return; }
        if (_idle == null && !LoadClips()) return;
        if (!_started) {
            _started = true;
            var a = GetComponentsInChildren<Animator>(true)[0];
            if (a != null) {
                a.runtimeAnimatorController = null; // 再次确保 Controller 已清空，防止默认状态闪现
                a.applyRootMotion = false;
                a.enabled = true;
            }
            _animancer.Play(_idle, 0f); // 零帧过渡，杜绝默认姿态闪现
            return; // 首帧只做初始化，下一帧开始走正常逻辑
        }
        bool grounded = snap.IsGrounded;
        Vector3 vel = new Vector3(snap.Velocity.x, 0f, snap.Velocity.z);
        float speed = vel.magnitude;
        bool moving = speed > 0.3f;
        bool fast = speed > 5f;
        bool aiming = input.Aim;
        bool crouching = input.Crouch;

        // ---- 死亡 ----
        _warmupFrames++;
        if (snap.Health <= 0 && _warmupFrames > 10)
        {
            Debug.Log($"[PG] DEATH health={snap.Health} warmupFrames={_warmupFrames}");
            PlayOnce(_die1); return;
        }

        // ---- 受击 ----
        if (snap.Health < _lastHp)
        {
            PlayOnce(Random.value < 0.5f ? _hit1 : _hit2);
            _prevState = _state;
            _state = State.Hit;
            _lastHp = snap.Health;
            return;
        }
        _lastHp = snap.Health;
        if (_state == State.Hit) { _frameCount++; if (_frameCount < 20) return; _frameCount = 0; _state = _prevState; }

        // ---- 蹲伏碰撞体 ----
        if (crouching != _wasCrouching && capsule != null)
        {
            capsule.height = crouching ? CrouchH : StandH;
            capsule.center = new Vector3(0, crouching ? CrouchCY : StandCY, 0);
        }

        // ---- 拔枪（右键瞄准时自动拔枪）----
        if (aiming && !_wasAiming && !_gunDrawn)
        {
            PlayOnce(_drawGun);
            _state = State.Drawing;
            _gunDrawn = true;
            _frameCount = 0;
            return;
        }
        // ---- 收枪（松开右键时收枪）----
        if (!aiming && _wasAiming && _gunDrawn)
        {
            PlayOnce(_holsterGun);
            _state = State.Holstering;
            _gunDrawn = false;
            _frameCount = 0;
            return;
        }
        // ---- 拔枪/收枪动画播完 ----
        if (_state == State.Drawing || _state == State.Holstering)
        {
            _frameCount++;
            if (_frameCount < 15) return;
            _frameCount = 0;
            _state = State.Idle;
        }

        // ---- 转向 ----
        float deltaYaw = Mathf.DeltaAngle(_lastYaw, input.AimYaw);
        if (!moving && Mathf.Abs(deltaYaw) > 15f)
        {
            PlayOnce(deltaYaw > 0 ? _turnR90 : _turnL90);
            _lastYaw = input.AimYaw;
            return;
        }
        _lastYaw = input.AimYaw;

        // ---- 主状态 ----
        if (_tickLog <= 3) Debug.Log($"[PG] MAIN hp={snap.Health} moving={moving} aim={aiming} crouch={crouching} gunDrawn={_gunDrawn}");
        // 射击锁定期内保持射击动画（不切换到其他动画）
        if (Time.unscaledTime < _shootLockUntil && _gunDrawn)
        {
            PlayClip(crouching && aiming ? _crouchShoot : _shoot);
            _wasAiming = aiming; _wasCrouching = crouching;
            return;
        }
        if (crouching && aiming && _gunDrawn)
        {
            if (moving) PlayClip(_crouchAimWalk); else PlayClip(_crouchAimIdle);
        }
        else if (crouching)
        {
            if (moving && fast) PlayClip(_crouchJog);
            else if (moving) PlayClip(_crouchWalk);
            else PlayClip(_crouchIdle);
        }
        else if (aiming && _gunDrawn)
        {
            if (moving && fast) PlayClip(_aimJog);
            else if (moving) PlayClip(_aimWalkF);
            else PlayClip(_aimIdle);
            _state = State.Aiming;
        }
        else if (moving)
        {
            if (fast) PlayClip(_run); else PlayClip(_walk);
            _state = State.Moving;
        }
        else if (!grounded)
        {
            PlayClip(snap.VerticalVelocity > 0 ? _jump : _fall);
        }
        else
        {
            PlayClip(_idle); _state = State.Idle;
        }

        // 摄像机切换（优先级: crouch > aim > normal）
        if (crouching && crouchCam != null) SwitchCamera("crouch");
        else if (aiming && _gunDrawn && aimCam != null) SwitchCamera("aim");
        else SwitchCamera("normal");

        _wasAiming = aiming; _wasCrouching = crouching;
    }

    private ClipTransition _lastClip;

    private void PlayClip(ClipTransition clip)
    {
        if (clip == _lastClip) return;
        if (clip != null && clip.Clip != null)
        {
            // 每次播放前确保 Controller 已清空（防外部重新赋值）
            var a = _animancer.Animator;
            if (a != null && a.runtimeAnimatorController != null) a.runtimeAnimatorController = null;
            _animancer.Play(clip, 0.12f);
            _lastClip = clip;
        }
    }

    private void SwitchCamera(string mode) // "normal" / "aim" / "crouch"
    {
        if (normalCam != null) normalCam.gameObject.SetActive(mode == "normal");
        if (aimCam != null) aimCam.gameObject.SetActive(mode == "aim");
        if (crouchCam != null) crouchCam.gameObject.SetActive(mode == "crouch");
    }

    private void PlayOnce(ClipTransition clip)
    {
        if (clip != null && clip.Clip != null)
            _animancer.Play(clip, 0.08f);
    }
}
