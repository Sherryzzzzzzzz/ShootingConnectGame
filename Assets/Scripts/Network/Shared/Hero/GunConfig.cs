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
    }

    public enum FireMode
    {
        Single,   // 单发/半自动
        Auto,     // 连发/全自动
        Shotgun   // 霰弹
    }

    public enum BulletType
    {
        Hitscan,    // 即时命中（射线）
        Projectile  // 飞行弹道（慢速子弹）
    }
}
