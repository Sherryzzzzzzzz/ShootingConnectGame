using UnityEngine;

namespace ShootingGame.Shared.Hero
{
    /// <summary>
    /// 英雄配置 ScriptableObject。在 Assets 右键 → Create → ShootingGame → Hero Config 创建。
    /// 存放在 Resources/Heroes/ 下，运行时由 HeroRegistry 自动加载。
    ///
    /// 可配置：模型 Prefab、Humanoid Avatar、UI 头像、初始枪械、技能等。
    /// </summary>
    [CreateAssetMenu(menuName = "ShootingGame/Hero Config", fileName = "HeroConfig")]
    public class HeroConfigSO : ScriptableObject
    {
        [Header("基础属性")]
        public int HeroId;
        public string HeroName = "New Hero";
        public byte MaxHP = 100;
        public float MoveSpeed = 6f;
        public float PlayerRadius = 0.35f;
        public float PlayerHeight = 1.8f;

        [Header("视觉 - 模型")]
        [Tooltip("角色 Prefab（含 Animator、PlayerModel 等组件），为空则使用 BattleManager 的默认 Prefab")]
        public GameObject HeroPrefab;

        [Tooltip("Humanoid Avatar，用于动画重定向。为空则不修改 Animator.avatar")]
        public Avatar HeroAvatar;

        [Header("视觉 - UI")]
        [Tooltip("选角界面的头像图标")]
        public Sprite HeroIcon;

        [Header("战斗")]
        [Tooltip("初始枪械配置（Resources/Guns 下的 GunConfig）")]
        public GunConfig StartingGun;

        [Header("技能")]
        [Tooltip("技能 Asset ID 列表。运行时由 HeroRegistry 转换为 AbilityConfig[]")]
        public int[] AbilityAssetIds;

        /// <summary>
        /// 转换为共享数据类 HeroConfig（用于传递给 BattleManager 等系统）。
        /// </summary>
        public HeroConfig ToHeroConfig()
        {
            return new HeroConfig
            {
                HeroId = this.HeroId,
                Name = this.HeroName,
                MaxHP = this.MaxHP,
                MoveSpeed = this.MoveSpeed,
                PlayerRadius = this.PlayerRadius,
                PlayerHeight = this.PlayerHeight,
                HeroPrefab = this.HeroPrefab,
                HeroAvatar = this.HeroAvatar,
                HeroIcon = this.HeroIcon,
                StartingGun = this.StartingGun,
                Abilities = null, // 由 HeroRegistry 根据 AbilityAssetIds 构建
            };
        }
    }
}
