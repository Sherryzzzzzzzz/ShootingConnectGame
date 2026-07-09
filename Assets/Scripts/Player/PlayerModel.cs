using System.Collections.Generic;
using UnityEngine;
using Animancer;
using Unity.Cinemachine;
using UnityEngine.UI;
using Random = UnityEngine.Random;

// 【重要】引入共享库，并指定 InputFrame 指的是共享库里的那个
using ShootingGame.Shared.Simulation;
using ShootingGame.Shared.Hero;
using InputFrame = ShootingGame.Shared.Simulation.InputFrame;
using FireMode = ShootingGame.Shared.Hero.FireMode;

// --- 枚举定义保持不变 ---
public enum PlayerAnimationState
{
    idle, move, jump, fall, turn, aim
}

public enum PlayerState
{
    ground, sky, aim
}

public class PlayerModel : MonoBehaviour, IStateOwner
{
    #region ===== 状态机 =====

    private StateMachine animationStateMachine;
    private StateMachine playerStateMachine;

    private PlayerAnimationState _PlayerAnimationState;
    private PlayerState _PlayerState;

    #endregion

    #region ===== 组件引用 =====

    [Header("Animation")]
    public AnimancerComponent animancer;
    public Animator animator;
    public PlayerAnimationSet AnimationSet;

    [Header("Camera & Aim")]
#pragma warning disable CS0618
    public CinemachineFreeLook normal;
    public CinemachineFreeLook aim;
#pragma warning restore CS0618
    public Image aimImage;
    public LayerMask aimLayer;
    public Transform aimTarget;

    [Header("Weapon")]
    public FireMode fireMode = FireMode.Single;
    public int shotgunPellets = 6;
    public float shotgunSpread = 5f;
    public Transform firePoint;
    public float fireRate = 0.2f;

    public ParticleSystem muzzleFlash;
    public GameObject shellPrefab;
    public Transform shellEjectPoint;
    public Animator recoilAnimator;
    private static readonly int RecoilTriggerHash = Animator.StringToHash("recoil_trigger");

    public AudioClip fireSoundClip;
    public CinemachineImpulseSource impulseSource;

    [Header("Network")]
    public bool isLocalPlayer = true;

    [Header("Movement Settings")]
    public float walkSpeed = 2f;
    public float runSpeed = 5f;
    public float jumpHeight = 1.2f;
    public float gravity = -9.81f;
    public float rotationSpeed = 72f; // 每秒旋转度数

    // Movement state variables
    [HideInInspector] public Vector3 gravityVector;

    #endregion

    #region ===== 射击 Tick 变量 =====

    private float fireCooldown;

    #endregion

    #region ===== 初始化 =====

    private void Awake()
    {
        animator = GetComponent<Animator>();
        if (animator != null)
        {
            // 禁用 root motion：联网角色的位置由服务器/模拟驱动，动画不应移动角色
            animator.applyRootMotion = false;
        }

        animancer = GetComponent<AnimancerComponent>();
        if (animancer == null)
        {
            animancer = GetComponentInChildren<AnimancerComponent>();
        }

        // 如果 AnimationSet 未在 Inspector 中指定，尝试自动加载
        if (AnimationSet == null)
        {
            AnimationSet = Resources.Load<PlayerAnimationSet>("FenNi");
            if (AnimationSet == null)
                Debug.LogWarning("[PlayerModel] AnimationSet 未找到，请在 Inspector 中指定或放入 Resources/FenNi.asset");
        }

        // 如果 fireSoundClip 未在 Inspector 中指定，尝试从 Resources 加载
        if (fireSoundClip == null)
        {
            fireSoundClip = Resources.Load<AudioClip>("Audio/single-gunshot-54-40780");
            if (fireSoundClip != null)
                Debug.Log("[PlayerModel] 从 Resources 加载枪声音效: Audio/single-gunshot-54-40780");
            else
                Debug.LogWarning("[PlayerModel] 枪声音效未找到。请将枪声文件放入 Resources/Audio/ 或在 Inspector 中赋值。");
        }

        animationStateMachine = new StateMachine(this);
        playerStateMachine = new StateMachine(this);
    }

    private void Start()
    {
        // 仅本地玩家初始化状态机；远程玩家由 RemotePlayerController 驱动动画
        if (!isLocalPlayer) return;

        ChangeAnimationState(PlayerAnimationState.idle);
        ChangePlayerState(PlayerState.ground);
    }

    #endregion

    #region ===== Tick 入口（核心）=====

    public void StateMachineTick(InputFrame input, float dt)
    {
        // 仅本地玩家运行状态机和射击逻辑
        if (!isLocalPlayer) return;

        // 1️⃣ 玩家状态机
        playerStateMachine?.Tick(input, dt);

        // 2️⃣ 动画状态机
        animationStateMachine?.Tick(input, dt);

        // 3️⃣ 射击逻辑
        HandleFire(input, dt);

        // 4️⃣ 更新准星
        UpdateAimingTarget();
    }

    #endregion

    #region ===== 射击逻辑（Tick驱动）=====

    public void Fire()
    {
        Debug.LogWarning("PlayerModel.Fire() called but logic is now input-driven in HandleFire.");
    }

    private void HandleFire(InputFrame input, float dt)
    {
        fireCooldown -= dt;

        // 注意：共享库 InputFrame 字段是大写开头 (.Fire)
        if (!input.Fire)
            return;

        if (fireCooldown > 0f)
            return;

        fireCooldown = fireRate;

        // 确定发射位置
        Vector3 startPos;
        if (firePoint != null)
            startPos = firePoint.position;
        else
            startPos = transform.position + Vector3.up * (GameConstants.PlayerHeight * 0.85f);

        // 确定发射方向
        Vector3 mainDir;
        if (aimTarget != null)
        {
            mainDir = (aimTarget.position - startPos).normalized;
        }
        else
        {
            Camera cam = Camera.main;
            if (cam != null)
                mainDir = cam.transform.forward;
            else
                mainDir = transform.forward;
        }

        switch (fireMode)
        {
            case FireMode.Single:
            case FireMode.Auto:
                // 网络模式下视觉子弹由 NetPlayerController -> VisualBulletManager 管理
                break;

            case FireMode.Shotgun:
                // 霰弹散射方向计算保留（未来可能用于 VFX）
                break;
        }

        PlayShootFeedback();
    }

    private Vector3 CalculateSpread(Vector3 dir, float angle)
    {
        Quaternion rot = Quaternion.LookRotation(dir);
        Quaternion randomRot = Quaternion.Euler(
            Random.Range(-angle, angle),
            Random.Range(-angle, angle),
            0
        );
        return (rot * randomRot) * Vector3.forward;
    }

    private void PlayShootFeedback()
    {
        if (muzzleFlash != null)
            muzzleFlash.Play();

        if (AudioPoolManager.Instance != null && fireSoundClip != null)
            AudioPoolManager.Instance.PlaySound(fireSoundClip, transform.position);

        if (isLocalPlayer && impulseSource != null)
            impulseSource.GenerateImpulse();

        if (shellPrefab != null && shellEjectPoint != null)
        {
            GameObject shell = Instantiate(shellPrefab, shellEjectPoint.position, shellEjectPoint.rotation);
            Rigidbody rb = shell.GetComponent<Rigidbody>();
            if (rb)
                rb.AddForce(shellEjectPoint.right * Random.Range(1f, 3f), ForceMode.Impulse);

            Destroy(shell, 3f);
        }

        if (recoilAnimator != null)
            recoilAnimator.SetTrigger(RecoilTriggerHash);
    }

    #endregion

    #region ===== 准星更新 =====

    private void UpdateAimingTarget()
    {
        Camera mainCam = Camera.main;
        if (mainCam == null) return;

        Ray ray = mainCam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, 1000, aimLayer))
        {
            if (aimImage != null)
                aimImage.color = Color.red;

            if (aimTarget != null)
                aimTarget.position = hit.point;
        }
        else
        {
            if (aimImage != null)
                aimImage.color = Color.white;

            if (aimTarget != null)
                aimTarget.position = ray.GetPoint(100);
        }
    }

    #endregion

    #region ===== 状态机切换 =====

    public void ChangeAnimationState(PlayerAnimationState animationState)
    {
        switch (animationState)
        {
            case PlayerAnimationState.idle:
                animationStateMachine.EnterState<IdleState>();
                break;
            case PlayerAnimationState.move:
                animationStateMachine.EnterState<MoveState>();
                break;
            case PlayerAnimationState.jump:
                animationStateMachine.EnterState<JumpState>();
                break;
            case PlayerAnimationState.fall:
                animationStateMachine.EnterState<FallState>();
                break;
            case PlayerAnimationState.turn:
                animationStateMachine.EnterState<TurnState>();
                break;
            case PlayerAnimationState.aim:
                animationStateMachine.EnterState<AimState>();
                break;
        }

        _PlayerAnimationState = animationState;
    }

    /// <summary>
    /// 强制重新初始化当前状态（用于 NetPlayerController 延迟添加后重新获取引用）
    /// </summary>
    public void ReinitCurrentStates()
    {
        animationStateMachine?.ReinitCurrentState();
        playerStateMachine?.ReinitCurrentState();
    }

    public void ChangePlayerState(PlayerState state)
    {
        switch (state)
        {
            case PlayerState.ground:
                playerStateMachine.EnterState<PlayerGroundState>();
                break;
            case PlayerState.sky:
                playerStateMachine.EnterState<PlayerSkyState>();
                break;
            case PlayerState.aim:
                playerStateMachine.EnterState<PlayerAimState>();
                break;
        }

        _PlayerState = state;
    }

    #endregion

    // 【新增辅助方法】如果你的 StateMachine 代码里报错说 InputFrame.Movement 是 SharedVec2 不是 Vector2
    // 可以在这里加个 Helper，或者直接在 State 代码里强转： (float)input.Movement.X
    public Vector2 GetInputMovement(InputFrame input)
    {
        return new Vector2(input.Movement.x, input.Movement.y);
    }
}