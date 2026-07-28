using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

/// <summary>
/// 一键搭建 HeroSelectScene：复用场景 3D 布局，叠加 Canvas UI。
/// 菜单: ShootingGame > Setup HeroSelectScene
/// </summary>
public class HeroSelectSceneSetup : EditorWindow
{
    private static readonly Color PanelBg = new Color(0, 0, 0, 0.55f);

    [MenuItem("ShootingGame/Setup HeroSelectScene")]
    public static void Setup()
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.name.Contains("HeroSelect"))
        {
            EditorUtility.DisplayDialog("提示", "请先打开 Assets/Scenes/HeroSelectScene.unity", "OK");
            return;
        }

        Undo.SetCurrentGroupName("Setup HeroSelectScene");

        // === 1. 清理 ===
        DestroyIfExists("Weapon_Changer");
        DestroyIfExists("Cloth_MaterialChanger");
        DestroyIfExists("Face_Changer");
        DestroyIfExists("Button_Manager");

        // 禁掉 demo 角色模型
        foreach (var go in scene.GetRootGameObjects())
        {
            if (go == null) continue;
            if (go.GetComponent<Camera>() != null) continue;
            if (go.GetComponent<Light>() != null) continue;
            if (go.GetComponent<Canvas>() != null) continue;
            if (go.name.Contains("---")) { DestroyIfExists(go.name); continue; }

            var anim = go.GetComponentInChildren<Animator>(true);
            if (anim != null && !go.name.Contains("Canvas"))
            {
                go.SetActive(false);
                Debug.Log($"[Setup] 禁用: {go.name}");
            }
        }

        // === 2. 预览锚点 ===
        var anchor = EnsureChild(null, "PreviewAnchor", Vector3.zero);

        // === 3. Bootstrap ===
        var bootGo = EnsureChild(null, "HeroSelectSetup", Vector3.zero);
        var previewCtrl = bootGo.GetComponent<CharacterPreviewController>();
        if (previewCtrl == null) previewCtrl = Undo.AddComponent<CharacterPreviewController>(bootGo);
        var selector = bootGo.GetComponent<HeroSelectController>();
        if (selector == null) selector = Undo.AddComponent<HeroSelectController>(bootGo);

        // === 4. Canvas ===
        var canvasGo = GameObject.Find("HeroSelectCanvas");
        if (canvasGo == null)
        {
            canvasGo = new GameObject("HeroSelectCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Undo.RegisterCreatedObjectUndo(canvasGo, "Create Canvas");
        }
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 0;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        // === 5. 左侧面板 ===
        var panel = EnsureChild(canvasGo, "LeftPanel", Vector3.zero);
        var panelRect = panel.GetComponent<RectTransform>();
        if (panelRect == null) panelRect = panel.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0, 0);
        panelRect.anchorMax = new Vector2(0.22f, 1);
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;
        var panelBg = panel.GetComponent<Image>();
        if (panelBg == null) panelBg = panel.AddComponent<Image>();
        panelBg.color = PanelBg;

        // == Vertical layout ==
        var vlg = panel.GetComponent<VerticalLayoutGroup>();
        if (vlg == null) vlg = panel.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(15, 15, 20, 20);
        vlg.spacing = 8;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        // Content fitter so panel height adapts
        var csf = panel.GetComponent<ContentSizeFitter>();
        if (csf == null) csf = panel.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // == 标题 ==
        var title = MakeUI<Text>(panel, "Title", out _);
        title.text = "角色选择";
        title.fontSize = 32;
        title.fontStyle = FontStyle.Bold;
        title.color = Color.white;
        title.alignment = TextAnchor.MiddleCenter;
        title.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        title.rectTransform.sizeDelta = new Vector2(0, 45);

        // 角色按钮由 HeroSelectController 运行时动态生成（放在 panel 下）

        // == 服装 ==
        var outfitTitle = MakeUI<Text>(panel, "OutfitTitle", out _);
        outfitTitle.text = "服装";
        outfitTitle.fontSize = 22;
        outfitTitle.fontStyle = FontStyle.Bold;
        outfitTitle.color = Color.white;
        outfitTitle.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        outfitTitle.alignment = TextAnchor.MiddleCenter;
        outfitTitle.rectTransform.sizeDelta = new Vector2(0, 28);

        // 服装切换行
        var outfitRow = EnsureChild(panel, "OutfitRow", Vector3.zero);
        var oRowRt = outfitRow.AddMissingComponent<RectTransform>();
        var ohlg = outfitRow.AddMissingComponent<HorizontalLayoutGroup>();
        ohlg.spacing = 6;
        ohlg.childAlignment = TextAnchor.MiddleCenter;
        oRowRt.sizeDelta = new Vector2(0, 40);

        var prevBtn = MakeButton(outfitRow, "◀", 40, 40);
        var outfitLabel = MakeUI<Text>(outfitRow, "OutfitLabel", out _);
        outfitLabel.text = "默认";
        outfitLabel.fontSize = 18;
        outfitLabel.color = Color.white;
        outfitLabel.alignment = TextAnchor.MiddleCenter;
        outfitLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var nextBtn = MakeButton(outfitRow, "▶", 40, 40);

        // == 枪械颜色标题 ==
        var gunTitle = MakeUI<Text>(panel, "GunColorTitle", out _);
        gunTitle.text = "枪械颜色";
        gunTitle.fontSize = 22;
        gunTitle.fontStyle = FontStyle.Bold;
        gunTitle.color = Color.white;
        gunTitle.alignment = TextAnchor.MiddleCenter;
        gunTitle.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        gunTitle.rectTransform.sizeDelta = new Vector2(0, 28);

        // 颜色按钮行 (2行×3个)
        var colorRow1 = EnsureChild(panel, "GunColorRow1", Vector3.zero);
        var cr1Rt = colorRow1.AddMissingComponent<RectTransform>();
        var cr1HLG = colorRow1.AddMissingComponent<HorizontalLayoutGroup>();
        cr1HLG.spacing = 6;
        cr1HLG.childAlignment = TextAnchor.MiddleCenter;
        cr1Rt.sizeDelta = new Vector2(0, 45);

        var colorRow2 = EnsureChild(panel, "GunColorRow2", Vector3.zero);
        var cr2Rt = colorRow2.AddMissingComponent<RectTransform>();
        var cr2HLG = colorRow2.AddMissingComponent<HorizontalLayoutGroup>();
        cr2HLG.spacing = 6;
        cr2HLG.childAlignment = TextAnchor.MiddleCenter;
        cr2Rt.sizeDelta = new Vector2(0, 45);

        Color[] gunColors = { new Color(0.3f,0.3f,0.3f), Color.black,
            new Color(0.7f,0.15f,0.1f), new Color(0.1f,0.3f,0.7f),
            new Color(0.1f,0.6f,0.2f), new Color(0.8f,0.6f,0.1f) };

        for (int i = 0; i < 3; i++) MakeColorBtn(colorRow1, gunColors[i]);
        for (int i = 3; i < 6; i++) MakeColorBtn(colorRow2, gunColors[i]);

        // == 确认按钮 ==
        var confirmBtn = MakeButton(panel, "确认进入战斗", 0, 55);
        confirmBtn.GetComponent<Image>().color = new Color(0.2f, 0.7f, 0.2f);
        var cfLabel = confirmBtn.GetComponentInChildren<Text>();
        cfLabel.fontSize = 22;
        cfLabel.fontStyle = FontStyle.Bold;

        // == 角色信息 ==
        var infoTxt = MakeUI<Text>(panel, "HeroInfo", out _);
        infoTxt.text = "";
        infoTxt.fontSize = 16;
        infoTxt.color = new Color(0.8f, 0.8f, 0.8f);
        infoTxt.alignment = TextAnchor.MiddleCenter;
        infoTxt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        infoTxt.rectTransform.sizeDelta = new Vector2(0, 40);

        // === 6. 绑定引用到 HeroSelectController ===
        var so = new SerializedObject(selector);
        so.FindProperty("_preview").objectReferenceValue = previewCtrl;
        so.FindProperty("_previewSpawnPoint").objectReferenceValue = anchor.transform;

        // 角色按钮模板放在 panel 下
        var charBtnTemplate = MakeButton(panel, "模板角色", 0, 38);
        charBtnTemplate.gameObject.SetActive(false);
        so.FindProperty("_charBtnTemplate").objectReferenceValue = charBtnTemplate.GetComponent<Button>();
        so.FindProperty("_charBtnParent").objectReferenceValue = panel.transform;

        // 颜色按钮模板（第一个）
        var colorBtns = colorRow1.GetComponentsInChildren<Button>();
        var colorBtnTemplate = colorBtns.Length > 0 ? colorBtns[0] : null;
        so.FindProperty("_colorBtnTemplate").objectReferenceValue = colorBtnTemplate;
        so.FindProperty("_colorBtnParent").objectReferenceValue = colorRow1.transform;

        so.FindProperty("_outfitPrevBtn").objectReferenceValue = prevBtn.GetComponent<Button>();
        so.FindProperty("_outfitNextBtn").objectReferenceValue = nextBtn.GetComponent<Button>();
        so.FindProperty("_outfitLabel").objectReferenceValue = outfitLabel;
        so.FindProperty("_heroInfoLabel").objectReferenceValue = infoTxt;
        so.FindProperty("_confirmBtn").objectReferenceValue = confirmBtn.GetComponent<Button>();
        so.FindProperty("_fightScene").stringValue = "Fight";
        so.ApplyModifiedProperties();

        // 配置 CharacterPreviewController 默认服装
        SetupOutfits(previewCtrl);

        // === 7. 保存 ===
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[Setup] HeroSelectScene 配置完成！");
        EditorUtility.DisplayDialog("完成", "场景已配置。\n左侧面板：角色/服装/枪色\n右侧：3D预览\n点「确认进入战斗」开始", "OK");
    }

    // ===== Helpers =====

    private static GameObject EnsureChild(GameObject parent, string name, Vector3 pos)
    {
        var go = parent != null ? FindChild(parent.transform, name)?.gameObject : GameObject.Find(name);
        if (go != null) return go;

        go = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, $"Create {name}");
        if (parent != null) go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = pos;
        return go;
    }

    private static Transform FindChild(Transform parent, string name)
    {
        foreach (Transform t in parent)
        {
            if (t.name == name) return t;
            var f = FindChild(t, name);
            if (f != null) return f;
        }
        return null;
    }

    private static T MakeUI<T>(GameObject parent, string name, out GameObject go) where T : Component
    {
        go = EnsureChild(parent, name, Vector3.zero);
        var c = go.GetComponent<T>();
        if (c == null) c = Undo.AddComponent<T>(go);
        return c;
    }

    private static Button MakeButton(GameObject parent, string label, float w, float h)
    {
        var go = EnsureChild(parent, "Btn_" + label.Replace(" ", ""), Vector3.zero);
        var img = go.AddMissingComponent<Image>();
        img.color = new Color(0.25f, 0.25f, 0.35f);
        var btn = go.AddMissingComponent<Button>();
        var rt = go.GetComponent<RectTransform>();
        if (w > 0 && h > 0) rt.sizeDelta = new Vector2(w, h);
        rt.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Left, 0, w);

        // Label
        var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
        labelGo.transform.SetParent(go.transform, false);
        var lRt = labelGo.GetComponent<RectTransform>();
        lRt.anchorMin = Vector2.zero; lRt.anchorMax = Vector2.one;
        lRt.offsetMin = Vector2.zero; lRt.offsetMax = Vector2.zero;
        var txt = labelGo.GetComponent<Text>();
        txt.text = label;
        txt.fontSize = 16;
        txt.color = Color.white;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        return btn;
    }

    private static void MakeColorBtn(GameObject parent, Color c)
    {
        var btn = MakeButton(parent, "", 45, 45);
        btn.GetComponent<Image>().color = c;
        var label = btn.GetComponentInChildren<Text>();
        if (label != null) label.text = "";
    }

    private static void DestroyIfExists(string name)
    {
        var go = GameObject.Find(name);
        if (go != null) Undo.DestroyObjectImmediate(go);
    }

    private static void SetupOutfits(CharacterPreviewController ctrl)
    {
        var so = new SerializedObject(ctrl);
        var outfits = so.FindProperty("_outfits");
        if (outfits == null || outfits.arraySize > 0) return;

        outfits.arraySize = 2;
        string[][] data = {
            new[] {"默认","PistolGirl_Top","PistolGirl_Pants","PistolGirl_Boots","PistolGirl_Hair","PistolGirl_Face","PistolGirl_Body","PistolGirl_Helmet","PistolGirl_HelmetAddon","PistolGirl_ACC1","PistolGirl_ACC2"},
            new[] {"运动服","PistolGirl_Sportswear_Top","PistolGirl_Sportswear_Pants","PistolGirl_Sportswear_Shoes","PistolGirl_Hair","PistolGirl_Face","PistolGirl_Body","","","PistolGirl_ACC1","PistolGirl_ACC2"}
        };
        string[] keys = {"name","topPrefab","pantsPrefab","shoesPrefab","hairPrefab","facePrefab","bodyPrefab","helmetPrefab","helmetAddonPrefab","acc1Prefab","acc2Prefab"};

        for (int i = 0; i < 2; i++)
        {
            var el = outfits.GetArrayElementAtIndex(i);
            for (int j = 0; j < keys.Length && j < data[i].Length; j++)
                el.FindPropertyRelative(keys[j]).stringValue = data[i][j];
        }
        so.ApplyModifiedProperties();
    }
}

internal static class GoExt
{
    public static T AddMissingComponent<T>(this GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        return c != null ? c : go.AddComponent<T>();
    }
}
