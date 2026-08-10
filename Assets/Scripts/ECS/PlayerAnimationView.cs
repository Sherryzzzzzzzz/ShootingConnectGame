using UnityEngine;
using Animancer;
using Unity.Cinemachine;
using ShootingGame.Shared.Simulation;

/// <summary>
/// 玩家动画表现层薄壳（客户端专用）。
/// 只负责"播放"：持有 Animancer + 动画剪辑，提供播放/相机/特效 API。
/// 所有"决策"（播什么动画）由 ClientAnimationSystem 从 ECS 状态推导后调用本类。
/// </summary>
public class PlayerAnimationView : MonoBehaviour
{
    public PlayerAnimationSet animSet;
    public CapsuleCollider capsule;

    // 武器表现
    public Transform firePoint;
    public ParticleSystem muzzleFlash;
    public AudioClip fireSoundClip;

    // 相机（本地玩家使用）
    public CinemachineFreeLook normalCam;
    public CinemachineFreeLook aimCam;
    public CinemachineFreeLook crouchCam;

    private AnimancerComponent _animancer;

    // Clips
    private ClipTransition _idle, _walk, _run;
    private AvatarMask _shootLayerMask;
    private ClipTransition _aimIdle, _aimWalkF, _aimWalkL, _aimWalkR, _aimWalkB, _aimJog;
    private ClipTransition _crouchIdle, _crouchWalk, _crouchJog;
    private ClipTransition _crouchAimIdle, _crouchAimWalk;
    private ClipTransition _drawGun, _holsterGun;
    private ClipTransition _shoot, _crouchShoot;
    private ClipTransition _hit1, _hit2, _die1;
    private ClipTransition _turnL90, _turnR90;
    private ClipTransition _jump, _fall, _evade, _stun;

    private bool _started;

    public AnimancerComponent Animancer => _animancer;
    public bool IsInitialized => _started;

    private void Awake()
    {
        _animancer = GetComponentInChildren<AnimancerComponent>(true);
        var anim = GetComponentsInChildren<Animator>(true);
        if (anim.Length > 0 && anim[0] != null)
        {
            anim[0].runtimeAnimatorController = null;
            anim[0].applyRootMotion = false;
        }
        if (capsule == null) capsule = GetComponent<CapsuleCollider>();
        if (normalCam == null) normalCam = GameObject.Find("FreeLook Camera")?.GetComponent<CinemachineFreeLook>();
        if (aimCam == null) aimCam = GameObject.Find("AimCamera")?.GetComponent<CinemachineFreeLook>();
        if (crouchCam == null) crouchCam = GameObject.Find("CrouchCamera")?.GetComponent<CinemachineFreeLook>();

        // 运行时音效兜底：prefab 未配置时从 Resources 加载枪声
        if (fireSoundClip == null)
            fireSoundClip = Resources.Load<AudioClip>("Audio/single-gunshot-54-40780");
    }

    /// <summary>
    /// 首帧初始化：加载 Clips、清空 Animator Controller、创建射击层、播放 Idle。
    /// 幂等，多次调用只初始化一次。
    /// </summary>
    public void EnsureInitialized()
    {
        if (_started) return;
        if (_animancer == null) _animancer = GetComponentInChildren<AnimancerComponent>(true);
        if (_animancer == null) return;
        if (_idle == null && !LoadClips()) return;

        var anims = GetComponentsInChildren<Animator>(true);
        if (anims.Length > 0 && anims[0] != null)
        {
            anims[0].runtimeAnimatorController = null;
            anims[0].applyRootMotion = false;
            anims[0].enabled = true;
        }
        SetupShootLayer();
        _animancer.Play(_idle, 0f);
        _started = true;
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

    // ==================== 播放 API（ClientAnimationSystem 调用） ====================

    private ClipTransition _lastClip;

    /// <summary>循环/持续播放（相同剪辑不重复切入）。</summary>
    public void PlayClip(ClipTransition clip)
    {
        if (clip == null || clip.Clip == null) return;
        if (clip == _lastClip) return;
        EnsureControllerCleared();
        _animancer.Play(clip, 0.12f);
        _lastClip = clip;
    }

    /// <summary>一次性播放（可打断，播放后允许被覆盖）。</summary>
    public void PlayOnce(ClipTransition clip)
    {
        if (clip == null || clip.Clip == null) return;
        EnsureControllerCleared();
        _animancer.Play(clip, 0.08f, FadeMode.FromStart);
        _lastClip = null;
    }

    private void EnsureControllerCleared()
    {
        var a = _animancer.Animator;
        if (a != null && a.runtimeAnimatorController != null)
            a.runtimeAnimatorController = null;
    }

    /// <summary>播放死亡动画（一次性）。</summary>
    public void PlayDeath()
    {
        if (_die1 != null) _animancer?.Play(_die1, 0.2f);
    }

    /// <summary>
    /// 触发开火表现：射击动画播在上半身层 + 关键帧协程触发枪口/音效/弹道/视觉子弹。
    /// </summary>
    public void OnShoot(bool isCrouching, Vector3 fireOrigin, Vector3 fireDir, int attackId)
    {
        var clip = isCrouching ? _crouchShoot : _shoot;
        if (clip == null || clip.Clip == null)
        {
            Debug.LogWarning($"[PG-SHOOT] clip is null! isCrouching={isCrouching}");
            return;
        }
        if (_shootLayerMask != null && _animancer.Layers.Count > 1)
        {
            _animancer.Layers[1].Play(clip, 0.05f, FadeMode.FromStart);
        }
        else
        {
            _animancer.Play(clip, 0.03f, FadeMode.FromStart);
        }
        _lastClip = clip;
        float delay = clip.Clip.length * 0.25f;
        StartCoroutine(FireBulletAtKeyframe(delay, fireOrigin, fireDir, attackId));
    }

    private System.Collections.IEnumerator FireBulletAtKeyframe(float delay, Vector3 fireOrigin, Vector3 fireDir, int attackId)
    {
        yield return new WaitForSeconds(delay);
        if (muzzleFlash != null) muzzleFlash.Play();
        if (fireSoundClip != null && AudioPoolManager.Instance != null)
            AudioPoolManager.Instance.PlaySound(fireSoundClip, transform.position);
        if (TracerVFX.Instance != null)
            TracerVFX.Instance.SpawnTracer(fireOrigin, fireDir);
        if (ClientBulletSystem.Instance != null)
            ClientBulletSystem.Instance.SpawnLocalBullet(fireOrigin, fireDir, attackId);
    }

    // ==================== 蹲伏碰撞体 ====================

    private const float StandH = 1.8f, StandCY = 0.9f;
    private const float CrouchH = 1.0f, CrouchCY = 0.5f;

    /// <summary>设置蹲伏/站立碰撞体尺寸（仅本地玩家；模拟层碰撞世界独立于 Unity Collider）。</summary>
    public void ApplyCrouchCollider(bool crouching)
    {
        if (capsule == null) return;
        capsule.height = crouching ? CrouchH : StandH;
        capsule.center = new Vector3(0, crouching ? CrouchCY : StandCY, 0);
    }

    // ==================== 相机（仅本地玩家） ====================

    private float _baseFov = 60f;
    private bool _fovInitialized;
    private Transform _camAnchor;

    /// <summary>接管主相机：创建相机锚点控制蹲下高度，挂遮挡半透明。仅本地玩家调用。</summary>
    public void BindCamera()
    {
        if (normalCam == null || _camAnchor != null) return;

        var anchorGo = new GameObject("PlayerCamAnchor");
        anchorGo.transform.SetParent(transform, false);
        _camAnchor = anchorGo.transform;
        normalCam.Follow = _camAnchor;
        normalCam.LookAt = _camAnchor;
        EnsureCameraOcclusionFade(normalCam);
        Debug.Log("[PG] BindCamera: 相机已绑定到本地玩家锚点");
    }

    /// <summary>切换相机模式：normal / aim / crouch。</summary>
    public void SwitchCamera(string mode)
    {
        if (normalCam != null) normalCam.gameObject.SetActive(mode == "normal");
        if (aimCam != null) aimCam.gameObject.SetActive(mode == "aim");
        if (crouchCam != null) crouchCam.gameObject.SetActive(mode == "crouch");
    }

    /// <summary>奔跑 FOV 拉大 / 蹲下 FOV 收紧；蹲下降低相机锚点。</summary>
    public void AdjustCamera(bool running, bool crouching)
    {
        if (normalCam == null || !normalCam.gameObject.activeSelf) return;

        if (!_fovInitialized)
        {
            _baseFov = normalCam.m_Lens.FieldOfView;
            _fovInitialized = true;
        }

        float targetFov = _baseFov;
        if (running) targetFov += 15f;
        else if (crouching) targetFov -= 8f;
        normalCam.m_Lens.FieldOfView = Mathf.Lerp(normalCam.m_Lens.FieldOfView, targetFov, Time.deltaTime * 10f);

        if (_camAnchor != null)
        {
            float targetY = crouching ? -0.4f : 0f;
            Vector3 anchorPos = _camAnchor.localPosition;
            anchorPos.y = Mathf.Lerp(anchorPos.y, targetY, Time.deltaTime * 8f);
            _camAnchor.localPosition = anchorPos;
        }
    }

    /// <summary>创建上半身射击层（Additive + AvatarMask），移动射击时下半身保持跑步。</summary>
    private void SetupShootLayer()
    {
        if (_animancer == null || _animancer.Layers.Count <= 1) return;

        _shootLayerMask = new AvatarMask();
        _shootLayerMask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Root, true);
        _shootLayerMask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Body, true);
        _shootLayerMask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Head, true);
        _shootLayerMask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm, true);
        _shootLayerMask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm, true);
        _shootLayerMask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFingers, true);
        _shootLayerMask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFingers, true);
        _shootLayerMask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftLeg, false);
        _shootLayerMask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightLeg, false);

        var layer = _animancer.Layers[1];
        layer.IsAdditive = true;
        layer.Mask = _shootLayerMask;
        layer.Weight = 1f;
    }

    private void EnsureCameraOcclusionFade(CinemachineFreeLook cam)
    {
        if (cam.GetComponent<CinemachineCollider>() == null)
            cam.gameObject.AddComponent<CinemachineCollider>();

        var fade = cam.gameObject.GetComponent<CameraOcclusionFade>();
        if (fade == null)
            fade = cam.gameObject.AddComponent<CameraOcclusionFade>();
        fade.SetTarget(_camAnchor != null ? _camAnchor : transform);
    }

    // ==================== 动画剪辑访问器（ClientAnimationSystem 使用） ====================

    public ClipTransition Idle => _idle;
    public ClipTransition Walk => _walk;
    public ClipTransition Run => _run;
    public ClipTransition AimIdle => _aimIdle;
    public ClipTransition AimWalkF => _aimWalkF;
    public ClipTransition AimJog => _aimJog;
    public ClipTransition CrouchIdle => _crouchIdle;
    public ClipTransition CrouchWalk => _crouchWalk;
    public ClipTransition CrouchJog => _crouchJog;
    public ClipTransition CrouchAimIdle => _crouchAimIdle;
    public ClipTransition CrouchAimWalk => _crouchAimWalk;
    public ClipTransition DrawGun => _drawGun;
    public ClipTransition HolsterGun => _holsterGun;
    public ClipTransition Shoot => _shoot;
    public ClipTransition CrouchShoot => _crouchShoot;
    public ClipTransition Hit1 => _hit1;
    public ClipTransition Hit2 => _hit2;
    public ClipTransition Die1 => _die1;
    public ClipTransition TurnL90 => _turnL90;
    public ClipTransition TurnR90 => _turnR90;
    public ClipTransition Jump => _jump;
    public ClipTransition Fall => _fall;
}
