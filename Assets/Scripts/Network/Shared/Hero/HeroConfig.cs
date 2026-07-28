using UnityEngine;

namespace ShootingGame.Shared.Hero
{
    /// <summary>
    /// 英雄配置（纯数据类）。实例由 HeroRegistry 在运行时从 HeroConfigSO 创建。
    /// </summary>
    public class HeroConfig
    {
        public int HeroId;
        public string Name;
        public byte MaxHP;
        public float MoveSpeed;
        public float PlayerRadius;
        public float PlayerHeight;
        public Ability.AbilityConfig[] Abilities;

        /// <summary>关联的枪械配置（从 Resources 加载的 ScriptableObject）</summary>
        public GunConfig StartingGun;

        /// <summary>初始枪械 ID（= GunConfig SO 资产名），与服务器 heroes.json 一致</summary>
        public string StartingGunId;

        /// <summary>初始枪械模拟数据（双端共用，来自 GunRegistry）</summary>
        public GunConfigData Gun;

        // === 客户端视觉字段（服务端为 null） ===

        /// <summary>角色模型 Prefab（客户端专用）</summary>
        public GameObject HeroPrefab;

        /// <summary>Humanoid Avatar，用于动画重定向（客户端专用）</summary>
        public Avatar HeroAvatar;

        /// <summary>选角界面头像（客户端专用）</summary>
        public Sprite HeroIcon;
    }
}
