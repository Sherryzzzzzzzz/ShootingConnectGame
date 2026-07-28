using UnityEngine;

namespace ShootingGame.Shared.Hero
{
    /// <summary>
    /// 枪械配置 ScriptableObject。在 Assets 右键 → Create → ShootingGame → Gun Config 创建。
    /// </summary>
    [CreateAssetMenu(menuName = "ShootingGame/Gun Config", fileName = "GunConfig")]
    public class GunConfig : ScriptableObject
    {
        [Header("基础")]
        public string GunName = "New Gun";
        public FireMode FireMode = FireMode.Single;

        [Header("属性")]
        public float FireRate = 0.15f;      // 射击间隔(秒)
        public byte Damage = 25;            // 每发伤害
        public int ClipSize = 30;           // 弹夹容量
        public float ReloadTime = 2.0f;     // 换弹时间(秒)
        public float Range = 200f;          // 射程(米)
        public float SpreadAngle = 0f;      // 散射角(度, 0=精准)

        [Header("弹道")]
        public BulletType Bullet = BulletType.Hitscan;

        [Header("VFX & Audio")]
        public GameObject MuzzleFlashPrefab;
        public GameObject ShellPrefab;
        public AudioClip FireSound;

        [Header("弹道模拟")]
        public float BulletSpeed = 100f;    // 弹速(米/秒), 仅 Projectile 有效

        [Header("伤害衰减")]
        public float FalloffStart = 1e9f;   // 衰减起始距离(米), 默认不衰减
        public float FalloffEnd = 1e9f;     // 衰减结束距离(米)
        public float FalloffMinMultiplier = 1f; // 最低伤害倍率

        [Header("后坐(纯视觉)")]
        public float RecoilKick = 0f;       // 单发后坐上抬量(FreeLook Y 轴单位)

        [Header("扩散(Valorant 手感)")]
        public float MoveSpreadAdd = 0f;    // 移动时额外散射角(度)
        public float BloomPerShot = 0f;     // 每连发一发增加的散射角(度)
        public float BloomMax = 0f;         // 连发扩散上限(度)
        public float BloomRecover = 0f;     // 每秒恢复的扩散角(度)

        /// <summary>
        /// 导出为双端共用的模拟数据（id = SO 资产名）。
        /// </summary>
        public GunConfigData ToGunConfigData(string id)
        {
            return new GunConfigData
            {
                Id = id,
                GunName = GunName,
                FireMode = FireMode,
                Bullet = Bullet,
                FireRate = FireRate,
                Damage = Damage,
                ClipSize = ClipSize,
                ReloadTime = ReloadTime,
                Range = Range,
                SpreadAngle = SpreadAngle,
                RecoilKick = RecoilKick,
                BulletSpeed = BulletSpeed,
                FalloffStart = FalloffStart,
                FalloffEnd = FalloffEnd,
                FalloffMinMultiplier = FalloffMinMultiplier,
                MoveSpreadAdd = MoveSpreadAdd,
                BloomPerShot = BloomPerShot,
                BloomMax = BloomMax,
                BloomRecover = BloomRecover,
            };
        }
    }
}
