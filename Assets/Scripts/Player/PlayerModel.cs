using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Animancer;
using Cinemachine;
using UnityEngine.UI;
using Random = UnityEngine.Random;

// --- 枚举定义保持不变 ---
public enum PlayerAnimationState
{
    idle, move, jump, fall, turn, aim
}

public enum PlayerState
{
    ground, sky, aim
}

public enum FireMode
{
    Single,     // 单发
    Auto,       // 连发
    Shotgun     // 散弹
}

public class PlayerModel : MonoBehaviour, IStateOwner
{
    [Header("State Machine")]
    // 注意：假设你有外部定义的 State 类 (IdleState 等)，这里保留原样
    private StateMachine animationStateMachine;
    private StateMachine playerStateMachine;
    private PlayerAnimationState _PlayerAnimationState;
    private PlayerState _PlayerState;

    [SerializeField]
    public AnimancerComponent animancer;
    public Animator animator;
    public PlayerAnimationSet AnimationSet; // 假设你有这个类

    [Header("Camera & Aiming")]
    public CinemachineFreeLook normal;
    public CinemachineFreeLook aim;
    public Image aimImage;
    public LayerMask aimLayer;
    public Transform aimTarget; // 场景中的一个空物体，用于标记准星实际命中的位置

    [Header("Weapon System")]
    public FireMode fireMode = FireMode.Single;
    public int shotgunPellets = 6;      // 散弹数量
    public float shotgunSpread = 5f;    // 散弹散射角度
    public Transform firePoint;         // 枪口位置
    public float fireRate = 0.2f;       // 射速
    public ParticleSystem muzzleFlash;
    public GameObject shellPrefab;
    public Transform shellEjectPoint;
    public Animator recoilAnimator;
    
    // 网络同步标识
    public bool isLocalPlayer = true; 
    public bool isShooting;
    
    // 资源
    public AudioClip fireSoundClip;
    public CinemachineImpulseSource impulseSource;

    [Header("Movement & Physics")]
    public float gravity = -9.8f;
    public float jumpHeight = 2f;
    [HideInInspector]
    public Vector3 gravityVector;
    public bool stopGravity = false;
    
    public CharacterController cc { get; private set; }
    public float walkSpeed = 3f;
    public float runSpeed = 10f;

    // --- 初始化 ---
    private void Awake()
    {
        animator = GetComponent<Animator>();
        animancer = GetComponent<AnimancerComponent>();
        cc = GetComponent<CharacterController>();
        
        // 初始化状态机
        animationStateMachine = new StateMachine(this);
        playerStateMachine = new StateMachine(this);
    }

    void Start()
    {
        ChangeAnimationState(PlayerAnimationState.idle);
        ChangePlayerState(PlayerState.ground);
    }

    void Update()
    {
        // 1. 处理重力
        HandleGravity();

        // 2. 更新准星目标点 (核心弹道修正)
        UpdateAimingTarget();
    }

    // --- 重力逻辑 (保留你的原始逻辑) ---
    private void HandleGravity()
    {
        if (cc != null && cc.enabled && !stopGravity)
        {
            // 注意：这里假设你有 PlayerController 单例，如果没有，请自行修改判断条件
            bool isGrounded = (PlayerController.Instance != null) ? PlayerController.Instance.isGround : cc.isGrounded;

            if (isGrounded && gravityVector.y < 0f)
            {
                gravityVector.y = gravity; // 贴地
            }
            else
            {
                gravityVector.y += gravity * Time.deltaTime; // 累积重力
            }

            cc.Move(gravityVector * Time.deltaTime);
        }
    }

    // --- 状态机相关 (保留你的原始逻辑) ---
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

    // ==========================================
    //               核心射击逻辑修改区
    // ==========================================

    // 1. 外部调用开火 (绑定到鼠标左键)
    public void Fire()
    {
        if (!isShooting)
        {
            StartCoroutine(FireCoroutine());
        }
    }

    // 2. 射击协程：负责输入检测、频率控制和数据准备
    public IEnumerator FireCoroutine()
    {
        isShooting = true;

        // --- 步骤 A: 计算射击数据 ---
        // 关键修正：从【枪口】指向【准星目标】，解决 TPS 视差
        Vector3 startPos = firePoint.position;
        Vector3 targetPos = aimTarget.position;
        Vector3 mainDir = (targetPos - startPos).normalized;

        // --- 步骤 B: 根据模式发射 ---
        switch (fireMode)
        {
            case FireMode.Single:
            case FireMode.Auto:
                // 本地执行
                ExecuteFire(startPos, mainDir);

                // 【联机预留位】: 可以在这里发送 Socket 消息
                // NetworkManager.SendFireMessage(startPos, mainDir);
                break;

            case FireMode.Shotgun:
                // 散弹生成多发
                for (int i = 0; i < shotgunPellets; i++)
                {
                    Vector3 spreadDir = CalculateSpread(mainDir, shotgunSpread);
                    ExecuteFire(startPos, spreadDir);
                }
                break;
        }

        // --- 步骤 C: 播放表现特效 ---
        PlayShootFeedback();

        yield return new WaitForSeconds(fireRate);
        isShooting = false;
    }

    // 3. 执行射击：这是真正的生成子弹逻辑 (联机时，接收端也调用这个)
    public void ExecuteFire(Vector3 position, Vector3 direction)
    {
        // 从对象池获取子弹
        Bullet bullet = BulletManager.Instance.GetBullet();
        
        if (bullet != null)
        {
            // 调用新的 Launch 接口，传递方向
            bullet.Launch(position, direction);
        }
        else
        {
            Debug.LogWarning("Bullet Manager returned null bullet!");
        }
    }

    // 辅助：计算散布
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

    // 辅助：播放音效、动画、壳
    private void PlayShootFeedback()
    {
        // 枪口光
        if (muzzleFlash != null) muzzleFlash.Play();
        
        // 音效
        if (AudioPoolManager.Instance != null && fireSoundClip != null)
        {
            AudioPoolManager.Instance.PlaySound(fireSoundClip, transform.position);
        }
        
        // 相机震动 (只在本地玩家触发)
        if (isLocalPlayer && impulseSource != null)
        {
            impulseSource.GenerateImpulse();
        }

        // 抛壳
        if (shellPrefab != null && shellEjectPoint != null)
        {
            GameObject shell = Instantiate(shellPrefab, shellEjectPoint.position, shellEjectPoint.rotation);
            Rigidbody rb = shell.GetComponent<Rigidbody>();
            if (rb) rb.AddForce(shellEjectPoint.right * Random.Range(1f, 3f), ForceMode.Impulse);
            Destroy(shell, 3f);
        }

        // 后坐力动画
        if (recoilAnimator != null)
        {
            recoilAnimator.SetTrigger("recoil_trigger");
        }
    }

    // 切换模式
    public void SwitchFireMode()
    {
        fireMode = (FireMode)(((int)fireMode + 1) % System.Enum.GetValues(typeof(FireMode)).Length);
    }

    // ==========================================
    //               准星检测逻辑
    // ==========================================
    
    private void UpdateAimingTarget()
    {
        // 从屏幕中心发出射线
        Ray ray = Camera.main.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, 1000, aimLayer))
        {
            // 命中物体：变红，目标点设为击中点
            if (aimImage != null) aimImage.color = Color.red;
            
            if (aimTarget != null)
                aimTarget.position = hit.point;
        }
        else
        {
            // 未命中：变白，目标点设为极远处
            if (aimImage != null) aimImage.color = Color.white;
            
            if (aimTarget != null)
                aimTarget.position = ray.GetPoint(100); 
        }
    }
}