namespace ShootingGame.Shared.Hero
{
    /// <summary>
    /// 枪械开火模式。
    /// </summary>
    public enum FireMode
    {
        Single,   // 单发/半自动
        Auto,     // 连发/全自动
        Shotgun   // 霰弹
    }

    /// <summary>
    /// 弹道类型。
    /// </summary>
    public enum BulletType
    {
        Hitscan,    // 即时命中（射线）
        Projectile  // 飞行弹道
    }

    /// <summary>
    /// 枪械模拟数据（纯 C#，双端共用）。
    /// 与 Unity 的 GunConfig(ScriptableObject) 的关系：
    /// GunConfig 是编辑器里的唯一编辑入口（含 VFX/Audio 等客户端视觉字段），
    /// 导出工具把模拟字段导出为 guns.json 供服务器加载，保证双端数值一致。
    /// </summary>
    public class GunConfigData
    {
        /// <summary>唯一 ID（= GunConfig SO 资产名，如 "Rifle_SemiAuto"）</summary>
        public string Id;
        public string GunName = "New Gun";
        public FireMode FireMode = FireMode.Single;
        public BulletType Bullet = BulletType.Hitscan;

        public float FireRate = 0.15f;      // 射击间隔(秒)
        public byte Damage = 25;            // 每发伤害
        public int ClipSize = 30;           // 弹夹容量
        public float ReloadTime = 2.0f;     // 换弹时间(秒)
        public float Range = 200f;          // 射程(米)
        public float SpreadAngle = 0f;      // 基础散射角(度, 0=精准)
        public float BulletSpeed = 100f;    // 弹速(米/秒), 仅 Projectile 有效

        // ---- 伤害衰减 ----
        public float FalloffStart = 1e9f;   // 衰减起始距离(米), 默认不衰减
        public float FalloffEnd = 1e9f;     // 衰减结束距离(米)
        public float FalloffMinMultiplier = 1f; // 衰减到最低时的伤害倍率

        /// <summary>单发后坐上抬量（客户端 FreeLook Y 轴单位，纯视觉）</summary>
        public float RecoilKick = 0f;

        // ---- 移动/连发扩散(Valorant 手感) ----
        public float MoveSpreadAdd = 0f;    // 移动时额外散射角(度)
        public float BloomPerShot = 0f;     // 每连发一发增加的散射角(度)
        public float BloomMax = 0f;         // 连发扩散上限(度)
        public float BloomRecover = 0f;     // 每秒恢复的扩散角(度)

        /// <summary>按距离计算伤害衰减倍率</summary>
        public float GetFalloffMultiplier(float distance)
        {
            if (distance <= FalloffStart) return 1f;
            if (distance >= FalloffEnd) return FalloffMinMultiplier;
            float t = (distance - FalloffStart) / (FalloffEnd - FalloffStart);
            return 1f + (FalloffMinMultiplier - 1f) * t;
        }
    }
}
