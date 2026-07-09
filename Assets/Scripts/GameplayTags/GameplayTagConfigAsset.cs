using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ScriptableObject 定义所有 GameplayTag。用于在 Unity Editor 中可视化编辑标签层级。
/// 代码生成器会扫描此文件生成 C# 配置代码。
/// </summary>
[CreateAssetMenu(fileName = "GameplayTagConfig", menuName = "ShootingGame/GameplayTag Config", order = 1)]
public class GameplayTagConfigAsset : ScriptableObject
{
    [Serializable]
    public struct TagDef
    {
        /// <summary>标签全名，如 "State.Dead"</summary>
        public string Name;

        /// <summary>父标签名（如 "State"），根标签留空</summary>
        public string Parent;
    }

    [Tooltip("所有 GameplayTag 定义。顺序应确保父标签在子标签之前。")]
    public List<TagDef> Tags = new List<TagDef>
    {
        // State
        new TagDef { Name = "State" },
        new TagDef { Name = "State.Dead", Parent = "State" },
        new TagDef { Name = "State.Alive", Parent = "State" },
        new TagDef { Name = "State.Stunned", Parent = "State" },
        new TagDef { Name = "State.Reloading", Parent = "State" },

        // Action
        new TagDef { Name = "Action" },
        new TagDef { Name = "Action.Firing", Parent = "Action" },
        new TagDef { Name = "Action.Jumping", Parent = "Action" },
        new TagDef { Name = "Action.Running", Parent = "Action" },
        new TagDef { Name = "Action.Aiming", Parent = "Action" },
        new TagDef { Name = "Action.Dashing", Parent = "Action" },
        new TagDef { Name = "Action.Charging", Parent = "Action" },

        // Ability
        new TagDef { Name = "Ability" },
        new TagDef { Name = "Ability.Fire", Parent = "Ability" },
        new TagDef { Name = "Ability.Reload", Parent = "Ability" },
        new TagDef { Name = "Ability.Jump", Parent = "Ability" },
        new TagDef { Name = "Ability.Sprint", Parent = "Ability" },

        // Buff
        new TagDef { Name = "Buff" },
        new TagDef { Name = "Buff.SpeedBoost", Parent = "Buff" },
        new TagDef { Name = "Buff.DamageBoost", Parent = "Buff" },
        new TagDef { Name = "Buff.DamageResist", Parent = "Buff" },
        new TagDef { Name = "Buff.Invisible", Parent = "Buff" },
        new TagDef { Name = "Buff.Unstoppable", Parent = "Buff" },

        // Debuff
        new TagDef { Name = "Debuff" },
        new TagDef { Name = "Debuff.Slowed", Parent = "Debuff" },
    };
}
