using UnityEditor;
using UnityEngine;
using Animancer;
using ShootingGame.Shared.Hero;

/// <summary>
/// 角色 Prefab 生成器。选择模型 + 英雄配置 → 生成完整 Player Prefab。
/// 菜单: 游戏 → 角色 → 生成角色 Prefab
/// </summary>
public class HeroPrefabGenerator : EditorWindow
{
    private GameObject _sourceModel;       // FBX 模型实例
    private int _heroId = 1;               // 英雄 ID (1-3)
    private bool _createNewAvatar = true;  // 是否创建独立 Humanoid Avatar

    [MenuItem("游戏/角色/生成角色 Prefab", false, 200)]
    public static void ShowWindow() => GetWindow<HeroPrefabGenerator>("Hero Prefab Generator");

    private void OnGUI()
    {
        GUILayout.Label("角色 Prefab 生成器", EditorStyles.boldLabel);
        GUILayout.Space(10);

        _sourceModel = (GameObject)EditorGUILayout.ObjectField("角色模型 FBX", _sourceModel, typeof(GameObject), false);
        _heroId = EditorGUILayout.IntSlider("英雄 ID", _heroId, 1, 3);
        _createNewAvatar = EditorGUILayout.Toggle("创建独立 Avatar", _createNewAvatar);

        GUILayout.Space(10);

        // 显示当前 HeroId 的信息
        if (!Application.isPlaying)
            HeroRegistry.Initialize();
        var hero = HeroRegistry.GetHero(_heroId);
        if (hero != null)
        {
            EditorGUILayout.HelpBox($"英雄: {hero.Name} | HP:{hero.MaxHP} 速度:{hero.MoveSpeed}\n初始枪: {(hero.StartingGun != null ? hero.StartingGun.GunName : "无")}",
                MessageType.Info);
        }

        GUILayout.Space(10);

        GUI.enabled = _sourceModel != null;
        if (GUILayout.Button("生成角色 Prefab", GUILayout.Height(40)))
            GeneratePrefab();
        GUI.enabled = true;
    }

    private void GeneratePrefab()
    {
        if (_sourceModel == null)
        {
            EditorUtility.DisplayDialog("错误", "请先选择角色模型 FBX", "OK");
            return;
        }

        // 初始化 HeroRegistry
        HeroRegistry.Initialize();
        var hero = HeroRegistry.GetHero(_heroId);
        if (hero == null)
        {
            EditorUtility.DisplayDialog("错误", $"找不到英雄 ID {_heroId}", "OK");
            return;
        }

        // 1. 实例化模型
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(_sourceModel);
        if (instance == null) instance = Instantiate(_sourceModel);
        instance.name = $"Hero_{hero.Name}";

        // 2. 配置 Animator
        var animator = instance.GetComponent<Animator>();
        if (animator == null) animator = instance.AddComponent<Animator>();
        animator.applyRootMotion = false;
        // Humanoid Avatar 请在 Inspector 中手动设置：选中模型 FBX → Rig → Animation Type = Humanoid → Apply

        // 3. 添加 AnimancerComponent
        var animancer = instance.GetComponent<AnimancerComponent>();
        if (animancer == null)
        {
            animancer = instance.AddComponent<AnimancerComponent>();
            animancer.Animator = animator;
        }

        // 4. 添加 NetPlayerController
        var netCtrl = instance.GetComponent<NetPlayerController>();
        if (netCtrl == null) netCtrl = instance.AddComponent<NetPlayerController>();

        // 5. 添加 PlayerModel
        var playerModel = instance.GetComponent<PlayerModel>();
        if (playerModel == null) playerModel = instance.AddComponent<PlayerModel>();
        playerModel.animancer = animancer;
        playerModel.animator = animator;

        // 5a. 从 Resources 加载 AnimationSet
        var animSet = Resources.Load<PlayerAnimationSet>("FenNi");
        if (animSet != null) playerModel.AnimationSet = animSet;

        // 5b. 创建 firePoint（枪口位置）
        var firePoint = FindOrCreateChild(instance.transform, "FirePoint",
            new Vector3(0, hero.PlayerHeight * 0.85f, 0.3f));
        playerModel.firePoint = firePoint;

        // 5c. 创建 aimTarget
        FindOrCreateChild(instance.transform, "AimTarget",
            new Vector3(0, hero.PlayerHeight * 0.8f, 50f));

        // 6. 添加 BodyPartHitbox
        if (instance.GetComponent<BodyPartHitbox>() == null)
            instance.AddComponent<BodyPartHitbox>();

        // 7. 添加 CursorControl
        if (instance.GetComponent<CursorControl>() == null)
            instance.AddComponent<CursorControl>();

        // 8. 保存为 Prefab
        const string prefabDir = "Assets/Prefabs/Heroes";
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs")) AssetDatabase.CreateFolder("Assets", "Prefabs");
        if (!AssetDatabase.IsValidFolder(prefabDir)) AssetDatabase.CreateFolder("Assets/Prefabs", "Heroes");

        var prefabPath = $"{prefabDir}/Hero_{hero.Name}.prefab";
        var prefab = PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
        DestroyImmediate(instance);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("完成", $"角色 Prefab 已生成:\n{prefabPath}\n\n请在 Inspector 中手动配置:\n- Animator Avatar\n- PlayerModel 的 Camera/Aim 引用\n- 枪械枪口特效", "OK");
        Selection.activeObject = prefab;
        Debug.Log($"[HeroPrefabGenerator] Prefab 生成成功: {prefabPath}");
    }

    private static Transform FindOrCreateChild(Transform parent, string name, Vector3 localPos)
    {
        var existing = parent.Find(name);
        if (existing != null) return existing;
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        return go.transform;
    }

}
