using UnityEngine;
using UnityEngine.InputSystem;
using ShootingGame.Shared.ECS;
using ShootingGame.Shared.Math;
using ShootingGame.Shared.Simulation;

/// <summary>
/// 客户端输入采集系统（替代 NetPlayerController.BuildLocalInput）。
/// 从 Unity Input System 读取原始输入，转换为 InputFrame 并写入本地玩家的 InputComponent + InputEdgeComponent。
/// 纯逻辑：不含任何渲染/表现调用。
/// </summary>
public static class ClientInputSystem
{
    private static PlayerInputAction _input;

    /// <summary>确保输入资源已创建并绑定按键。</summary>
    public static void EnsureInput()
    {
        if (_input != null) return;
        UnityEngine.InputSystem.InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        _input = new PlayerInputAction();
        ForceBindInputs();
        _input.Enable();
    }

    /// <summary>释放输入资源。</summary>
    public static void Shutdown()
    {
        if (_input == null) return;
        _input.Disable();
        _input = null;
    }

    public static void ForceBindInputs()
    {
        if (Keyboard.current == null) UnityEngine.InputSystem.InputSystem.AddDevice<Keyboard>();
        if (Mouse.current == null) UnityEngine.InputSystem.InputSystem.AddDevice<Mouse>();

        var moveAction = _input.Simple.Move;
        for (int i = moveAction.bindings.Count - 1; i >= 0; i--)
            moveAction.ChangeBinding(i).Erase();
        moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
        moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
            .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");

        var jumpAction = _input.Simple.Jump;
        for (int i = jumpAction.bindings.Count - 1; i >= 0; i--)
            jumpAction.ChangeBinding(i).Erase();
        jumpAction.AddBinding("<Keyboard>/space");

        var fireAction = _input.Simple.LightAttack;
        for (int i = fireAction.bindings.Count - 1; i >= 0; i--)
            fireAction.ChangeBinding(i).Erase();
        fireAction.AddBinding("<Mouse>/leftButton");

        var reloadAction = _input.Simple.Reload;
        for (int i = reloadAction.bindings.Count - 1; i >= 0; i--)
            reloadAction.ChangeBinding(i).Erase();
        reloadAction.AddBinding("<Keyboard>/r");

        var runAction = _input.Simple.Run;
        for (int i = runAction.bindings.Count - 1; i >= 0; i--)
            runAction.ChangeBinding(i).Erase();
        runAction.AddBinding("<Keyboard>/leftShift");

        var aimAction = _input.Simple.Aim;
        for (int i = aimAction.bindings.Count - 1; i >= 0; i--)
            aimAction.ChangeBinding(i).Erase();
        aimAction.AddBinding("<Mouse>/rightButton");
    }

    /// <summary>
    /// 为本地玩家构建一帧输入并写入 ECS 组件。
    /// </summary>
    public static InputFrame Tick(EntityManager em, Entity entity, int tick, float moveYawDeg)
    {
        EnsureInput();
        if (!em.HasComponent<InputEdgeComponent>(entity))
            em.AddComponent(entity, new InputEdgeComponent());
        var edge = em.GetComponent<InputEdgeComponent>(entity);

        float aimYaw = 0f, aimPitch = 0f;
        var activeCam = Camera.main;
        if (activeCam != null)
        {
            Vector3 cf = activeCam.transform.forward;
            aimYaw = Mathf.Atan2(cf.x, cf.z) * Mathf.Rad2Deg;
            aimPitch = -Mathf.Asin(cf.y) * Mathf.Rad2Deg;
        }

        Vector2 rawInput = _input.Simple.Move.ReadValue<Vector2>();
        if (rawInput.sqrMagnitude < 0.001f)
        {
            float legacyH = Input.GetAxis("Horizontal");
            float legacyV = Input.GetAxis("Vertical");
            if (Mathf.Abs(legacyH) > 0.001f || Mathf.Abs(legacyV) > 0.001f)
                rawInput = new Vector2(legacyH, legacyV);
        }

        float yawRad = moveYawDeg * Mathf.Deg2Rad;
        float cosYaw = Mathf.Cos(yawRad), sinYaw = Mathf.Sin(yawRad);
        float worldX = rawInput.x * cosYaw + rawInput.y * sinYaw;
        float worldZ = rawInput.y * cosYaw - rawInput.x * sinYaw;

        // 边缘检测
        bool jumpPressed = _input.Simple.Jump.IsPressed();
        bool jumpEdge = jumpPressed && !edge.LastJump;
        edge.LastJump = jumpPressed;

        bool reloadPressed = _input.Simple.Reload.IsPressed();
        bool reloadEdge = reloadPressed && !edge.LastReload;
        edge.LastReload = reloadPressed;

        bool ab1 = Edge(ref edge.LastAbility1, _input.Simple.Ability1.IsPressed());
        bool ab2 = Edge(ref edge.LastAbility2, _input.Simple.Ability2.IsPressed());
        bool ab3 = Edge(ref edge.LastAbility3, _input.Simple.Ability3.IsPressed());
        bool ab4 = Edge(ref edge.LastAbility4, _input.Simple.Ability4.IsPressed());

        // 输入兜底：新 Input System 可能静默失败，用旧 Input Manager 兜底
        bool isFire = _input.Simple.LightAttack.IsPressed() || Input.GetMouseButton(0);
        bool isAim = _input.Simple.Aim.IsPressed() || Input.GetMouseButton(1);
        bool isRun = _input.Simple.Run.IsPressed() || Input.GetKey(KeyCode.LeftShift);
        bool isJump = jumpEdge || Input.GetKeyDown(KeyCode.Space);
        bool isReload = reloadEdge || Input.GetKeyDown(KeyCode.R);

        var frame = new InputFrame
        {
            Tick = tick,
            Movement = new Vec2(worldX, worldZ),
            Jump = isJump, Run = isRun, Aim = isAim,
            Fire = isFire, Reload = isReload,
            Ability1 = ab1 || Input.GetKeyDown(KeyCode.Alpha1),
            Ability2 = ab2 || Input.GetKeyDown(KeyCode.Alpha2),
            Ability3 = ab3 || Input.GetKeyDown(KeyCode.Alpha3),
            Ability4 = ab4 || Input.GetKeyDown(KeyCode.Alpha4),
            AimYaw = aimYaw, AimPitch = aimPitch,
            Crouch = Input.GetKey(KeyCode.C) || Input.GetKey(KeyCode.LeftControl)
        };

        em.SetComponent(entity, edge);
        ECSBridge.WriteInput(em, entity, frame);
        return frame;
    }

    private static bool Edge(ref bool prev, bool current)
    {
        bool edge = current && !prev;
        prev = current;
        return edge;
    }
}
