using UnityEngine;
using ShootingGame.Shared.Hero;

/// <summary>
/// 选人界面的角色预览控制器。加载模型、切换服装、修改枪色。
/// 不播放动画，纯静态展示。挂到 HeroSelectScene 的预览区父节点上。
/// </summary>
public class CharacterPreviewController : MonoBehaviour
{
    [Header("服装配置")]
    [SerializeField] private OutfitSet[] _outfits = new[]
    {
        // 默认服装
        new OutfitSet
        {
            name = "默认",
            topPrefab = "PistolGirl_Top",
            pantsPrefab = "PistolGirl_Pants",
            shoesPrefab = "PistolGirl_Boots",
            hairPrefab = "PistolGirl_Hair",
            facePrefab = "PistolGirl_Face",
            bodyPrefab = "PistolGirl_Body",
            helmetPrefab = "PistolGirl_Helmet",
            helmetAddonPrefab = "PistolGirl_HelmetAddon",
            acc1Prefab = "PistolGirl_ACC1",
            acc2Prefab = "PistolGirl_ACC2",
        },
        // 运动服
        new OutfitSet
        {
            name = "运动服",
            topPrefab = "PistolGirl_Sportswear_Top",
            pantsPrefab = "PistolGirl_Sportswear_Pants",
            shoesPrefab = "PistolGirl_Sportswear_Shoes",
            hairPrefab = "PistolGirl_Hair",
            facePrefab = "PistolGirl_Face",
            bodyPrefab = "PistolGirl_Body",
            helmetPrefab = null,
            helmetAddonPrefab = null,
            acc1Prefab = "PistolGirl_ACC1",
            acc2Prefab = "PistolGirl_ACC2",
        },
    };

    [Header("枪械")]
    [SerializeField] private string _gunPrefabPath = "Desert_Eagle_01";

    private GameObject _currentModel;
    private GameObject _currentGun;
    private int _currentOutfitIndex;
    private static readonly string PartsPath = "CombatGirlsCharacterPack/Pistol_Girl/Prefab/Prefab_Parts/";

    public int OutfitCount => _outfits.Length;

    /// <summary>
    /// 显示指定英雄的预览模型（静态，不播动画）
    /// </summary>
    public void ShowHero(HeroConfig hero, Vector3 position)
    {
        ClearPreview();

        // 优先用英雄专属 Prefab
        if (hero.HeroPrefab != null)
        {
            _currentModel = Instantiate(hero.HeroPrefab, position, Quaternion.identity, transform);
        }
        else
        {
            // 回退：用 FullBody 预制体组装
            var fullBody = Resources.Load<GameObject>("CombatGirlsCharacterPack/Pistol_Girl/Prefab/PistolGirl_FullBody");
            if (fullBody != null)
                _currentModel = Instantiate(fullBody, position, Quaternion.identity, transform);
        }

        if (_currentModel == null)
        {
            Debug.LogError("[Preview] 无法加载角色模型");
            return;
        }

        // 禁用 Animator，纯静态展示
        var animator = _currentModel.GetComponentInChildren<Animator>(true);
        if (animator != null) animator.enabled = false;

        // 禁用所有物理组件
        foreach (var rb in _currentModel.GetComponentsInChildren<Rigidbody>(true))
            rb.isKinematic = true;
        foreach (var col in _currentModel.GetComponentsInChildren<Collider>(true))
            col.enabled = false;

        // 应用默认服装
        SwitchOutfit(0);

        // 加载枪械
        LoadGun();
    }

    /// <summary>
    /// 切换服装
    /// </summary>
    public void SwitchOutfit(int index)
    {
        if (_outfits == null || index < 0 || index >= _outfits.Length) return;
        if (_currentModel == null) return;

        _currentOutfitIndex = index;
        var outfit = _outfits[index];

        // 替换各部分预制体
        ReplacePart("Body", outfit.bodyPrefab);
        ReplacePart("Top", outfit.topPrefab);
        ReplacePart("Pants", outfit.pantsPrefab);
        ReplacePart("Shoes", outfit.shoesPrefab);
        ReplacePart("Hair", outfit.hairPrefab);
        ReplacePart("Face", outfit.facePrefab);
        ReplacePart("Helmet", outfit.helmetPrefab);
        ReplacePart("HelmetAddon", outfit.helmetAddonPrefab);
        ReplacePart("ACC1", outfit.acc1Prefab);
        ReplacePart("ACC2", outfit.acc2Prefab);
    }

    /// <summary>
    /// 设置枪械材质颜色
    /// </summary>
    public void SetGunColor(Color color)
    {
        if (_currentGun == null) return;
        foreach (var renderer in _currentGun.GetComponentsInChildren<Renderer>(true))
        {
            foreach (var mat in renderer.materials)
            {
                // 只改名字含 "Gun" 或 "Weapon" 的材质
                if (mat.name.Contains("Gun") || mat.name.Contains("Weapon") || mat.name.Contains("Slide"))
                    mat.color = color;
            }
        }
    }

    public string GetOutfitName(int index)
    {
        if (_outfits == null || index < 0 || index >= _outfits.Length) return "?";
        return _outfits[index].name;
    }

    private void LoadGun()
    {
        if (_currentModel == null || string.IsNullOrEmpty(_gunPrefabPath)) return;

        var gunPrefab = Resources.Load<GameObject>(PartsPath + _gunPrefabPath);
        if (gunPrefab == null) return;

        // 找到武器挂载点（Weapon/Hand 节点）
        var weaponParent = FindChildRecursive(_currentModel.transform, "Weapon")
                        ?? FindChildRecursive(_currentModel.transform, "Hand_R")
                        ?? _currentModel.transform;

        _currentGun = Instantiate(gunPrefab, weaponParent);
        _currentGun.transform.localPosition = Vector3.zero;
        _currentGun.transform.localRotation = Quaternion.identity;
    }

    private void ReplacePart(string partName, string prefabName)
    {
        if (string.IsNullOrEmpty(prefabName)) return;

        var existing = FindChildRecursive(_currentModel.transform, partName);
        if (existing != null) Destroy(existing.gameObject);

        var prefab = Resources.Load<GameObject>(PartsPath + prefabName);
        if (prefab == null) return;

        var parent = FindParentForPart(partName) ?? _currentModel.transform;
        var part = Instantiate(prefab, parent);
        part.name = partName;
        part.transform.localPosition = Vector3.zero;
        part.transform.localRotation = Quaternion.identity;
    }

    private Transform FindParentForPart(string partName)
    {
        return partName switch
        {
            "Body" or "Hip" => FindChildRecursive(_currentModel.transform, "Hips")
                            ?? FindChildRecursive(_currentModel.transform, "Pelvis"),
            "Top" or "Pants" or "Shoes" or "ACC1" or "ACC2"
                => FindChildRecursive(_currentModel.transform, "Hips"),
            "Hair" or "Face" or "Helmet" or "HelmetAddon"
                => FindChildRecursive(_currentModel.transform, "Head"),
            _ => _currentModel.transform,
        };
    }

    private Transform FindChildRecursive(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name.Contains(name)) return child;
            var found = FindChildRecursive(child, name);
            if (found != null) return found;
        }
        return null;
    }

    private void ClearPreview()
    {
        if (_currentModel != null) { Destroy(_currentModel); _currentModel = null; }
        _currentGun = null;
    }

    [System.Serializable]
    public class OutfitSet
    {
        public string name;
        public string bodyPrefab, topPrefab, pantsPrefab, shoesPrefab;
        public string hairPrefab, facePrefab;
        public string helmetPrefab, helmetAddonPrefab;
        public string acc1Prefab, acc2Prefab;
    }
}
