using UnityEngine;

/// <summary>
/// 把选角界面的外观选择应用到游戏中生成的玩家模型上。
/// 由 BattleManager.SpawnLocalPlayer / SpawnRemotePlayer 调用。
/// </summary>
public static class HeroAppearanceApplier
{
    private static readonly string PartsPath = "CombatGirlsCharacterPack/Pistol_Girl/Prefab/Prefab_Parts/";

    /// <summary>
    /// 应用选角外观：服装部件 + 枪颜色
    /// </summary>
    public static void Apply(GameObject playerModel, int outfitIndex, Color gunColor)
    {
        ApplyOutfit(playerModel, outfitIndex);
        ApplyGunColor(playerModel, gunColor);
    }

    private static void ApplyOutfit(GameObject model, int outfitIndex)
    {
        if (outfitIndex <= 0) return; // 0 = 默认服装，无需修改

        var outfit = GetOutfitSet(outfitIndex);
        if (outfit == null) return;

        ReplacePart(model, "Top", outfit.top);
        ReplacePart(model, "Pants", outfit.pants);
        ReplacePart(model, "Shoes", outfit.shoes);
        ReplacePart(model, "Hair", outfit.hair);
        ReplacePart(model, "Helmet", outfit.helmet);
        ReplacePart(model, "HelmetAddon", outfit.helmetAddon);
        ReplacePart(model, "ACC1", outfit.acc1);
        ReplacePart(model, "ACC2", outfit.acc2);
    }

    private static void ApplyGunColor(GameObject model, Color color)
    {
        // 找到武器模型（Weapon 子节点或名字含 Gun/Weapon 的）
        var weaponT = FindChildRecursive(model.transform, "Weapon");
        if (weaponT == null) weaponT = FindChildRecursive(model.transform, "Gun");
        if (weaponT == null) weaponT = FindChildRecursive(model.transform, "RiflePlaceholder");
        if (weaponT == null) return;

        foreach (var r in weaponT.GetComponentsInChildren<Renderer>(true))
        {
            foreach (var mat in r.materials)
            {
                if (mat.name.Contains("Gun") || mat.name.Contains("Weapon")
                    || mat.name.Contains("Slide") || mat.name.Contains("Body"))
                    mat.color = color;
            }
        }
    }

    private static void ReplacePart(GameObject model, string partName, string prefabName)
    {
        if (string.IsNullOrEmpty(prefabName)) return;

        var existing = FindChildRecursive(model.transform, partName);
        if (existing != null) Object.Destroy(existing.gameObject);

        var prefab = Resources.Load<GameObject>(PartsPath + prefabName);
        if (prefab == null) return;

        var parent = FindParentForPart(model, partName) ?? model.transform;
        var part = Object.Instantiate(prefab, parent);
        part.name = partName;
        part.transform.localPosition = Vector3.zero;
        part.transform.localRotation = Quaternion.identity;
    }

    private static Transform FindParentForPart(GameObject model, string partName)
    {
        return partName switch
        {
            "Top" or "Pants" or "Shoes" or "ACC1" or "ACC2"
                => FindChildRecursive(model.transform, "Hips")
                ?? FindChildRecursive(model.transform, "Pelvis"),
            "Hair" or "Helmet" or "HelmetAddon"
                => FindChildRecursive(model.transform, "Head"),
            _ => model.transform,
        };
    }

    private static Transform FindChildRecursive(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name.Contains(name)) return child;
            var found = FindChildRecursive(child, name);
            if (found != null) return found;
        }
        return null;
    }

    private static OutfitSet GetOutfitSet(int index)
    {
        // 和 CharacterPreviewController 保持同步
        return index switch
        {
            1 => new OutfitSet
            {
                top = "PistolGirl_Sportswear_Top",
                pants = "PistolGirl_Sportswear_Pants",
                shoes = "PistolGirl_Sportswear_Shoes",
                hair = "PistolGirl_Hair",
                helmet = null,
                helmetAddon = null,
                acc1 = "PistolGirl_ACC1",
                acc2 = "PistolGirl_ACC2",
            },
            _ => null,
        };
    }

    private class OutfitSet
    {
        public string top, pants, shoes, hair;
        public string helmet, helmetAddon;
        public string acc1, acc2;
    }
}
