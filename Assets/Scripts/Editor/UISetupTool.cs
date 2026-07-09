// UI自动配置工具 - 创建登录界面
#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 编辑器工具：自动创建登录界面UI
/// </summary>
public static class UISetupTool
{
    [MenuItem("游戏/UI/配置完整UI", false, 99)]
    public static void SetupCompleteUI()
    {
        var loginGo = CreateLoginUI();
        var lobbyGo = CreateLobbyUI();
        var heroGo = CreateHeroSelectUI();

        // 登录 ↔ 大厅 交叉引用
        var loginPanel = loginGo.GetComponent<LoginPanel>();
        var lobbyPanel = lobbyGo.GetComponent<LobbyPanel>();

        var loginSerialized = new SerializedObject(loginPanel);
        loginSerialized.FindProperty("lobbyPanel").objectReferenceValue = lobbyGo;
        loginSerialized.ApplyModifiedProperties();

        var lobbySerialized = new SerializedObject(lobbyPanel);
        lobbySerialized.FindProperty("loginPanel").objectReferenceValue = loginGo;
        lobbySerialized.ApplyModifiedProperties();

        Debug.Log("[UISetupTool] ✓ 完整UI配置完成（登录 + 大厅 + 选角）！");
    }

    [MenuItem("游戏/UI/创建登录界面", false, 100)]
    public static GameObject CreateLoginUI()
    {
        var canvas = CreateOrGetCanvas();
        var panel = CreatePanel(canvas.transform, "LoginPanel");

        // 标题
        CreateText(panel.transform, "Title", "联机射击游戏", 32, new Vector2(0, 150), new Vector2(400, 60));

        // 服务器设置
        var serverGroup = CreateVerticalGroup(panel.transform, "ServerSettings", new Vector2(0, 50), new Vector2(400, 120));
        CreateInputField(serverGroup.transform, "ServerIP", "服务器IP", "127.0.0.1");
        CreateInputField(serverGroup.transform, "LobbyPort", "大厅端口", "7778");
        CreateInputField(serverGroup.transform, "BattlePort", "战斗端口", "7777");

        // 用户信息
        var userGroup = CreateVerticalGroup(panel.transform, "UserSettings", new Vector2(0, -50), new Vector2(400, 80));
        CreateInputField(userGroup.transform, "UserID", "用户ID", Random.Range(1, 10000).ToString());
        CreateInputField(userGroup.transform, "Username", "用户名", $"Player_{Random.Range(1000, 9999)}");

        // 按钮 — 第 1 行：连接 + 登录 + Host
        var buttonGroup = CreateHorizontalGroup(panel.transform, "Buttons", new Vector2(0, -150), new Vector2(500, 50));
        CreateButton(buttonGroup.transform, "ConnectButton", "连接服务器");
        CreateButton(buttonGroup.transform, "LoginButton", "登录");
        CreateButton(buttonGroup.transform, "HostPlayButton", "Host & Play");
        CreateButton(buttonGroup.transform, "QuickPlayButton", "快速开始");

        // 状态文本
        CreateText(panel.transform, "StatusText", "未连接", 18, new Vector2(0, -220), new Vector2(400, 40));

        // 加载指示器
        var loadingGo = CreateText(panel.transform, "LoadingIndicator", "加载中...", 16, new Vector2(0, -260), new Vector2(100, 30));
        loadingGo.SetActive(false);

        // 添加 LoginPanel 组件并绑定引用
        var loginPanel = panel.AddComponent<LoginPanel>();
        var serializedObject = new SerializedObject(loginPanel);

        TrySetProperty(serializedObject, "serverIPInput", panel.transform, "ServerIP");
        TrySetProperty(serializedObject, "lobbyPortInput", panel.transform, "LobbyPort");
        TrySetProperty(serializedObject, "battlePortInput", panel.transform, "BattlePort");
        TrySetProperty(serializedObject, "userIdInput", panel.transform, "UserID");
        TrySetProperty(serializedObject, "usernameInput", panel.transform, "Username");
        TrySetProperty(serializedObject, "connectButton", panel.transform, "ConnectButton");
        TrySetProperty(serializedObject, "loginButton", panel.transform, "LoginButton");
        TrySetProperty(serializedObject, "quickPlayButton", panel.transform, "QuickPlayButton");
        TrySetProperty(serializedObject, "hostPlayButton", panel.transform, "HostPlayButton");
        TrySetProperty(serializedObject, "statusText", panel.transform, "StatusText");
        TrySetProperty(serializedObject, "loadingIndicator", panel.transform, "LoadingIndicator");

        serializedObject.ApplyModifiedProperties();

        // 确保基础设施
        EnsureEventSystem();
        EnsureGameInitializer();

        Debug.Log("[UISetupTool] 登录界面创建完成");
        Selection.activeGameObject = panel;
        return panel;
    }

    [MenuItem("游戏/UI/创建大厅界面", false, 101)]
    public static GameObject CreateLobbyUI()
    {
        var canvas = CreateOrGetCanvas();
        var panel = CreatePanel(canvas.transform, "LobbyPanel");
        panel.SetActive(false);

        // 标题
        CreateText(panel.transform, "Title", "游戏大厅", 28, new Vector2(0, 170), new Vector2(300, 50));

        // 状态文本
        CreateText(panel.transform, "StatusText", "欢迎！", 20, new Vector2(0, 140), new Vector2(350, 40));
        CreateText(panel.transform, "QueueStatusText", "选择一个房间加入 或 创建新房间", 16, new Vector2(0, 110), new Vector2(350, 30));
        CreateText(panel.transform, "PlayerCountText", "在线玩家: 0", 14, new Vector2(0, 80), new Vector2(200, 30));

        // 房间创建区域
        var createRoomGroup = CreateHorizontalGroup(panel.transform, "CreateRoomGroup", new Vector2(0, 45), new Vector2(600, 40));
        var nameInput = CreateInputField(createRoomGroup.transform, "RoomNameInput", "房间名称", "My Room");
        var maxInput = CreateInputField(createRoomGroup.transform, "MaxPlayersInput", "人数", "2");
        nameInput.GetComponent<RectTransform>().sizeDelta = new Vector2(150, 30);
        maxInput.GetComponent<RectTransform>().sizeDelta = new Vector2(50, 30);
        CreateButton(createRoomGroup.transform, "CreateRoomButton", "创建房间");
        CreateButton(createRoomGroup.transform, "RefreshRoomsButton", "刷新");

        // 我的房间面板
        var myRoomGroup = CreateHorizontalGroup(panel.transform, "MyRoomPanel", new Vector2(0, 15), new Vector2(500, 40));
        myRoomGroup.SetActive(false);
        CreateText(myRoomGroup.transform, "MyRoomNameText", "", 16, Vector2.zero, new Vector2(180, 30));
        CreateText(myRoomGroup.transform, "MyRoomPlayersText", "", 14, Vector2.zero, new Vector2(120, 30));
        CreateButton(myRoomGroup.transform, "LeaveRoomButton", "离开房间");

        // 匹配按钮（保留旧匹配系统入口）
        var matchGroup = CreateHorizontalGroup(panel.transform, "MatchButtons", new Vector2(0, -15), new Vector2(300, 40));
        CreateButton(matchGroup.transform, "JoinQueueButton", "开始匹配");
        CreateButton(matchGroup.transform, "LeaveQueueButton", "取消匹配");

        // 匹配动画指示器
        var matchingGo = CreateText(panel.transform, "MatchingIndicator", "匹配中...", 16, new Vector2(0, -45), new Vector2(200, 30));
        matchingGo.SetActive(false);

        // --- 房间列表 ScrollView ---
        var listTitle = CreateText(panel.transform, "RoomListTitle", "--- 房间列表 ---", 18, new Vector2(0, -75), new Vector2(200, 30));

        var scrollGO = new GameObject("RoomScrollView");
        scrollGO.transform.SetParent(panel.transform, false);
        var scrollRT = scrollGO.AddComponent<RectTransform>();
        scrollRT.anchorMin = new Vector2(0.1f, 0.05f);
        scrollRT.anchorMax = new Vector2(0.9f, 0.35f);
        scrollRT.sizeDelta = new Vector2(0, 0);

        var scrollImg = scrollGO.AddComponent<Image>();
        scrollImg.color = new Color(0.1f, 0.1f, 0.12f, 0.9f);

        var scroll = scrollGO.AddComponent<ScrollRect>();
        scroll.horizontal = false;

        var viewportGO = new GameObject("Viewport");
        viewportGO.transform.SetParent(scrollGO.transform, false);
        var viewportRT = viewportGO.AddComponent<RectTransform>();
        viewportRT.anchorMin = Vector2.zero;
        viewportRT.anchorMax = Vector2.one;
        viewportRT.offsetMin = Vector2.zero;
        viewportRT.offsetMax = Vector2.zero;
        viewportGO.AddComponent<RectMask2D>();

        var contentGO = new GameObject("RoomListContent");
        contentGO.transform.SetParent(viewportGO.transform, false);
        var contentRT = contentGO.AddComponent<RectTransform>();
        contentRT.anchorMin = new Vector2(0, 1);
        contentRT.anchorMax = new Vector2(1, 1);
        contentRT.pivot = new Vector2(0.5f, 1);
        contentRT.sizeDelta = new Vector2(0, 0);

        var contentVL = contentGO.AddComponent<VerticalLayoutGroup>();
        contentVL.childAlignment = TextAnchor.UpperCenter;
        contentVL.spacing = 5;
        contentVL.padding = new RectOffset(5, 5, 5, 5);
        contentVL.childForceExpandWidth = true;
        contentVL.childForceExpandHeight = false;

        var contentFitter = contentGO.AddComponent<ContentSizeFitter>();
        contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.viewport = viewportRT;
        scroll.content = contentRT;

        // 创建 RoomEntry 预制体并赋值
        var roomEntryPrefab = CreateRoomEntryPrefab();

        // 添加 LobbyPanel 组件并绑定引用
        var lobbyPanel = panel.AddComponent<LobbyPanel>();
        var serializedObject = new SerializedObject(lobbyPanel);

        TrySetProperty(serializedObject, "statusText", panel.transform, "StatusText");
        TrySetProperty(serializedObject, "queueStatusText", panel.transform, "QueueStatusText");
        TrySetProperty(serializedObject, "playerCountText", panel.transform, "PlayerCountText");
        TrySetProperty(serializedObject, "joinQueueButton", panel.transform, "JoinQueueButton");
        TrySetProperty(serializedObject, "leaveQueueButton", panel.transform, "LeaveQueueButton");
        TrySetProperty(serializedObject, "matchingIndicator", panel.transform, "MatchingIndicator");
        TrySetProperty(serializedObject, "roomListContainer", panel.transform, "RoomListContent");
        TrySetProperty(serializedObject, "createRoomButton", panel.transform, "CreateRoomButton");
        TrySetProperty(serializedObject, "refreshRoomsButton", panel.transform, "RefreshRoomsButton");
        TrySetProperty(serializedObject, "roomNameInput", panel.transform, "RoomNameInput");
        TrySetProperty(serializedObject, "maxPlayersInput", panel.transform, "MaxPlayersInput");
        TrySetProperty(serializedObject, "myRoomPanel", panel.transform, "MyRoomPanel");
        TrySetProperty(serializedObject, "myRoomNameText", panel.transform, "MyRoomNameText");
        TrySetProperty(serializedObject, "myRoomPlayersText", panel.transform, "MyRoomPlayersText");
        TrySetProperty(serializedObject, "leaveRoomButton", panel.transform, "LeaveRoomButton");

        // 绑定 RoomEntry 预制体引用
        var roomEntryProp = serializedObject.FindProperty("roomEntryPrefab");
        if (roomEntryProp != null && roomEntryPrefab != null)
        {
            roomEntryProp.objectReferenceValue = roomEntryPrefab;
            Debug.Log($"[UISetupTool] ✓ roomEntryPrefab -> {roomEntryPrefab.name}");
        }

        serializedObject.ApplyModifiedProperties();

        // 设置 LoginPanel ↔ LobbyPanel 交叉引用
        var loginPanelGo = GameObject.Find("LoginPanel");
        if (loginPanelGo != null)
        {
            var loginPanel = loginPanelGo.GetComponent<LoginPanel>();
            if (loginPanel != null)
            {
                var loginSerialized = new SerializedObject(loginPanel);
                var lobbyProp = loginSerialized.FindProperty("lobbyPanel");
                if (lobbyProp != null)
                {
                    lobbyProp.objectReferenceValue = panel;
                    loginSerialized.ApplyModifiedProperties();
                }
            }

            var lobbySerialized = new SerializedObject(lobbyPanel);
            var loginProp = lobbySerialized.FindProperty("loginPanel");
            if (loginProp != null)
            {
                loginProp.objectReferenceValue = loginPanelGo;
                lobbySerialized.ApplyModifiedProperties();
            }
        }

        Debug.Log("[UISetupTool] 大厅界面创建完成");
        Selection.activeGameObject = panel;
        return panel;
    }

    [MenuItem("游戏/初始化/添加GameInitializer", false, 53)]
    public static void EnsureGameInitializer()
    {
        var existing = Object.FindFirstObjectByType<GameInitializer>();
        if (existing != null)
        {
            Debug.Log("[UISetupTool] GameInitializer已存在");
            Selection.activeGameObject = existing.gameObject;
            return;
        }

        var go = new GameObject("GameInitializer");
        go.AddComponent<GameInitializer>();
        Debug.Log("[UISetupTool] GameInitializer创建完成");
        Selection.activeGameObject = go;
    }

    #region 辅助方法

    private static Canvas CreateOrGetCanvas()
    {
        var canvas = Object.FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            var go = new GameObject("Canvas");
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            go.AddComponent<CanvasScaler>();
            go.AddComponent<GraphicRaycaster>();
        }
        return canvas;
    }

    private static GameObject CreatePanel(Transform parent, string name, Color bgColor = default)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);

        var rect = go.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.sizeDelta = Vector2.zero;

        var image = go.AddComponent<Image>();
        if (bgColor == default) bgColor = new Color(0.1f, 0.1f, 0.1f, 0.9f);
        image.color = bgColor;

        return go;
    }

    private static GameObject CreateVerticalGroup(Transform parent, string name, Vector2 position, Vector2 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);

        var rect = go.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        var group = go.AddComponent<VerticalLayoutGroup>();
        group.childAlignment = TextAnchor.MiddleCenter;
        group.spacing = 5;
        group.childControlWidth = true;
        group.childControlHeight = false;

        return go;
    }

    private static GameObject CreateHorizontalGroup(Transform parent, string name, Vector2 position, Vector2 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);

        var rect = go.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        var group = go.AddComponent<HorizontalLayoutGroup>();
        group.childAlignment = TextAnchor.MiddleCenter;
        group.spacing = 10;
        group.childControlWidth = true;
        group.childControlHeight = false;

        return go;
    }

    private static GameObject CreateText(Transform parent, string name, string text, int fontSize, Vector2 position, Vector2 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);

        var rect = go.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;

        var chineseFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/NotoSansCJK-Black-7 SDF.asset");
        if (chineseFont != null)
            tmp.font = chineseFont;

        return go;
    }

    private static GameObject CreateInputField(Transform parent, string name, string placeholder, string defaultValue)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);

        var rect = go.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(200, 30);

        var image = go.AddComponent<Image>();
        image.color = new Color(0.2f, 0.2f, 0.2f);

        var inputField = go.AddComponent<TMP_InputField>();
        var textArea = new GameObject("Text Area");
        textArea.transform.SetParent(go.transform, false);

        var textRect = textArea.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(5, 0);
        textRect.offsetMax = new Vector2(-5, 0);

        var chineseFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/NotoSansCJK-Black-7 SDF.asset");

        var text = new GameObject("Text");
        text.transform.SetParent(textArea.transform, false);
        var textComp = text.AddComponent<TextMeshProUGUI>();
        textComp.fontSize = 18;
        textComp.color = Color.white;
        if (chineseFont != null) textComp.font = chineseFont;
        var textR = text.GetComponent<RectTransform>();
        textR.anchorMin = Vector2.zero;
        textR.anchorMax = Vector2.one;
        textR.sizeDelta = Vector2.zero;

        var placeholderGo = new GameObject("Placeholder");
        placeholderGo.transform.SetParent(textArea.transform, false);
        var placeholderComp = placeholderGo.AddComponent<TextMeshProUGUI>();
        placeholderComp.text = placeholder;
        placeholderComp.fontSize = 18;
        placeholderComp.color = new Color(0.5f, 0.5f, 0.5f);
        if (chineseFont != null) placeholderComp.font = chineseFont;
        var placeholderRect = placeholderGo.GetComponent<RectTransform>();
        placeholderRect.anchorMin = Vector2.zero;
        placeholderRect.anchorMax = Vector2.one;
        placeholderRect.sizeDelta = Vector2.zero;

        inputField.textViewport = textRect;
        inputField.textComponent = textComp;
        inputField.placeholder = placeholderComp;
        inputField.text = defaultValue;

        return go;
    }

    private static GameObject CreateButton(Transform parent, string name, string buttonText)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);

        var rect = go.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(120, 40);

        var image = go.AddComponent<Image>();
        image.color = new Color(0.3f, 0.3f, 0.8f);

        var button = go.AddComponent<Button>();

        var textGo = new GameObject("Text");
        textGo.transform.SetParent(go.transform, false);
        var textRect = textGo.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.sizeDelta = Vector2.zero;

        var text = textGo.AddComponent<TextMeshProUGUI>();
        text.text = buttonText;
        text.fontSize = 18;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;

        var chineseFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/NotoSansCJK-Black-7 SDF.asset");
        if (chineseFont != null)
            text.font = chineseFont;

        return go;
    }

    private static GameObject CreateRoomEntryPrefab()
    {
        var chineseFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/NotoSansCJK-Black-7 SDF.asset");

        // 临时实例（不会保存到场景）
        var entry = new GameObject("RoomEntry");
        var entryRT = entry.AddComponent<RectTransform>();
        entryRT.sizeDelta = new Vector2(500, 50);

        // 背景图
        var bg = entry.AddComponent<Image>();
        bg.color = new Color(0.15f, 0.15f, 0.18f, 0.95f);

        // LayoutElement 控制高度
        var layoutElem = entry.AddComponent<LayoutElement>();
        layoutElem.minHeight = 44;
        layoutElem.preferredHeight = 50;

        // 水平排列子对象
        var hlg = entry.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.spacing = 6;
        hlg.padding = new RectOffset(8, 8, 0, 0);
        hlg.childControlWidth = false;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = true;

        // --- 子对象 ---

        // RoomName (flexible, stretches)
        var roomName = new GameObject("RoomName");
        roomName.transform.SetParent(entry.transform, false);
        var roomNameRT = roomName.AddComponent<RectTransform>();
        roomNameRT.sizeDelta = new Vector2(120, 30);
        var roomNameText = roomName.AddComponent<TextMeshProUGUI>();
        roomNameText.fontSize = 16;
        roomNameText.alignment = TextAlignmentOptions.Left;
        roomNameText.color = Color.white;
        if (chineseFont != null) roomNameText.font = chineseFont;
        var roomNameLE = roomName.AddComponent<LayoutElement>();
        roomNameLE.flexibleWidth = 1;

        // Creator
        var creator = new GameObject("Creator");
        creator.transform.SetParent(entry.transform, false);
        var creatorRT = creator.AddComponent<RectTransform>();
        creatorRT.sizeDelta = new Vector2(80, 30);
        var creatorText = creator.AddComponent<TextMeshProUGUI>();
        creatorText.fontSize = 14;
        creatorText.alignment = TextAlignmentOptions.Left;
        creatorText.color = new Color(0.7f, 0.7f, 0.7f);
        if (chineseFont != null) creatorText.font = chineseFont;

        // PlayerCount
        var playerCount = new GameObject("PlayerCount");
        playerCount.transform.SetParent(entry.transform, false);
        var playerCountRT = playerCount.AddComponent<RectTransform>();
        playerCountRT.sizeDelta = new Vector2(60, 30);
        var playerCountText = playerCount.AddComponent<TextMeshProUGUI>();
        playerCountText.fontSize = 16;
        playerCountText.alignment = TextAlignmentOptions.Center;
        playerCountText.color = Color.green;
        if (chineseFont != null) playerCountText.font = chineseFont;

        // Status (默认隐藏)
        var status = new GameObject("Status");
        status.transform.SetParent(entry.transform, false);
        var statusRT = status.AddComponent<RectTransform>();
        statusRT.sizeDelta = new Vector2(60, 30);
        var statusText = status.AddComponent<TextMeshProUGUI>();
        statusText.fontSize = 12;
        statusText.alignment = TextAlignmentOptions.Center;
        statusText.color = Color.yellow;
        if (chineseFont != null) statusText.font = chineseFont;
        status.SetActive(false);

        // JoinButton
        var joinBtn = new GameObject("JoinButton");
        joinBtn.transform.SetParent(entry.transform, false);
        var joinBtnRT = joinBtn.AddComponent<RectTransform>();
        joinBtnRT.sizeDelta = new Vector2(64, 34);
        var joinBtnImg = joinBtn.AddComponent<Image>();
        joinBtnImg.color = new Color(0.25f, 0.55f, 0.85f);
        joinBtn.AddComponent<Button>();

        var joinBtnTextGo = new GameObject("Text");
        joinBtnTextGo.transform.SetParent(joinBtn.transform, false);
        var joinBtnTextRT = joinBtnTextGo.AddComponent<RectTransform>();
        joinBtnTextRT.anchorMin = Vector2.zero;
        joinBtnTextRT.anchorMax = Vector2.one;
        joinBtnTextRT.sizeDelta = Vector2.zero;
        var joinBtnText = joinBtnTextGo.AddComponent<TextMeshProUGUI>();
        joinBtnText.text = "加入";
        joinBtnText.fontSize = 14;
        joinBtnText.alignment = TextAlignmentOptions.Center;
        joinBtnText.color = Color.white;
        if (chineseFont != null) joinBtnText.font = chineseFont;

        // 保存为预制体资源
        const string prefabDir = "Assets/Prefabs";
        if (!System.IO.Directory.Exists(prefabDir))
            System.IO.Directory.CreateDirectory(prefabDir);

        var prefabPath = $"{prefabDir}/RoomEntry.prefab";
        var prefab = PrefabUtility.SaveAsPrefabAsset(entry, prefabPath);
        Debug.Log($"[UISetupTool] RoomEntry 预制体已保存到 {prefabPath}");

        // 删除临时实例
        Object.DestroyImmediate(entry);

        return prefab;
    }

    private static void EnsureEventSystem()
    {
        var eventSystem = Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>();
        if (eventSystem == null)
        {
            var go = new GameObject("EventSystem");
            go.AddComponent<UnityEngine.EventSystems.EventSystem>();
            go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            Debug.Log("[UISetupTool] EventSystem创建完成");
        }
    }

    private static void TrySetProperty(SerializedObject serializedObject, string propertyName, Transform root, string childName)
    {
        var prop = serializedObject.FindProperty(propertyName);
        if (prop == null)
        {
            Debug.LogWarning($"[UISetupTool] 未找到属性: {propertyName}");
            return;
        }

        Transform child = null;
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == childName)
            {
                child = t;
                break;
            }
        }

        if (child == null)
        {
            Debug.LogWarning($"[UISetupTool] 未找到子对象: {childName}");
            return;
        }

        var fieldInfo = serializedObject.targetObject.GetType().GetField(propertyName,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        if (fieldInfo != null)
        {
            var fieldType = fieldInfo.FieldType;

            if (fieldType == typeof(GameObject))
            {
                prop.objectReferenceValue = child.gameObject;
                Debug.Log($"[UISetupTool] ✓ {propertyName} -> {childName} (GameObject)");
                return;
            }

            var component = child.GetComponent(fieldType);
            if (component != null)
            {
                prop.objectReferenceValue = component;
                Debug.Log($"[UISetupTool] ✓ {propertyName} -> {childName} ({fieldType.Name})");
                return;
            }
        }

        // fallback: 尝试常见组件类型
        var button = child.GetComponent<Button>();
        var inputField = child.GetComponent<TMP_InputField>();
        var text = child.GetComponent<TextMeshProUGUI>();

        if (button != null)
            prop.objectReferenceValue = button;
        else if (inputField != null)
            prop.objectReferenceValue = inputField;
        else if (text != null)
            prop.objectReferenceValue = text;
        else
            Debug.LogWarning($"[UISetupTool] 无法设置 {propertyName}，{childName} 无匹配组件");

        Debug.Log($"[UISetupTool] ✓ {propertyName} -> {childName}");
    }

    [MenuItem("游戏/UI/创建选角界面", false, 102)]
    public static GameObject CreateHeroSelectUI()
    {
        var canvas = CreateOrGetCanvas();
        var panel = CreatePanel(canvas.transform, "HeroSelectPanel", new Color(0.08f, 0.08f, 0.12f, 0.95f));
        panel.SetActive(false);

        // 标题
        CreateText(panel.transform, "TitleText", "选择英雄", 32, new Vector2(0, 200), new Vector2(400, 60));
        // 倒计时
        CreateText(panel.transform, "TimerText", "30", 24, new Vector2(0, 170), new Vector2(100, 40));

        // 角色卡片容器（横向排列，运行时由 HeroSelectPanel.PopulateCards() 动态填充）
        var cardsContainer = CreateHorizontalGroup(panel.transform, "CardContainer", new Vector2(0, 60), new Vector2(550, 200));

        // 状态文本
        CreateText(panel.transform, "StatusText", "请选择你的英雄", 18, new Vector2(0, -80), new Vector2(400, 40));
        // 确认按钮
        CreateButton(panel.transform, "ConfirmButton", "锁定选择");
        // 加载遮罩
        var loadingGo = CreateText(panel.transform, "LoadingOverlay", "等待对手...", 16, new Vector2(0, -170), new Vector2(200, 30));
        loadingGo.SetActive(false);

        // 绑定
        var heroPanel = panel.AddComponent<HeroSelectPanel>();
        var heroSer = new SerializedObject(heroPanel);
        TrySetProperty(heroSer, "_cardContainer", panel.transform, "CardContainer");
        TrySetProperty(heroSer, "_titleText", panel.transform, "TitleText");
        TrySetProperty(heroSer, "_timerText", panel.transform, "TimerText");
        TrySetProperty(heroSer, "_statusText", panel.transform, "StatusText");
        TrySetProperty(heroSer, "_confirmButton", panel.transform, "ConfirmButton");
        TrySetProperty(heroSer, "_loadingOverlay", panel.transform, "LoadingOverlay");
        heroSer.ApplyModifiedProperties();

        Debug.Log("[UISetupTool] 选角界面创建完成（卡片由 HeroRegistry 动态生成）");
        Selection.activeGameObject = panel;
        return panel;
    }

    private static GameObject CreateHeroCard(Transform parent, string name, int heroId, string title, string desc)
    {
        var card = new GameObject(name);
        card.transform.SetParent(parent, false);
        var rt = card.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(160, 180);

        var img = card.AddComponent<Image>();
        img.color = new Color(0.15f, 0.15f, 0.2f, 0.9f);

        var layout = card.AddComponent<LayoutElement>();
        layout.preferredWidth = 160;
        layout.preferredHeight = 180;

        // 垂直排列：名字 → 描述
        var vlg = card.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.spacing = 5;
        vlg.padding = new RectOffset(8, 8, 12, 8);

        var chineseFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/NotoSansCJK-Black-7 SDF.asset");

        // 名字
        var nameGo = new GameObject("Name");
        nameGo.transform.SetParent(card.transform, false);
        var nameText = nameGo.AddComponent<TextMeshProUGUI>();
        nameText.text = title;
        nameText.fontSize = 20;
        nameText.alignment = TextAlignmentOptions.Center;
        nameText.color = Color.white;
        if (chineseFont != null) nameText.font = chineseFont;

        // 描述
        var descGo = new GameObject("Desc");
        descGo.transform.SetParent(card.transform, false);
        var descText = descGo.AddComponent<TextMeshProUGUI>();
        descText.text = desc;
        descText.fontSize = 13;
        descText.alignment = TextAlignmentOptions.Center;
        descText.color = new Color(0.7f, 0.7f, 0.7f);
        if (chineseFont != null) descText.font = chineseFont;

        // Button（覆盖整个卡片）
        var btn = card.AddComponent<Button>();

        return card;
    }

    private static Transform FindChild(Transform parent, string name)
    {
        for (int i = 0; i < parent.childCount; i++)
            if (parent.GetChild(i).name == name)
                return parent.GetChild(i);
        return null;
    }

    private static Transform FindChildDeep(Transform parent, string name)
    {
        var q = new System.Collections.Generic.Queue<Transform>();
        q.Enqueue(parent);
        while (q.Count > 0)
        {
            var t = q.Dequeue();
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
                q.Enqueue(t.GetChild(i));
        }
        return null;
    }

    #endregion
}
#endif
