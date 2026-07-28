using System;
using UnityEngine;
using ShootingGame.Shared.Hero;
using ShootingGame.Shared.Math;
using ShootingGame.Shared.Simulation;
using ShootingGame.Shared.Physics;
using ShootingGame.Shared.Protocol;
using ShootingGame.Shared.Prediction;
using ShootingGame.Network;
using ShootingGame.Shared.ECS;
using UnityEngine.InputSystem;

/// <summary>
/// 本地玩家控制器。整合新的网络系统（BattleClient、AttackManager、AuthoritySync）。
/// 处理输入收集、客户端预测、服务端和解。
/// </summary>
public class NetPlayerController : MonoBehaviour
{
    [Header("网络")]
    [SerializeField] private BattleClient battleClient;

    [Header("平滑设置")]
    [SerializeField] private float smoothingSpeed = 30f;
    [SerializeField] private float snapThreshold = 5f; // 放宽到 5m，减少误拉回

    // 调试
    [Header("调试")]
    [SerializeField] private bool enableDebugLog = false;
    private Vector3 _lastLoggedPosition;
    private float _debugLogTimer;

    // 模拟状态
    private PlayerSnapshot _currentSnapshot;
    private CollisionWorld _collisionWorld;
    private int _currentTick;
    private float _accumulator;
    private float _tickInterval;

    // 预测缓冲
    private RingBuffer<InputFrame> _inputHistory;
    private RingBuffer<PlayerSnapshot> _snapshotHistory;

    // Prediction service for chain predictions (skills, abilities)
    private PredictionService _predictionService = new PredictionService();

    /// <summary>Public access to prediction service for ability systems.</summary>
    public PredictionService PredictionService => _predictionService;

    // 视觉平滑
    private Vector3 _visualPosition;
    private Quaternion _visualRotation;

    // 输入冗余
    private InputFrame[] _redundantFrames;

    // 服务端状态追踪
    private int _lastServerTick = -1;

    private float _reloadTimer; // 换弹计时器

    // ECS
    private ClientECSWorld _ecsWorld;
    private Entity _ecsEntity;
    private PlayerCombatBehaviour _combatBehaviour;

    // 引用
    private Camera _mainCam;
    private PlayerInputAction input;

    // 本地玩家 ID（从 BattleClient 获取）
    public int PlayerId => battleClient?.BattlePlayerId ?? -1;
    public bool IsLocalPlayer => true;

    // 英雄配置（由 BattleManager 在 Instantiate 后设置）
    public HeroConfig HeroConfig { get; set; }

    // 死亡/复活状态
    private bool _isDead;
    public bool IsDead => _isDead;

    // 公开摄像机引用
    public Camera cam => _mainCam;

    private void Awake()
    {
        // 锁定光标（与原始 PlayerController 一致），同时触发 OS 窗口焦点捕获
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // 允许应用在失去焦点时仍接收输入（构建版本启动时经常没有焦点）
        UnityEngine.InputSystem.InputSystem.settings.backgroundBehavior = UnityEngine.InputSystem.InputSettings.BackgroundBehavior.IgnoreFocus;

        input = new PlayerInputAction();

        // 强制确保设备被识别（Unity 6 异步初始化可能漏设备）
        if (Keyboard.current == null) UnityEngine.InputSystem.InputSystem.AddDevice<Keyboard>();
        if (Mouse.current == null) UnityEngine.InputSystem.InputSystem.AddDevice<Mouse>();

        // 强制重绑定：清掉 .inputactions 里的绑定，用代码硬追加（绕过 control scheme 匹配问题）
        if (input.Simple.Move.bindings.Count > 0) input.Simple.Move.ChangeBinding(0).Erase();
        input.Simple.Move.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
        input.Simple.Move.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
            .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
        if (input.Simple.Jump.bindings.Count > 0) input.Simple.Jump.ChangeBinding(0).Erase();
        input.Simple.Jump.AddBinding("<Keyboard>/space");
        if (input.Simple.LightAttack.bindings.Count > 0) input.Simple.LightAttack.ChangeBinding(0).Erase();
        input.Simple.LightAttack.AddBinding("<Mouse>/leftButton");
        if (input.Simple.Reload.bindings.Count > 0) input.Simple.Reload.ChangeBinding(0).Erase();
        input.Simple.Reload.AddBinding("<Keyboard>/r");
        if (input.Simple.Run.bindings.Count > 0) input.Simple.Run.ChangeBinding(0).Erase();
        input.Simple.Run.AddBinding("<Keyboard>/leftShift");
        if (input.Simple.Aim.bindings.Count > 0) input.Simple.Aim.ChangeBinding(0).Erase();
        input.Simple.Aim.AddBinding("<Mouse>/rightButton");
        // 禁用 control scheme 过滤——任何设备都能触发
        input.bindingMask = null;
        Debug.Log("[NetPlayerController] 输入绑定已强制覆盖");

        // === 诊断 ===
        Debug.Log($"[INPUT-INIT] devices={UnityEngine.InputSystem.InputSystem.devices.Count} " +
                  $"kb={Keyboard.current?.name ?? "NULL"} mouse={Mouse.current?.name ?? "NULL"} " +
                  $"moveBindings={input.Simple.Move.bindings.Count} enabled={input.Simple.enabled}");

        // === 诊断 ===
        Debug.Log($"[INPUT-INIT] devices={UnityEngine.InputSystem.InputSystem.devices.Count} " +
                  $"kb={Keyboard.current?.name ?? "NULL"} mouse={Mouse.current?.name ?? "NULL"} " +
                  $"moveBindings={input.Simple.Move.bindings.Count} enabled={input.Simple.enabled}");

        var animancer = GetComponentInChildren<Animancer.AnimancerComponent>(true);
        var anim = GetComponentInChildren<Animator>(true);
        if (anim != null) anim.applyRootMotion = false;
    }

    private void Start()
    {
        _tickInterval = GameConstants.TickDelta;
        _inputHistory = new RingBuffer<InputFrame>(GameConstants.SnapshotHistorySize);
        _snapshotHistory = new RingBuffer<PlayerSnapshot>(GameConstants.SnapshotHistorySize);
        _redundantFrames = new InputFrame[GameConstants.InputRedundancy];

        _collisionWorld = CollisionWorldLoader.Instance;
        if (_collisionWorld == null)
        {
            Debug.LogWarning("[NetPlayerController] CollisionWorldLoader 未找到，创建默认地面");
            _collisionWorld = new ShootingGame.Shared.Physics.CollisionWorld();
            _collisionWorld.AddBox(new ShootingGame.Shared.Physics.AABB(
                new Vec3(-50, -1, -50),
                new Vec3(50, 0, 50)
            ));
        }

        // 查找 battleClient
        if (battleClient == null)
            battleClient = BattleClient.Instance;
        if (battleClient == null)
        {
            Debug.LogError("[NetPlayerController] ❌ BattleClient.Instance 为 null！Tick 循环不会运行。");
        }

        // 查找摄像机
        RefreshCamera();
        if (_mainCam == null)
        {
            Debug.LogWarning("[NetPlayerController] ⚠ 摄像机未找到，瞄准输入将使用默认值");
        }

        // 初始化快照
        if (transform.position.y < 0.01f)
            transform.position = new Vector3(transform.position.x, 0.1f, transform.position.z);

        _currentSnapshot = PlayerSnapshot.Default(transform.position.ToShared());
        _visualPosition = transform.position;
        _visualRotation = transform.rotation;

        // 初始化 ECS 世界并注册本地玩家
        _ecsWorld = FindFirstObjectByType<ClientECSWorld>();
        if (_ecsWorld == null)
        {
            var go = new GameObject("ClientECSWorld");
            _ecsWorld = go.AddComponent<ClientECSWorld>();
        }
        _ecsEntity = _ecsWorld.RegisterLocalPlayer(PlayerId, transform.position.ToShared(), HeroConfig);

        // 新框架：绑定 NetworkBehaviour（用于 RPC 接收）
        if (_combatBehaviour == null)
        {
            _combatBehaviour = new PlayerCombatBehaviour();
            _combatBehaviour.Bind(_ecsEntity, _ecsWorld.EntityManager, ShootingGame.Network.NetObjectType.Player);
            Debug.Log($"[NetPlayerController] NetworkBehaviour bound: NetId={_combatBehaviour.NetId} PlayerId={PlayerId}");
        }

        // 订阅事件
        if (battleClient != null)
        {
            battleClient.OnFrameReceived += OnFrameReceived;
            battleClient.OnBattleStart += OnBattleStart;

            if (battleClient.IsInBattle)
            {
                Debug.Log("[NetPlayerController] Start 时已处于战斗中，立即初始化 tick 状态");
                OnBattleStart();
            }
        }

        _lastLoggedPosition = _visualPosition;
        _debugLogTimer = 0f;

        // 诊断：打印所有管理器状态
        Debug.Log($"[NET-DIAG] 管理器状态: BattleClient={battleClient != null} isInBattle={battleClient?.IsInBattle} " +
                  $"AttackManager={AttackManager.Instance != null} canFire={AttackManager.Instance?.CanFire() ?? false} " +
                  $"VisualBulletManager={VisualBulletManager.Instance != null} activeBullets={VisualBulletManager.Instance?.GetActiveBulletCount() ?? -1} " +
                  $"AuthoritySync={AuthoritySync.Instance != null} " +
                  $"BattleManager={BattleManager.Instance != null}");
    }

    private void OnEnable()
    {
        input.Enable();
    }

    private void OnDisable()
    {
        input.Disable();
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        Debug.Log($"[FOCUS] hasFocus={hasFocus} | appFocused={Application.isFocused} | cursorLocked={Cursor.lockState == CursorLockMode.Locked}");
        if (hasFocus)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private void OnDestroy()
    {
        if (battleClient != null)
        {
            battleClient.OnFrameReceived -= OnFrameReceived;
            battleClient.OnBattleStart -= OnBattleStart;
        }
    }

    private void OnBattleStart()
    {
        // 战斗开始，重置状态
        _currentTick = 1;
        _lastServerTick = -1;
        _accumulator = 0f;

        // 重置快照 HP 和弹药（弹药/射速按英雄枪械配置）
        _currentSnapshot.Health = (byte)(HeroConfig?.MaxHP ?? GameConstants.MaxHealth);
        _currentSnapshot.CurrentAmmo = GameConstants.MaxAmmoPerClip;
        _currentSnapshot.IsReloading = false;
        _currentSnapshot.ReloadTimer = 0f;
        _bloomHeat = 0f;
        CurrentSpreadDeg = 0f;

        var gun = HeroConfig?.Gun;
        if (gun != null)
        {
            _currentSnapshot.MaxAmmo = gun.ClipSize;
            _currentSnapshot.CurrentAmmo = gun.ClipSize;
            _currentSnapshot.ReloadDuration = gun.ReloadTime;
            _currentSnapshot.FireInterval = gun.FireRate;
            if (AttackManager.Instance != null)
                AttackManager.Instance.FireInterval = gun.FireRate;
        }

        // 重置 ECS 实体
        if (_ecsWorld != null)
        {
            _ecsWorld.UnregisterPlayer(PlayerId);
            _ecsEntity = _ecsWorld.RegisterLocalPlayer(PlayerId, transform.position.ToShared(), HeroConfig);

            // 重新绑定 NetworkBehaviour
            if (_combatBehaviour == null) _combatBehaviour = new PlayerCombatBehaviour();
            _combatBehaviour.Unbind();
            _combatBehaviour.Bind(_ecsEntity, _ecsWorld.EntityManager, ShootingGame.Network.NetObjectType.Player);
        }

        // 强制进入 idle 动画状态

        // 更新动态追帧系统
        if (DynamicTickSystem.Instance != null)
        {
            DynamicTickSystem.Instance.Reset(1);
        }

        Debug.Log("[NetPlayerController] 战斗开始，状态已重置");
    }

    private bool _warnedNotLocal;
    private bool _warnedNotInBattle;

    private void Update()
    {
        // 连发扩散热度随时间恢复 + 准星扩散角刷新（非开火帧持续衰减）
        var gunForBloom = HeroConfig?.Gun;
        if (gunForBloom != null)
        {
            if (_bloomHeat > 0f && gunForBloom.BloomRecover > 0f)
                _bloomHeat = Mathf.Max(0f, _bloomHeat - gunForBloom.BloomRecover * Time.deltaTime);
            bool isMoving = (_currentSnapshot.Velocity.x * _currentSnapshot.Velocity.x
                           + _currentSnapshot.Velocity.z * _currentSnapshot.Velocity.z) > 1f;
            CurrentSpreadDeg = ShootingGame.Shared.Hero.SpreadUtility.ComputeTotalSpread(gunForBloom, isMoving, _bloomHeat);
        }

        // 仅本地玩家运行 Tick 循环和视觉更新；远程玩家的 transform 由 RemotePlayerController 驱动
        if (!this.enabled)
        {
            if (!_warnedNotLocal)
            {
                Debug.LogWarning($"[NetPlayerController] Update 被跳过: PlayerModel.isLocalPlayer=false (GameObject={gameObject.name}, PlayerId={PlayerId})。请在 BattleManager.SpawnLocalPlayer 中设置为 true。");
                _warnedNotLocal = true;
            }
            return;
        }

        if (battleClient == null || !battleClient.IsInBattle)
        {
            if (!_warnedNotInBattle)
            {
                Debug.LogWarning($"[NetPlayerController] Update 被跳过: battleClient={battleClient != null}, IsInBattle={battleClient?.IsInBattle} (GameObject={gameObject.name})。等待 BattleStart...");
                _warnedNotInBattle = true;
            }
            return;
        }

        // 固定 tick 率——和服务端保持一致（1/60）
        float tickInterval = _tickInterval;

        _accumulator += Time.unscaledDeltaTime;

        while (_accumulator >= tickInterval)
        {
            Tick();
            _accumulator -= tickInterval;
        }

        // 视觉平滑（指数衰减，帧率无关）
        float dist = Vector3.Distance(_visualPosition, _currentSnapshot.Position.ToUnity());
        if (dist > snapThreshold)
        {
            _visualPosition = _currentSnapshot.Position.ToUnity();
            _visualRotation = _currentSnapshot.Rotation.ToUnity();
        }
        else
        {
            // 指数衰减：visual 以 smoothSpeed 单位/秒逼近目标
            float t = 1f - Mathf.Exp(-smoothingSpeed * Time.deltaTime);
            _visualPosition = Vector3.Lerp(_visualPosition, _currentSnapshot.Position.ToUnity(), t);
            _visualRotation = Quaternion.Slerp(_visualRotation, _currentSnapshot.Rotation.ToUnity(), t);
        }

        // 更新位置和旋转
        transform.position = _visualPosition;
        transform.rotation = _visualRotation;

        // 调试日志：每秒输出一次位置变化
        if (enableDebugLog)
        {
            _debugLogTimer -= Time.deltaTime;
            if (_debugLogTimer <= 0f)
            {
                _debugLogTimer = 1f;
                Debug.Log($"[NetPlayerController] 模拟位置={_currentSnapshot.Position.ToUnity()} " +
                          $"视觉位置={_visualPosition} 在地面={_currentSnapshot.IsGrounded} " +
                          $"Tick={_currentTick} 在战斗中={battleClient != null && battleClient.IsInBattle}");
            }
        }
    }

    private bool _tickStartedLogged;
    private int _tickDiagCounter;

    private void Tick()
    {
        // 死亡时不模拟，但仍发送输入（用于复活后的输入同步）
        if (_isDead)
        {
            InputFrame deadInput = BuildLocalInput(_currentTick);
            SendInputToServer(deadInput);
            _currentTick++;
            return;
        }

        // 1. 构建输入
        InputFrame inputFrame = BuildLocalInput(_currentTick);

        // 诊断日志：前10帧 + 每120帧输出输入值（不需要enableDebugLog）
        if (!_tickStartedLogged || (_tickDiagCounter++ % 120 == 0))
        {
            if (!_tickStartedLogged) _tickStartedLogged = true;
            Vector3 pos = _currentSnapshot.Position.ToUnity();
            Debug.Log($"[TICK-DIAG] frame={_currentTick} move=({inputFrame.Movement.x:F2},{inputFrame.Movement.y:F2}) jump={inputFrame.Jump} run={inputFrame.Run} fire={inputFrame.Fire} pos=({pos.x:F2},{pos.y:F2},{pos.z:F2}) isGrounded={_currentSnapshot.IsGrounded} battleClient={(battleClient != null ? "OK" : "NULL")} isInBattle={battleClient?.IsInBattle}");
        }

        // 调试日志：输入值
        if (enableDebugLog && _currentTick % 60 == 0)
        {
            Debug.Log($"[NetPlayerController.Tick] Tick={_currentTick} " +
                      $"输入移动=({inputFrame.Movement.x:F2}, {inputFrame.Movement.y:F2}) " +
                      $"瞄准偏航={inputFrame.AimYaw:F1} 跑动={inputFrame.Run} 跳跃={inputFrame.Jump}");
        }

        // 2. 存储历史
        _inputHistory.Store(_currentTick, inputFrame);

        // 3. 发送到服务端
        SendInputToServer(inputFrame);

        // 4. 本地预测（用 PlayerSimulation——和服务端一致保证确定性）
        Vec3 posBefore = _currentSnapshot.Position;
        _currentSnapshot = PlayerSimulation.Simulate(_currentSnapshot, inputFrame, _tickInterval, _collisionWorld);
        Vec3 posAfter = _currentSnapshot.Position;

        // 5. 动画状态机（在物理之后运行，使 JumpState 读到正确的 IsGrounded）
        var pgSm = GetComponent<PistolGirlStateMachine>();
                if (pgSm != null && pgSm.enabled)
        {
            pgSm.StateMachineTick(inputFrame, _tickInterval, _currentSnapshot);
        }


        // 5b. 换弹处理
        HandleReload(inputFrame);

        // 调试日志：模拟前后位置 + ECS速度诊断
        if (_currentTick <= 10 || _currentTick % 60 == 0)
        {
            float moved = Vec3.Distance(posBefore, posAfter);
            string ecsVel = "";
            if (_ecsWorld != null && _ecsWorld.EntityManager.IsValid(_ecsEntity))
            {
                if (_ecsWorld.EntityManager.TryGetComponent<MovementComponent>(_ecsEntity, out var mv))
                {
                    ecsVel = $" ecsVel=({mv.Velocity.x:F3},{mv.Velocity.z:F3}) ecsMaxSpeed={mv.MaxMoveSpeed:F1} ecsGrounded={mv.IsGrounded}";
                }
            }
            Debug.Log($"[MOVE-DIAG] tick={_currentTick} moveIn=({inputFrame.Movement.x:F3},{inputFrame.Movement.y:F3}) " +
                      $"posBefore=({posBefore.x:F2},{posBefore.z:F2}) posAfter=({posAfter.x:F2},{posAfter.z:F2}) " +
                      $"moved={moved:F3}m snapGrounded={_currentSnapshot.IsGrounded}{ecsVel}");
        }

        // 6. 存储预测快照
        _snapshotHistory.Store(_currentTick, _currentSnapshot);

        // 7. 特效/音效改为只在子弹实际发射时触发（移到 SendInputToServer 里）

        // 8. 更新帧号
        _currentTick++;

        // 更新动态追帧系统
        if (DynamicTickSystem.Instance != null)
        {
            DynamicTickSystem.Instance.SetClientFrame(_currentTick);
        }
    }

    private bool _lastJumpPressed;
    private bool _lastReloadPressed;
    private bool _lastAbility1Pressed;
    private bool _lastAbility2Pressed;
    private bool _lastAbility3Pressed;
    private bool _lastAbility4Pressed;

    private InputFrame BuildLocalInput(int tick)
    {
        float aimYaw = 0f;
        float aimPitch = 0f;
        if (_mainCam != null)
        {
            Vector3 camForward = _mainCam.transform.forward;
            aimYaw = Mathf.Atan2(camForward.x, camForward.z) * Mathf.Rad2Deg;
            aimPitch = -Mathf.Asin(camForward.y) * Mathf.Rad2Deg;
        }

        // 移动方向用角色逻辑朝向（客户端预测快照，仅鼠标转动时变，不会因 FreeLook 绕圈），瞄准用相机朝向
        float moveYaw = _currentSnapshot.Rotation.EulerAngles.y;

        // 移动输入：优先用新 Input System，零值时回退到旧 Input Manager
        Vector2 rawInput = input.Simple.Move.ReadValue<Vector2>();
        if (rawInput.sqrMagnitude < 0.001f)
        {
            float legacyH = Input.GetAxis("Horizontal");
            float legacyV = Input.GetAxis("Vertical");
            if (Mathf.Abs(legacyH) > 0.001f || Mathf.Abs(legacyV) > 0.001f)
            {
                rawInput = new Vector2(legacyH, legacyV);
                if (tick <= 5 || tick % 60 == 0)
                    Debug.Log($"[INPUT-FALLBACK] 新InputSystem返回零，旧InputManager: ({legacyH:F2},{legacyV:F2})");
            }
        }
        // 射击也兜底
        if (!input.Simple.LightAttack.IsPressed() && Input.GetMouseButton(0))
        {
            if (tick <= 5) Debug.Log("[INPUT-FALLBACK] LightAttack.IsPressed=false, Input.GetMouseButton(0)=true");
        }

        // 诊断：同时读取 InputActionAsset 和 Keyboard.current，定位输入丢失层级
        if (tick <= 5 || tick % 60 == 0)
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            var mouse = UnityEngine.InputSystem.Mouse.current;
            string kbState = kb != null
                ? $"W={kb.wKey.isPressed} A={kb.aKey.isPressed} S={kb.sKey.isPressed} D={kb.dKey.isPressed} Spc={kb.spaceKey.isPressed} Shft={kb.leftShiftKey.isPressed}"
                : "Keyboard.current=NULL";
            string mouseState = mouse != null
                ? $"LMB={mouse.leftButton.isPressed} RMB={mouse.rightButton.isPressed} Delta=({mouse.delta.x.ReadValue():F2},{mouse.delta.y.ReadValue():F2})"
                : "Mouse.current=NULL";
            Debug.Log($"[INPUT-DIAG] tick={tick} rawInput=({rawInput.x:F3},{rawInput.y:F3}) | kb=[{kbState}] | mouse=[{mouseState}] | appFocused={Application.isFocused} | actionEnabled={input.Simple.enabled} | moveEnabled={input.Simple.Move.enabled}");
        }
        float yawRad = moveYaw * Mathf.Deg2Rad;
        float cosYaw = Mathf.Cos(yawRad);
        float sinYaw = Mathf.Sin(yawRad);
        float worldX = rawInput.x * cosYaw + rawInput.y * sinYaw;
        float worldZ = rawInput.y * cosYaw - rawInput.x * sinYaw;

        // 跳跃按键边缘检测：仅在按下瞬间触发，长按不反复跳跃
        bool jumpPressed = input.Simple.Jump.IsPressed();
        bool jumpEdge = jumpPressed && !_lastJumpPressed;
        _lastJumpPressed = jumpPressed;

        // 换弹：R键边缘检测，仅在按下瞬间触发
        bool reloadPressed = input.Simple.Reload.IsPressed();
        bool reloadEdge = reloadPressed && !_lastReloadPressed;
        _lastReloadPressed = reloadPressed;

        // 英雄技能 1/2/3/4 边缘检测
        bool ability1Pressed = input.Simple.Ability1.IsPressed();
        bool ability1Edge = ability1Pressed && !_lastAbility1Pressed;
        _lastAbility1Pressed = ability1Pressed;

        bool ability2Pressed = input.Simple.Ability2.IsPressed();
        bool ability2Edge = ability2Pressed && !_lastAbility2Pressed;
        _lastAbility2Pressed = ability2Pressed;

        bool ability3Pressed = input.Simple.Ability3.IsPressed();
        bool ability3Edge = ability3Pressed && !_lastAbility3Pressed;
        _lastAbility3Pressed = ability3Pressed;

        bool ability4Pressed = input.Simple.Ability4.IsPressed();
        bool ability4Edge = ability4Pressed && !_lastAbility4Pressed;
        _lastAbility4Pressed = ability4Pressed;

        // 新 Input System 的 .IsPressed() 可能静默返回 false——用旧 Input Manager 兜底
        bool newFire = input.Simple.LightAttack.IsPressed();
        bool oldFire = Input.GetMouseButton(0);
        bool isFire = newFire || oldFire;
        // 每 2 秒输出一次原始输入状态，方便排查"按键没反应"
        if (tick % 120 == 0)
            Debug.Log($"[RAW-INPUT] obj={gameObject.name} tick={tick} newFire={newFire} oldFire={oldFire} => isFire={isFire}");
        bool isAim = input.Simple.Aim.IsPressed() || Input.GetMouseButton(1);
        bool isRun = input.Simple.Run.IsPressed() || Input.GetKey(KeyCode.LeftShift);
        bool isJump = jumpEdge || Input.GetKeyDown(KeyCode.Space);
        bool isReload = reloadEdge || Input.GetKeyDown(KeyCode.R);
        bool ab1 = ability1Edge || Input.GetKeyDown(KeyCode.Alpha1);
        bool ab2 = ability2Edge || Input.GetKeyDown(KeyCode.Alpha2);
        bool ab3 = ability3Edge || Input.GetKeyDown(KeyCode.Alpha3);
        bool ab4 = ability4Edge || Input.GetKeyDown(KeyCode.Alpha4);

        LastInputFrame = new InputFrame
        {
            Tick = tick,
            Movement = new Vec2(worldX, worldZ),
            Jump = isJump,
            Run = isRun,
            Aim = isAim,
            Fire = isFire,
            Reload = isReload,
            Ability1 = ab1,
            Ability2 = ab2,
            Ability3 = ab3,
            Ability4 = ab4,
            AimYaw = aimYaw,
            AimPitch = aimPitch,
            Crouch = Input.GetKey(KeyCode.C) || Input.GetKey(KeyCode.LeftControl)
        };

        return LastInputFrame;
    }

    private int _debugLogTick;
    private InputFrame _lastSentInput;
    private int _fireDiagTick;

    private void SendInputToServer(InputFrame current)
    {
        if (battleClient == null || !battleClient.IsInBattle) return;

        _lastSentInput = current;

        // 构建玩家操作
        var operation = new PlayerOperation
        {
            PlayerId = battleClient.BattlePlayerId,
            MoveX = current.Movement.x,
            MoveY = current.Movement.y,
            AimYaw = current.AimYaw,
            AimPitch = current.AimPitch,
            Fire = current.Fire,
            Jump = current.Jump,
            Run = current.Run,
            Aim = current.Aim,
            Reload = current.Reload,
            ClientFrameId = _currentTick,
            PosX = _currentSnapshot.Position.x,
            PosY = _currentSnapshot.Position.y,
            PosZ = _currentSnapshot.Position.z,
            VelX = _currentSnapshot.Velocity.x,
            VelZ = _currentSnapshot.Velocity.z,
            IsGrounded = _currentSnapshot.IsGrounded,
            Crouch = current.Crouch
        };

        // 每60帧打印一次发送数据
        if (++_debugLogTick % 60 == 0)
        {
            Debug.Log($"[SEND] tick={_currentTick} move=({current.Movement.x:F2},{current.Movement.y:F2}) aimYaw={current.AimYaw:F1} aim={current.Aim} run={current.Run} fire={current.Fire} ammo={_currentSnapshot.CurrentAmmo} reload={_currentSnapshot.IsReloading}");
        }

        // 处理攻击（需检查本地弹药状态 + Pistol Girl 的拔枪状态）
        var pgSm = GetComponent<PistolGirlStateMachine>();
        bool gunOk = pgSm == null || pgSm.IsGunDrawn; // 无 PG 状态机=老角色,不限制
        if (current.Fire && gunOk && AttackManager.Instance != null && AttackManager.Instance.CanFire()
            && _currentSnapshot.CurrentAmmo > 0 && !_currentSnapshot.IsReloading)
        {
            if (AttackManager.Instance.TryCreateAttack(current.AimYaw, current.AimPitch, _currentTick, out var attack))
            {
                operation.AttackOperations.Add(attack);

                // 消耗弹药
                if (_currentSnapshot.CurrentAmmo > 0 && !_currentSnapshot.IsReloading)
                {
                    _currentSnapshot.CurrentAmmo--;
                }

                // 计算弹道（含扩散）
                var aimRot = Quaternion.Euler(current.AimPitch, current.AimYaw, 0f);
                var fireDir = aimRot * Vector3.forward;
                var fireOrigin = GetFireOrigin();

                var gun = HeroConfig?.Gun;
                if (gun != null)
                {
                    bool isMoving = (_currentSnapshot.Velocity.x * _currentSnapshot.Velocity.x
                                   + _currentSnapshot.Velocity.z * _currentSnapshot.Velocity.z) > 1f;
                    float spreadDeg = ShootingGame.Shared.Hero.SpreadUtility.ComputeTotalSpread(gun, isMoving, _bloomHeat);
                    var sd = ShootingGame.Shared.Hero.SpreadUtility.ApplyConeSpread(
                        new Vec3(fireDir.x, fireDir.y, fireDir.z), spreadDeg,
                        ShootingGame.Shared.Hero.SpreadUtility.MakeSeed(attack.AttackId, battleClient.BattlePlayerId));
                    fireDir = new Vector3(sd.x, sd.y, sd.z);

                    _bloomHeat = Mathf.Min(_bloomHeat + gun.BloomPerShot,
                        gun.BloomMax > 0f ? gun.BloomMax : _bloomHeat + gun.BloomPerShot);
                    CurrentSpreadDeg = spreadDeg;
                }

                attack.SpawnPos = new Vec3(fireOrigin.x, fireOrigin.y, fireOrigin.z);

                // 标记为预测子弹（权威帧不再重复生成）
                AttackManager.Instance.MarkAttackPredicted(attack.AttackId);

                // 触发开枪动画 — 子弹、特效、音效由 Coroutine 在关键帧生成
                var shootSm = GetComponent<PistolGirlStateMachine>();
                if (shootSm != null)
                {
                    shootSm.OnShoot(current.Crouch, fireOrigin, fireDir, attack.AttackId);
                }
                else
                {
                    Debug.LogError($"[FIRE] PistolGirlStateMachine is NULL on {gameObject.name}! 动画和子弹都不会生成！");
                }
            }
            else
            {
                // CanFire 通过了但 TryCreateAttack 返回 false — 静默失败变为显式日志
                Debug.LogWarning($"[FIRE-FAIL] CanFire=true but TryCreateAttack failed! cooldown={AttackManager.Instance.GetFireCooldown():F3} pending={AttackManager.Instance.PendingAttackCount} maxPending={32}");
            }
        }
        else if (current.Fire && ++_fireDiagTick % 60 == 0)
        {
            // 诊断：开火条件不满足 — 打印全部条件状态
            float cd = AttackManager.Instance?.GetFireCooldown() ?? -1f;
            int pending = AttackManager.Instance?.PendingAttackCount ?? -1;
            bool canFire = AttackManager.Instance?.CanFire() ?? false;
            bool hasFrame = AttackManager.Instance?.HasReceivedServerFrame ?? false;
            Debug.Log($"[FIRE-BLOCKED] obj={gameObject.name} gunOk={gunOk} ammo={_currentSnapshot.CurrentAmmo} reloading={_currentSnapshot.IsReloading} canFire={canFire} hasFrame={hasFrame} cooldown={cd:F3} pending={pending}");
        }

        // 处理英雄技能激活（1/2/3/4键）
        if (current.Ability1 || current.Ability2 || current.Ability3 || current.Ability4)
        {
            if (HeroConfig != null && HeroConfig.Abilities != null)
            {
                for (int i = 0; i < 4 && i < HeroConfig.Abilities.Length; i++)
                {
                    bool pressed = i switch
                    {
                        0 => current.Ability1,
                        1 => current.Ability2,
                        2 => current.Ability3,
                        3 => current.Ability4,
                        _ => false
                    };
                    if (!pressed) continue;

                    var abilityCfg = HeroConfig.Abilities[i];
                    ushort instanceId = _ecsWorld.TryActivateAbility(PlayerId, abilityCfg.AssetId);
                    if (instanceId > 0)
                    {
                        operation.AbilityEvents.Add(new ShootingGame.Shared.Ability.AbilityEventData
                        {
                            PlayerId = (byte)PlayerId,
                            InstanceId = instanceId,
                            AssetId = abilityCfg.AssetId,
                            EventType = ShootingGame.Shared.Ability.AbilityEventType.RequestActivate
                        });
                    }
                }
            }
        }

        // 诊断：打印 PlayerOperation 实际值（确认序列化前数据是否正确）
        if (_currentTick <= 10 || _currentTick % 30 == 0)
        {
            Debug.Log($"[OP-DUMP] tick={_currentTick} MoveX={operation.MoveX:F6} MoveY={operation.MoveY:F6} AimYaw={operation.AimYaw:F6} Fire={operation.Fire} Jump={operation.Jump} Run={operation.Run} Aim={operation.Aim} Reload={operation.Reload}");
        }

        // 发送操作
        battleClient.SendOperation(operation, _currentTick);

        // 更新调试面板
        var overlay = FindFirstObjectByType<NetworkDebugOverlay>();
        if (overlay != null)
            overlay.RecordSentInput(current.AimYaw, current.Aim, current.Run, current.Movement.x, current.Movement.y);
    }

    /// <summary>
    /// 换弹处理。边缘触发（R 键按下瞬间）。
    /// </summary>
    private void HandleReload(InputFrame input)
    {
        // 已经在换弹中——倒计时
        if (_currentSnapshot.IsReloading)
        {
            _reloadTimer -= _tickInterval;
            if (_reloadTimer <= 0f)
            {
                // 换弹完成
                _currentSnapshot.CurrentAmmo = _currentSnapshot.MaxAmmo; // 枪械弹夹容量
                _currentSnapshot.IsReloading = false;
                _reloadTimer = 0f;
            }
            return;
        }

        // 按 R 键且弹药不满 → 开始换弹（最大弹药走枪械配置）
        if (input.Reload && _currentSnapshot.CurrentAmmo < _currentSnapshot.MaxAmmo)
        {
            _currentSnapshot.IsReloading = true;
            _reloadTimer = HeroConfig?.Gun?.ReloadTime ?? GameConstants.ReloadTime;
        }
    }

    private void OnFrameReceived(AllPlayerOperation frame)
    {
        if (battleClient == null) return;

        // 处理能力事件确认（服务器→客户端）
        if (frame.AbilityEvents != null && frame.AbilityEvents.Count > 0)
        {
            foreach (var evt in frame.AbilityEvents)
            {
                if (evt.PlayerId != battleClient.BattlePlayerId) continue;

                switch (evt.EventType)
                {
                    case ShootingGame.Shared.Ability.AbilityEventType.ConfirmActivate:
                        _ecsWorld.ConfirmActivate(PlayerId, evt.InstanceId);
                        break;
                    case ShootingGame.Shared.Ability.AbilityEventType.RejectActivate:
                        _ecsWorld.RejectActivate(PlayerId, evt.InstanceId);
                        break;
                }
            }
        }

        // 查找本玩家的状态
        foreach (var state in frame.PlayerStates)
        {
            if (state.PlayerId == battleClient.BattlePlayerId)
            {
                // 首次收到服务端帧，解锁攻击
                if (!AttackManager.Instance.HasReceivedServerFrame)
                {
                    AttackManager.Instance.HasReceivedServerFrame = true;
                    Debug.Log($"[NetPlayerController] 首次收到服务端帧(frameId={frame.FrameId})，攻击已解锁");
                }

                // 更新服务端帧号
                if (DynamicTickSystem.Instance != null)
                {
                    DynamicTickSystem.Instance.UpdateServerFrame(frame.FrameId);
                }

                // 和解检查
                ReconcileWithServer(state, frame.FrameId);
                break;
            }
        }
    }

    private void ReconcileWithServer(PlayerStateMsg serverState, int serverTick)
    {
        if (serverTick <= _lastServerTick) return;
        _lastServerTick = serverTick;

        // 获取预测的快照
        PlayerSnapshot predicted = _snapshotHistory.Get(serverTick);
        if (predicted.Tick != serverTick) return;

        // 检查是否需要和解（只用 HP/Ammo，位置用 AuthoritySync 的平滑修正）
        if (predicted.Health != serverState.Hp)
        {
            _currentSnapshot.Health = (byte)serverState.Hp;
        }
        if (predicted.CurrentAmmo != serverState.CurrentAmmo)
        {
            _currentSnapshot.CurrentAmmo = serverState.CurrentAmmo;
        }
        if (predicted.IsReloading != serverState.IsReloading)
        {
            _currentSnapshot.IsReloading = serverState.IsReloading;
        }

        // 位置和解：>3m 硬 snap，2-3m 平滑修正，<2m 信任预测
        float posDist = Vec3.Distance(predicted.Position, serverState.Position);
        if (posDist > 3f)
        {
            Debug.LogWarning($"[NetPlayerController] 位置偏差过大 ({posDist:F1}m)，拉回服务端位置");
            _currentSnapshot.Position = serverState.Position;
            _currentSnapshot.Velocity = serverState.Velocity;
            _currentSnapshot.VerticalVelocity = serverState.Velocity.y;
            _currentSnapshot.IsGrounded = serverState.IsGrounded;
        }
        else if (posDist > 0.3f)
        {
            // 平滑拉向服务端位置
            float blend = 0.1f;
            _currentSnapshot.Position = Vec3.Lerp(predicted.Position, serverState.Position, blend);
        }

        _snapshotHistory.Store(serverTick, _currentSnapshot);

        // 仅在大偏差时做硬 snap + 重新模拟
        if (posDist > 10f)
        {
            // Create a prediction context for this reconciliation event
            var prevPos = _currentSnapshot.Position;
            _predictionService.CreatePrediction(0, () =>
            {
                // If later rejected, we'd restore to the pre-correction position
                _currentSnapshot.Position = prevPos;
            }, $"reconcile_tick_{serverTick}");

            if (_ecsWorld != null && _ecsWorld.EntityManager.IsValid(_ecsEntity))
                ECSBridge.ApplyServerCorrection(_ecsWorld.EntityManager, _ecsEntity, _currentSnapshot);

            for (int tick = serverTick + 1; tick < _currentTick; tick++)
            {
                var input = _inputHistory.Get(tick);
                _currentSnapshot = PlayerSimulation.Simulate(_currentSnapshot, input, _tickInterval, _collisionWorld);
                _snapshotHistory.Store(tick, _currentSnapshot);
            }
        }
    }

    private bool NeedsReconciliation(PlayerSnapshot predicted, PlayerStateMsg server)
    {
        float posDist = Vec3.Distance(predicted.Position, server.Position);
        if (posDist > 2f) return true; // 放宽到 2m，减少频繁和解

        if (predicted.IsGrounded != server.IsGrounded) return true;
        if (predicted.State != (PlayerStateEnum)server.StateEnum) return true;

        return false;
    }

    /// <summary>
    /// 获取枪口位置。优先使用 PlayerModel.firePoint，回退到计算位置。
    /// </summary>
    private Vector3 GetFireOrigin()
    {
        var pg = GetComponent<PistolGirlStateMachine>();
        if (pg != null && pg.firePoint != null) return pg.firePoint.position;
        // 回退：玩家位置 + 身高 * 0.85（约胸部/枪口高度）
        return transform.position + Vector3.up * (GameConstants.PlayerHeight * 0.85f);
    }

    /// <summary>
    /// 刷新摄像机引用（场景加载后调用，确保 Camera.main 不为 null）。
    /// </summary>
    public void RefreshCamera()
    {
        _mainCam = Camera.main;
        if (_mainCam == null)
            _mainCam = FindFirstObjectByType<Camera>();
    }

    /// <summary>
    /// 本地玩家死亡处理。停止模拟，播放死亡动画。
    /// </summary>
    public void SetDead()
    {
        if (_isDead) return;
        _isDead = true;

        // 播放死亡动画
        var pgDead = GetComponent<PistolGirlStateMachine>();
        if (pgDead != null) pgDead.PlayDeath();

        Debug.Log($"[NetPlayerController] 本地玩家 {PlayerId} 死亡");
    }

    /// <summary>
    /// 本地玩家复活。重置位置并恢复模拟。
    /// </summary>
    public void Revive(Vector3 spawnPosition)
    {
        _isDead = false;
        transform.position = spawnPosition;
        _visualPosition = spawnPosition;
        _currentSnapshot = PlayerSnapshot.Default(spawnPosition.ToShared());

        // Re-register ECS entity
        if (_ecsWorld != null)
        {
            _ecsWorld.RegisterLocalPlayer(PlayerId, spawnPosition.ToShared(), HeroConfig);
            _ecsEntity = _ecsWorld.GetPlayerEntity(PlayerId);
        }

        // 恢复动画
        var animancer = GetComponent<Animancer.AnimancerComponent>();
        var animator = GetComponent<Animator>();
        if (animator != null) animator.SetBool("Dead", false);

        Debug.Log($"[NetPlayerController] 本地玩家 {PlayerId} 复活 位置=({spawnPosition.x:F1},{spawnPosition.y:F1},{spawnPosition.z:F1})");
    }

    // 公开访问器
    public PlayerSnapshot CurrentSnapshot => _currentSnapshot;
    /// <summary>上一帧构建的输入（PistolAnimationDriver 等动画系统读取）</summary>
    public InputFrame LastInputFrame { get; private set; }
    public bool IsGrounded => _currentSnapshot.IsGrounded;
    public Vec3 Velocity => _currentSnapshot.Velocity;

    /// <summary>当前总散射角（度），供准星扩散显示（BattleUI 读取）</summary>
    public float CurrentSpreadDeg { get; private set; }

    // 连发扩散热度（与服务器同语义：每次开火 += BloomPerShot，随时间 -= BloomRecover）
    private float _bloomHeat;
    public float VerticalVelocity => _currentSnapshot.VerticalVelocity;
    public Transform Cam => _mainCam?.transform;
    public Vector3 LocalMovement => transform.position;
}