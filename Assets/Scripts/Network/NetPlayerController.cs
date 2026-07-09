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

    // 开火视觉特效
    private float _localFireCooldown;
    private float _reloadTimer; // 换弹计时器

    // ECS
    private ClientECSWorld _ecsWorld;
    private Entity _ecsEntity;
    private PlayerCombatBehaviour _combatBehaviour;

    // 引用
    private PlayerModel _playerModel;
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

        // === 构建版本诊断：确认 Input System 状态 ===
        Debug.Log($"[BUILD-VER] 2026-05-25-v3 | InputSystem v{UnityEngine.InputSystem.InputSystem.version} | " +
                  $"devices={UnityEngine.InputSystem.InputSystem.devices.Count} | " +
                  $"Keyboard.current={(Keyboard.current != null ? Keyboard.current.name : "NULL")} | " +
                  $"Mouse.current={(Mouse.current != null ? Mouse.current.name : "NULL")} | " +
                  $"inputActionMap.enabled={input.Simple.enabled} | " +
                  $"Move.bindings.count={input.Simple.Move.bindings.Count}");
        // ==============================

        // 检测必需组件
        _playerModel = GetComponent<PlayerModel>();
        if (_playerModel == null)
            _playerModel = GetComponentInChildren<PlayerModel>();
        if (_playerModel == null)
        {
            Debug.LogError($"[NetPlayerController] ❌ PlayerModel 组件缺失！请在预制体上添加 PlayerModel。GameObject={gameObject.name}");
        }

        var animancer = GetComponent<Animancer.AnimancerComponent>();
        if (animancer == null)
            animancer = GetComponentInChildren<Animancer.AnimancerComponent>();
        if (animancer == null)
        {
            Debug.LogError($"[NetPlayerController] ❌ AnimancerComponent 组件缺失！请在预制体上添加 AnimancerComponent。GameObject={gameObject.name}");
        }

        // 禁用 root motion：联网角色位置由 Simulation 驱动，动画不应移动角色
        var anim = GetComponent<Animator>();
        if (anim != null)
            anim.applyRootMotion = false;

        // 检测 AnimationSet
        if (_playerModel != null && _playerModel.AnimationSet == null)
        {
            Debug.LogWarning($"[NetPlayerController] ⚠ PlayerModel.AnimationSet 为空，动画不会播放。请在 Inspector 中指定或放入 Resources/FenNi.asset");
        }
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

        // 重置快照 HP 和弹药
        _currentSnapshot.Health = GameConstants.MaxHealth;
        _currentSnapshot.CurrentAmmo = GameConstants.MaxAmmoPerClip;
        _currentSnapshot.IsReloading = false;
        _currentSnapshot.ReloadTimer = 0f;

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
        if (_playerModel != null)
        {
            _playerModel.ChangeAnimationState(PlayerAnimationState.idle);
            _playerModel.ChangePlayerState(PlayerState.ground);
        }

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
        // 仅本地玩家运行 Tick 循环和视觉更新；远程玩家的 transform 由 RemotePlayerController 驱动
        if (_playerModel != null && !_playerModel.isLocalPlayer)
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
        if (_playerModel != null)
        {
            _playerModel.StateMachineTick(inputFrame, _tickInterval);
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

        // 7. 处理开火视觉特效
        _localFireCooldown -= _tickInterval;
        if (inputFrame.Fire && _localFireCooldown <= 0f)
        {
            _localFireCooldown = GameConstants.FireRate;
            ProcessFire(inputFrame);
        }

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

        // 移动输入根据摄像机朝向旋转（W=摄像机前方, A=摄像机左方）
        Vector2 rawInput = input.Simple.Move.ReadValue<Vector2>();

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
        float yawRad = aimYaw * Mathf.Deg2Rad;
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

        return new InputFrame
        {
            Tick = tick,
            Movement = new Vec2(worldX, worldZ),
            Jump = jumpEdge,
            Run = input.Simple.Run.IsPressed(),
            Aim = input.Simple.Aim.IsPressed(),
            Fire = input.Simple.LightAttack.IsPressed(),
            Reload = reloadEdge,
            Ability1 = ability1Edge,
            Ability2 = ability2Edge,
            Ability3 = ability3Edge,
            Ability4 = ability4Edge,
            AimYaw = aimYaw,
            AimPitch = aimPitch
        };
    }

    private int _debugLogTick;
    private InputFrame _lastSentInput;
    private int _localBulletCount;
    private int _vbmNullWarnCount;
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
            IsGrounded = _currentSnapshot.IsGrounded
        };

        // 每60帧打印一次发送数据
        if (++_debugLogTick % 60 == 0)
        {
            Debug.Log($"[SEND] tick={_currentTick} move=({current.Movement.x:F2},{current.Movement.y:F2}) aimYaw={current.AimYaw:F1} aim={current.Aim} run={current.Run} fire={current.Fire} ammo={_currentSnapshot.CurrentAmmo} reload={_currentSnapshot.IsReloading}");
        }

        // 处理攻击（需检查本地弹药状态）
        if (current.Fire && AttackManager.Instance != null && AttackManager.Instance.CanFire()
            && _currentSnapshot.CurrentAmmo > 0 && !_currentSnapshot.IsReloading)
        {
            if (AttackManager.Instance.TryCreateAttack(current.AimYaw, current.AimPitch, _currentTick, out var attack))
            {
                operation.AttackOperations.Add(attack);

                // 消耗弹药 + 阻止换弹中开火
                if (_currentSnapshot.CurrentAmmo > 0 && !_currentSnapshot.IsReloading)
                {
                    _currentSnapshot.CurrentAmmo--;
                }

                // 生成视觉子弹
                var aimRot = Quaternion.Euler(current.AimPitch, current.AimYaw, 0f);
                var fireDir = aimRot * Vector3.forward;
                var fireOrigin = GetFireOrigin();

                // 将枪口位置写入 AttackOperation，服务端会用此位置广播给其他客户端
                attack.SpawnPos = new Vec3(fireOrigin.x, fireOrigin.y, fireOrigin.z);

                Debug.DrawRay(fireOrigin, fireDir * 2f, Color.red, 1f);

                if (VisualBulletManager.Instance != null)
                {
                    VisualBulletManager.Instance.SpawnLocalBullet(fireOrigin, fireDir, attack.AttackId);
                    // Mark as predicted so authority frame doesn't double-spawn
                    AttackManager.Instance.MarkAttackPredicted(attack.AttackId);

                    if (_localBulletCount++ < 5 || _localBulletCount % 30 == 0)
                        Debug.Log($"[LOCAL-BULLET] #{_localBulletCount} atkId={attack.AttackId} origin={fireOrigin} dir={fireDir} visualBulletMgr.active={VisualBulletManager.Instance.GetActiveBulletCount()}");
                }
                else
                {
                    if (_vbmNullWarnCount++ < 3 || _vbmNullWarnCount % 60 == 0)
                        Debug.LogWarning($"[LOCAL-BULLET] VisualBulletManager.Instance 为 null！(第{_vbmNullWarnCount}次)");
                }
            }
        }
        else if (current.Fire && _fireDiagTick++ % 120 == 0)
        {
            // 诊断：开火条件不满足
            Debug.Log($"[FIRE-DIAG] Fire=true but blocked: atkMgr={AttackManager.Instance != null} canFire={AttackManager.Instance?.CanFire() ?? false} cooldown={AttackManager.Instance?.GetFireCooldown() ?? -1:F2} pending={AttackManager.Instance?.PendingAttackCount ?? -1}");
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
                _currentSnapshot.CurrentAmmo = GameConstants.MaxAmmoPerClip;
                _currentSnapshot.IsReloading = false;
                _reloadTimer = 0f;
            }
            return;
        }

        // 按 R 键且弹药不满 → 开始换弹
        if (input.Reload && _currentSnapshot.CurrentAmmo < GameConstants.MaxAmmoPerClip)
        {
            _currentSnapshot.IsReloading = true;
            _reloadTimer = GameConstants.ReloadTime;
        }
    }

    private void ProcessFire(InputFrame input)
    {
        if (_mainCam == null) return;

        var aimRot = Quaternion.Euler(input.AimPitch, input.AimYaw, 0f);
        var fireDir = aimRot * Vector3.forward;
        var fireOrigin = GetFireOrigin();

        // 生成弹道特效
        if (TracerVFX.Instance != null)
            TracerVFX.Instance.SpawnTracer(fireOrigin, fireDir);

        // 枪口火焰
        if (_playerModel != null && _playerModel.muzzleFlash != null)
            _playerModel.muzzleFlash.Play();

        // 枪声
        if (_playerModel != null && _playerModel.fireSoundClip != null && AudioPoolManager.Instance != null)
            AudioPoolManager.Instance.PlaySound(_playerModel.fireSoundClip, transform.position);
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
        if (_playerModel != null && _playerModel.firePoint != null)
            return _playerModel.firePoint.position;
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
        if (_playerModel != null)
        {
            var animancer = GetComponent<Animancer.AnimancerComponent>();
            var animSet = GetComponent<PlayerAnimationSet>();
            if (animSet != null && animancer != null)
            {
                var deathClip = animSet.GetClip(PlayerAnimType.Death);
                if (deathClip != null)
                    animancer.Play(deathClip, 0.2f);
            }
        }

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
    public bool IsGrounded => _currentSnapshot.IsGrounded;
    public Vec3 Velocity => _currentSnapshot.Velocity;
    public float VerticalVelocity => _currentSnapshot.VerticalVelocity;
    public Transform Cam => _mainCam?.transform;
    public Vector3 LocalMovement => transform.position;
}