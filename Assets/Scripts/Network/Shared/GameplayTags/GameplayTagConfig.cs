namespace ShootingGame.Shared.GameplayTags
{
    /// <summary>
    /// GameplayTag 初始化配置：定义所有层级标签并注册到 GameplayTagManager。
    /// 在游戏启动时调用 GameplayTagConfig.Initialize()。
    /// </summary>
    public static class GameplayTagConfig
    {
        // --- 标签名称常量（供代码生成器扫描，也供手动引用）---

        // State 分支
        public const string State = "State";
        public const string State_Dead = "State.Dead";
        public const string State_Alive = "State.Alive";
        public const string State_Stunned = "State.Stunned";
        public const string State_Reloading = "State.Reloading";

        // Action 分支
        public const string Action = "Action";
        public const string Action_Firing = "Action.Firing";
        public const string Action_Jumping = "Action.Jumping";
        public const string Action_Running = "Action.Running";
        public const string Action_Aiming = "Action.Aiming";
        public const string Action_Dashing = "Action.Dashing";
        public const string Action_Charging = "Action.Charging";

        // Ability 分支
        public const string Ability = "Ability";
        public const string Ability_Fire = "Ability.Fire";
        public const string Ability_Reload = "Ability.Reload";
        public const string Ability_Jump = "Ability.Jump";
        public const string Ability_Sprint = "Ability.Sprint";

        // Buff 分支
        public const string Buff = "Buff";
        public const string Buff_SpeedBoost = "Buff.SpeedBoost";
        public const string Buff_DamageBoost = "Buff.DamageBoost";
        public const string Buff_DamageResist = "Buff.DamageResist";
        public const string Buff_Invisible = "Buff.Invisible";
        public const string Buff_Unstoppable = "Buff.Unstoppable";

        // Debuff 分支
        public const string Debuff = "Debuff";
        public const string Debuff_Slowed = "Debuff.Slowed";

        // --- 标签 ID（由 Initialize 填充）---

        public static int Id_State;
        public static int Id_State_Dead;
        public static int Id_State_Alive;
        public static int Id_State_Stunned;
        public static int Id_State_Reloading;

        public static int Id_Action;
        public static int Id_Action_Firing;
        public static int Id_Action_Jumping;
        public static int Id_Action_Running;
        public static int Id_Action_Aiming;
        public static int Id_Action_Dashing;
        public static int Id_Action_Charging;

        public static int Id_Ability;
        public static int Id_Ability_Fire;
        public static int Id_Ability_Reload;
        public static int Id_Ability_Jump;
        public static int Id_Ability_Sprint;

        public static int Id_Buff;
        public static int Id_Buff_SpeedBoost;
        public static int Id_Buff_DamageBoost;
        public static int Id_Buff_DamageResist;
        public static int Id_Buff_Invisible;
        public static int Id_Buff_Unstoppable;

        public static int Id_Debuff;
        public static int Id_Debuff_Slowed;

        // --- 按名称缓存 GameplayTag ---

        public static GameplayTag Tag_State;
        public static GameplayTag Tag_State_Dead;
        public static GameplayTag Tag_State_Alive;
        public static GameplayTag Tag_State_Stunned;
        public static GameplayTag Tag_State_Reloading;
        public static GameplayTag Tag_Action;
        public static GameplayTag Tag_Action_Firing;
        public static GameplayTag Tag_Action_Jumping;
        public static GameplayTag Tag_Action_Running;
        public static GameplayTag Tag_Action_Aiming;
        public static GameplayTag Tag_Action_Dashing;
        public static GameplayTag Tag_Action_Charging;
        public static GameplayTag Tag_Ability;
        public static GameplayTag Tag_Ability_Fire;
        public static GameplayTag Tag_Ability_Reload;
        public static GameplayTag Tag_Ability_Jump;
        public static GameplayTag Tag_Ability_Sprint;
        public static GameplayTag Tag_Buff;
        public static GameplayTag Tag_Buff_SpeedBoost;
        public static GameplayTag Tag_Buff_DamageBoost;
        public static GameplayTag Tag_Buff_DamageResist;
        public static GameplayTag Tag_Buff_Invisible;
        public static GameplayTag Tag_Buff_Unstoppable;
        public static GameplayTag Tag_Debuff;
        public static GameplayTag Tag_Debuff_Slowed;

        private static bool _initialized;
        private static readonly object _initLock = new object();

        /// <summary>
        /// 初始化标签系统。幂等操作，线程安全。
        /// </summary>
        public static void Initialize()
        {
            if (_initialized) return;
            lock (_initLock)
            {
                if (_initialized) return;

                GameplayTagManager.Reset();

                // --- 注册所有标签 (层级顺序：父在前) ---
                Id_State = GameplayTagManager.Register(State);
                Id_State_Dead = GameplayTagManager.Register(State_Dead, State);
                Id_State_Alive = GameplayTagManager.Register(State_Alive, State);
                Id_State_Stunned = GameplayTagManager.Register(State_Stunned, State);
                Id_State_Reloading = GameplayTagManager.Register(State_Reloading, State);

                Id_Action = GameplayTagManager.Register(Action);
                Id_Action_Firing = GameplayTagManager.Register(Action_Firing, Action);
                Id_Action_Jumping = GameplayTagManager.Register(Action_Jumping, Action);
                Id_Action_Running = GameplayTagManager.Register(Action_Running, Action);
                Id_Action_Aiming = GameplayTagManager.Register(Action_Aiming, Action);
                Id_Action_Dashing = GameplayTagManager.Register(Action_Dashing, Action);
                Id_Action_Charging = GameplayTagManager.Register(Action_Charging, Action);

                Id_Ability = GameplayTagManager.Register(Ability);
                Id_Ability_Fire = GameplayTagManager.Register(Ability_Fire, Ability);
                Id_Ability_Reload = GameplayTagManager.Register(Ability_Reload, Ability);
                Id_Ability_Jump = GameplayTagManager.Register(Ability_Jump, Ability);
                Id_Ability_Sprint = GameplayTagManager.Register(Ability_Sprint, Ability);

                Id_Buff = GameplayTagManager.Register(Buff);
                Id_Buff_SpeedBoost = GameplayTagManager.Register(Buff_SpeedBoost, Buff);
                Id_Buff_DamageBoost = GameplayTagManager.Register(Buff_DamageBoost, Buff);
                Id_Buff_DamageResist = GameplayTagManager.Register(Buff_DamageResist, Buff);
                Id_Buff_Invisible = GameplayTagManager.Register(Buff_Invisible, Buff);
                Id_Buff_Unstoppable = GameplayTagManager.Register(Buff_Unstoppable, Buff);

                Id_Debuff = GameplayTagManager.Register(Debuff);
                Id_Debuff_Slowed = GameplayTagManager.Register(Debuff_Slowed, Debuff);

                // 预计算层级位掩码
                GameplayTagManager.Bake();

                // 缓存 GameplayTag 实例
                CacheTags();

                _initialized = true;
            }
        }

        private static void CacheTags()
        {
            Tag_State = new GameplayTag(Id_State);
            Tag_State_Dead = new GameplayTag(Id_State_Dead);
            Tag_State_Alive = new GameplayTag(Id_State_Alive);
            Tag_State_Stunned = new GameplayTag(Id_State_Stunned);
            Tag_State_Reloading = new GameplayTag(Id_State_Reloading);
            Tag_Action = new GameplayTag(Id_Action);
            Tag_Action_Firing = new GameplayTag(Id_Action_Firing);
            Tag_Action_Jumping = new GameplayTag(Id_Action_Jumping);
            Tag_Action_Running = new GameplayTag(Id_Action_Running);
            Tag_Action_Aiming = new GameplayTag(Id_Action_Aiming);
            Tag_Action_Dashing = new GameplayTag(Id_Action_Dashing);
            Tag_Action_Charging = new GameplayTag(Id_Action_Charging);
            Tag_Ability = new GameplayTag(Id_Ability);
            Tag_Ability_Fire = new GameplayTag(Id_Ability_Fire);
            Tag_Ability_Reload = new GameplayTag(Id_Ability_Reload);
            Tag_Ability_Jump = new GameplayTag(Id_Ability_Jump);
            Tag_Ability_Sprint = new GameplayTag(Id_Ability_Sprint);
            Tag_Buff = new GameplayTag(Id_Buff);
            Tag_Buff_SpeedBoost = new GameplayTag(Id_Buff_SpeedBoost);
            Tag_Buff_DamageBoost = new GameplayTag(Id_Buff_DamageBoost);
            Tag_Buff_DamageResist = new GameplayTag(Id_Buff_DamageResist);
            Tag_Buff_Invisible = new GameplayTag(Id_Buff_Invisible);
            Tag_Buff_Unstoppable = new GameplayTag(Id_Buff_Unstoppable);
            Tag_Debuff = new GameplayTag(Id_Debuff);
            Tag_Debuff_Slowed = new GameplayTag(Id_Debuff_Slowed);
        }
    }
}
