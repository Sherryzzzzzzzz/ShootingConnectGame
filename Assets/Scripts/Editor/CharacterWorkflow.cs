using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Animancer;

/// <summary>
/// 可视化角色工作流：从角色包 → 可玩角色，一键生成 Prefab + AnimSet + HeroConfig。
/// 菜单: 游戏/角色/可视化工作流
/// </summary>
public class CharacterWorkflow : EditorWindow
{
    private string _characterName = "";
    private string _modelFbxPath = "";
    private string _animFolder = "";
    private string _gunId = "Rifle_SemiAuto";
    private int _heroId = 4;
    private byte _maxHp = 100;
    private float _moveSpeed = 6f;
    private int[] _abilityIds = { 1, 2, 3 };
    private DropdownField _gunDropdown;

    [MenuItem("游戏/角色/可视化工作流", false, 350)]
    public static void ShowWindow() => GetWindow<CharacterWorkflow>("角色工作流");

    private void CreateGUI()
    {
        var root = rootVisualElement;
        root.style.paddingLeft = root.style.paddingRight = 10;
        root.style.paddingTop = root.style.paddingBottom = 10;

        // Title
        root.Add(new Label("🎮 新角色导入工作流") { style = { fontSize = 18, unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 10 } });

        // Character Name
        root.Add(new Label("角色名称:"));
        var nameField = new TextField { value = _characterName };
        nameField.RegisterValueChangedCallback(e => _characterName = e.newValue);
        root.Add(nameField);

        // Model FBX
        root.Add(new Label("模型 FBX:"));
        var modelRow = new VisualElement() { style = { flexDirection = FlexDirection.Row } };
        var modelField = new TextField { value = _modelFbxPath, style = { flexGrow = 1 } };
        modelField.RegisterValueChangedCallback(e => _modelFbxPath = e.newValue);
        modelRow.Add(modelField);
        var modelBtn = new Button(() => {
            var path = EditorUtility.OpenFilePanel("选择模型 FBX", "Assets/", "fbx");
            if (!string.IsNullOrEmpty(path)) { _modelFbxPath = "Assets" + path.Substring(Application.dataPath.Length); modelField.value = _modelFbxPath; }
        }) { text = "浏览" };
        modelRow.Add(modelBtn);
        root.Add(modelRow);

        // Animation folder
        root.Add(new Label("动画文件夹:"));
        var animRow = new VisualElement() { style = { flexDirection = FlexDirection.Row } };
        var animField = new TextField { value = _animFolder, style = { flexGrow = 1 } };
        animField.RegisterValueChangedCallback(e => _animFolder = e.newValue);
        animRow.Add(animField);
        var animBtn = new Button(() => {
            var path = EditorUtility.OpenFolderPanel("选择动画文件夹", "Assets/", "");
            if (!string.IsNullOrEmpty(path)) { _animFolder = "Assets" + path.Substring(Application.dataPath.Length); animField.value = _animFolder; }
        }) { text = "浏览" };
        animRow.Add(animBtn);
        root.Add(animRow);

        // Auto-detect from CombatGirlsCharacterPack
        root.Add(new Button(() => AutoDetect()) { text = "🔍 自动检测 (从 CombatGirlsCharacterPack)", style = { marginTop = 5, marginBottom = 10 } });

        // Stats
        root.Add(new Label("属性:") { style = { marginTop = 10 } });
        var statsRow = new VisualElement() { style = { flexDirection = FlexDirection.Row } };
        var heroIdField = new IntegerField("HeroId") { value = _heroId };
        heroIdField.RegisterValueChangedCallback(e => _heroId = e.newValue);
        statsRow.Add(heroIdField);
        var hpField = new IntegerField("HP") { value = _maxHp };
        hpField.RegisterValueChangedCallback(e => _maxHp = (byte)e.newValue);
        statsRow.Add(hpField);
        var speedField = new FloatField("移速") { value = _moveSpeed };
        speedField.RegisterValueChangedCallback(e => _moveSpeed = e.newValue);
        statsRow.Add(speedField);
        root.Add(statsRow);

        // Gun
        root.Add(new Label("初始枪械:"));
        _gunDropdown = new DropdownField("", new List<string> { "Rifle_SemiAuto", "Shotgun_Pump", "Sniper_BoltAction", "Pistols_Dual" }, 0);
        _gunDropdown.RegisterValueChangedCallback(e => _gunId = e.newValue);
        root.Add(_gunDropdown);

        // Buttons
        var btnRow = new VisualElement() { style = { flexDirection = FlexDirection.Row, marginTop = 15 } };
        btnRow.Add(new Button(() => BuildCharacter()) { text = "⚡ 一键生成角色", style = { flexGrow = 1, height = 35 } });
        root.Add(btnRow);

        // Status
        var status = new Label("") { name = "status", style = { marginTop = 10, whiteSpace = WhiteSpace.Normal } };
        root.Add(status);
    }

    private void AutoDetect()
    {
        var pistolPath = "Assets/CombatGirlsCharacterPack/Pistol_Girl";
        if (AssetDatabase.IsValidFolder(pistolPath))
        {
            _characterName = "PistolGirl";
            _modelFbxPath = $"{pistolPath}/Models/PistolGirl_FullBody.fbx";
            _animFolder = $"{pistolPath}/Animations";
            _heroId = 4;
            _maxHp = 100;
            _moveSpeed = 6f;
            rootVisualElement.Q<TextField>().value = _characterName;
            Debug.Log("[Workflow] 已自动检测 PistolGirl 角色包");
        }
        else Debug.LogWarning("[Workflow] 未找到 CombatGirlsCharacterPack/Pistol_Girl");
    }

    private void BuildCharacter()
    {
        var status = rootVisualElement.Q<Label>("status");
        if (string.IsNullOrEmpty(_characterName)) { status.text = "❌ 请输入角色名称"; return; }
        if (string.IsNullOrEmpty(_modelFbxPath) || !File.Exists(_modelFbxPath)) { status.text = "❌ 模型 FBX 不存在"; return; }

        var steps = new List<string>();
        try
        {
            // 1. Create Prefab
            var prefabPath = $"Assets/Resources/{_characterName}_Player.prefab";
            steps.Add($"1. 创建预制体: {prefabPath}");
            CreatePlayerPrefab(prefabPath);
            steps.Add("   ✅ 预制体已创建");

            // 2. Create AnimSet
            var animSetPath = $"Assets/Resources/{_characterName}_AnimSet.asset";
            steps.Add($"2. 创建动画集: {animSetPath}");
            CreateAnimSet(animSetPath);
            steps.Add("   ✅ 动画集已创建");

            // 3. Create HeroConfigSO
            var heroSoPath = $"Assets/Resources/Heroes/Hero_{_heroId}_{_characterName}.asset";
            steps.Add($"3. 创建英雄配置: {heroSoPath}");
            CreateHeroConfig(heroSoPath);
            steps.Add("   ✅ 英雄配置已创建");

            AssetDatabase.Refresh();
            status.text = string.Join("\n", steps) + "\n\n✅ 完成！在 Hierarchy 测试: 拖入 " + prefabPath;

            // 4. Populate HeroConfigSO with prefab reference
            var heroSo = AssetDatabase.LoadAssetAtPath<ShootingGame.Shared.Hero.HeroConfigSO>(heroSoPath);
            if (heroSo != null)
            {
                heroSo.HeroPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                EditorUtility.SetDirty(heroSo);
            }
        }
        catch (System.Exception ex)
        {
            status.text = string.Join("\n", steps) + $"\n\n❌ 错误: {ex.Message}";
        }
    }

    private void CreatePlayerPrefab(string dstPath)
    {
        var srcPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Player.prefab");
        if (srcPrefab == null) throw new System.Exception("找不到 Player.prefab");

        AssetDatabase.CopyAsset("Assets/Resources/Player.prefab", dstPath);
        AssetDatabase.Refresh();
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(dstPath));

        // Replace visual model
        var oldBody = instance.transform.Find("Body");
        if (oldBody != null) DestroyImmediate(oldBody.gameObject);
        var smrs = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        foreach (var s in smrs) if (s.gameObject != instance) DestroyImmediate(s.gameObject);

        var modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(_modelFbxPath);
        if (modelPrefab == null)
        {
            // FBX not a prefab — instantiate FBX directly
            var modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(_modelFbxPath));
            if (modelInstance == null)
            {
                DestroyImmediate(instance);
                throw new System.Exception($"无法加载模型: {_modelFbxPath}。请确认 FBX 是 Humanoid Rig。");
            }
            modelInstance.transform.SetParent(instance.transform, false);
            modelInstance.transform.localPosition = Vector3.zero;
            modelInstance.name = "Body";
        }
        else
        {
            var model = (GameObject)PrefabUtility.InstantiatePrefab(modelPrefab);
            model.transform.SetParent(instance.transform, false);
            model.transform.localPosition = Vector3.zero;
            model.name = "Body";
        }

        // Destroy Weapon child
        var weapon = instance.transform.Find("Weapon");
        if (weapon != null) DestroyImmediate(weapon.gameObject);

        // Add components
        if (instance.GetComponent<AnimancerComponent>() == null) instance.AddComponent<AnimancerComponent>();
        var view = instance.GetComponent<PlayerAnimationView>();
        if (view == null) view = instance.AddComponent<PlayerAnimationView>();
        view.capsule = instance.GetComponent<CapsuleCollider>();
        if (view.capsule == null)
        {
            var cap = instance.AddComponent<CapsuleCollider>();
            cap.center = new Vector3(0, 0.9f, 0); cap.radius = 0.35f; cap.height = 1.8f;
            view.capsule = cap;
        }
        var bodyAnim = instance.GetComponentInChildren<Animator>(true);
        if (bodyAnim != null)
        {
            bodyAnim.applyRootMotion = false;
            bodyAnim.runtimeAnimatorController = null;
            instance.GetComponent<AnimancerComponent>().Animator = bodyAnim;
        }

        PrefabUtility.SaveAsPrefabAsset(instance, dstPath);
        DestroyImmediate(instance);
    }

    private void CreateAnimSet(string dstPath)
    {
        var set = ScriptableObject.CreateInstance<PlayerAnimationSet>();
        if (!string.IsNullOrEmpty(_animFolder))
        {
            // Auto-detect: walk subdirectories for all FBX files
            var fbxFiles = System.IO.Directory.GetFiles(_animFolder, "*.fbx", System.IO.SearchOption.AllDirectories);
            foreach (var fbx in fbxFiles)
            {
                var relPath = fbx.Replace("\\", "/");
                var clip = LoadFirstClip(relPath);
                if (clip == null) continue;
                var entryName = Path.GetFileNameWithoutExtension(relPath);
                // Map to PistolGirl naming convention
                var mappedName = MapToAnimName(entryName);
                if (mappedName == null) continue;

                set.animations.Add(new PlayerAnimationSet.AnimationEntry
                {
                    name = mappedName,
                    clip = new ClipTransition { Clip = clip }
                });
            }
        }
        AssetDatabase.CreateAsset(set, dstPath);
    }

    private string MapToAnimName(string fbxName)
    {
        var map = new Dictionary<string, string> {
            {"Idle","Rifle_Idle"}, {"Walk","Rifle_WalkFwdLoop"}, {"Run","Rifle_RunFwdLoop"},
            {"AimIdle","Rifle_AimIdle"}, {"AimWalk_F","Rifle_AimWalkF"}, {"AimJog","Rifle_AimJog"},
            {"AimTurn_L90","Rifle_TurnL90"}, {"AimTurn_R90","Rifle_TurnR90"},
            {"Crouch_Idle","Rifle_CrouchIdle"}, {"Crouch_Walk","Rifle_CrouchWalk"}, {"Crouch_Jog","Rifle_CrouchJog"},
            {"AimIdle_Shoot","Rifle_Shoot"}, {"Crouch_AimIdle_Shoot","Rifle_CrouchShoot"},
            {"Die1","Rifle_Death"}, {"Hit1","Rifle_Hit1"}, {"Hit2","Rifle_Hit2"},
            {"Evade","Rifle_JumpUp"}, {"Stun","Rifle_FallingLoop"},
            {"TakeGun","Rifle_DrawGun"}, {"PutGun","Rifle_HolsterGun"},
            {"Crouch_AimIdle","Rifle_CrouchAimIdle"}, {"Crouch_AimWalk_F","Rifle_CrouchAimWalk"},
        };
        return map.GetValueOrDefault(fbxName);
    }

    private AnimationClip LoadFirstClip(string fbxPath)
    {
        foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
            if (obj is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                return clip;
        return null;
    }

    private void CreateHeroConfig(string dstPath)
    {
        var so = ScriptableObject.CreateInstance<ShootingGame.Shared.Hero.HeroConfigSO>();
        so.HeroId = _heroId;
        so.HeroName = _characterName;
        so.MaxHP = _maxHp;
        so.MoveSpeed = _moveSpeed;
        so.AbilityAssetIds = _abilityIds;
        Directory.CreateDirectory("Assets/Resources/Heroes");
        AssetDatabase.CreateAsset(so, dstPath);
    }
}
