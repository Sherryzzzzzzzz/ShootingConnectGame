// ========================================

// BiperworksPrefabReplace.cs
// ========================================

// Place this file under Assets/Editor/
// Menu: Tools/Biperworks Tool/Biperworks-Prefab Replace
//
// Step 1: Compare/copy missing bones from Prefab B -> Prefab A (search + blacklist file + cascade-uncheck children)
// Step 2: List SkinnedMeshRenderers in B, copy selected mesh objects into A, optionally create required bones, and rebind by bone-name to A.
// Step 3: List Colliders + "Magica*" components under selected B mesh objects, copy components to A counterparts, and validate references.
//         (Reference remap is NOT fully automatic; the tool highlights remaining external references in RED.)
//
// NOTE: Prefab saving will fail if Prefab A contains Missing Script components. Tool can optionally remove them before saving.
//
// Blacklist file format (recommended):
// - One token per line
// - Lines starting with # are comments.
//
// Limitations (by design, for stability):
// - Step2 rebind is name-based (fast). For perfect results use your BiperworksRebinder or humanoid mapping.
// - Step3 copies component serialized values best-effort; it DOES NOT fully remap all object references yet.
//   The tool highlights remaining external references so you can see what’s not portable.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Biperworks.Tools.PrefabReplace
{
    [Serializable]
    public class BonItem
    {
        public bool selected = true;
        public string pathInB;
        public string parentPathInB;
        public int depth;
        public bool isBlacklisted;
        public bool existsInA; // New field for Hide Transferred feature
    }

    [Serializable]
    public class MeshItem
    {
        public bool selected = false;
        public string pathInB; // relative to loadedRootB
        public SkinnedMeshRenderer smrB;
        public MeshRenderer mrB;

        public string meshName;
        public int materialCount;
        public int boneCount;
        public string rootBoneName;

        public int missingBonesInA;
        public bool hasProblems;

        public string problemText;
        public bool existsInA;
        public bool isEmptyGO; // True if this is an Empty GameObject (no renderers)
        public bool isUnderRig; // True if this object is under Rig Root
    }

    public enum CompCategory
    {
        Transform,
        Renderer,
        Collider,
        Script,
        Physics,
        Audio,
        Light,
        Other
    }

    public enum CompSource
    {
        Mesh,
        Bone
    }

    [Serializable]
    public class CompItem
    {
        public bool selected = false;
        public bool locked = false;
        public CompCategory category;
        public CompSource source; // New field for Source Filter
        public string pathInB;
        public string compTypeName;
        public Component compB;

        public bool isMissingScript;

        public int externalRefCount;
        public int nullRefCount;

        public string notes;
        public bool existsInA; // New field for Hide Transferred feature
    }

    public class BiperworksPrefabReplace : EditorWindow
{
    [MenuItem("Tools/Biperworks Tool/Biperworks-Prefab Replace")]
    public static void Open() => GetWindow<BiperworksPrefabReplace>("Biperworks-Prefab Replace");

    // ---- Prefabs ----
    [SerializeField] private GameObject prefabA;
    [SerializeField] private GameObject prefabB;

    [SerializeField] private string rigRootNameA = "root";
    [SerializeField] private string rigRootNameB = "root";

    // Keep PrefabContents loaded while window is open
    private GameObject loadedRootA;
    private GameObject loadedRootB;
    private Transform rigA;
    private Transform rigB;

    // Path maps (relative to rigRoot)
    private Dictionary<string, Transform> mapA;
    private Dictionary<string, Transform> mapB;

    private Texture2D bannerLogo;

    // ---- UI state ----
    private int tab = 0;
    private Vector2 scroll1, scroll2, scroll3;

    // ---- Common options ----
    [SerializeField] private bool removeMissingScriptsBeforeSave = false;

    // =========================
    // Step 1: Bones
    // =========================
    [SerializeField] private List<BonItem> boneItems = new();

    [SerializeField] private string boneSearch = "";
    [SerializeField] private bool showOnlySelectedBones = false;
    [SerializeField] private bool hideBlacklistedBones = false;
    [SerializeField] private bool hideTransferredBones = true; // New Toggle
    [SerializeField] private bool cascadeUncheckChildren = true;


    // Blacklist tokens
    [SerializeField] private List<string> blacklistTokens = new List<string> { "ik", "helper", "twist" };
    [SerializeField] private string blacklistInput = "";
    [SerializeField] private bool showBlacklist = false; // Foldout state

    // =========================
    // Step 2: Meshes
    // =========================
    [SerializeField] private List<MeshItem> meshItems = new();
    [SerializeField] private string meshSearch = "";
    [SerializeField] private bool showOnlySelectedMeshes = false;
    [SerializeField] private bool hideTransferredMeshes = true;

    [SerializeField] private string targetMeshesParentPathInA = "ImportedMeshes";

    // =========================
    // Step 3: Components (Colliders + Magica*)
    // =========================
    [SerializeField] private List<CompItem> compItems = new();
    [SerializeField] private string compSearch = "";
    [SerializeField] private bool showOnlySelectedComps = false;
    [SerializeField] private bool hideTransferredComps = true; // New Toggle
    [SerializeField] private bool showOnlyIssues = false; // New issues filter

    // Redesign: Category Toggles

    [SerializeField] private bool showTransforms = false;
    [SerializeField] private bool showRenderers = false;
    [SerializeField] private bool showColliders = true;
    [SerializeField] private bool showScripts = true;
    [SerializeField] private bool showPhysics = true;
    [SerializeField] private bool showAudio = true;
    [SerializeField] private bool showLight = true;
    [SerializeField] private bool showOther = true;
    [SerializeField] private bool showManualStep2 = false; // Foldout state

    // Redesign: Source Toggles
    [SerializeField] private bool showSourceMesh = true;
    [SerializeField] private bool showSourceBone = true;
    

    // -------------------------
    // Unity lifecycle
    // -------------------------
    private void OnDisable() => UnloadIfNeeded();

    private void OnGUI()
    {
        DrawUIBanner("Biperworks-Prefab Replace", "Developed by Biperworks");

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            DrawHeader();
        }

        EditorGUILayout.Space(5);
        
        tab = GUILayout.Toolbar(tab, new[] { "Step 1: Bone & Collider", "Step 2: Mesh", "Step 3: Inspector" }, GUILayout.Height(25));

        EditorGUILayout.Space(5);

        // ALWAYS draw steps so users can see Scan buttons
        switch (tab)
        {
            case 0: DrawStep1Bones(); break;
            case 1: DrawStep2Meshes(); break;
            case 2: DrawStep3Components(); break;
        }
    }

    private void DrawUIBanner(string title, string subTitle)
    {
        var rect = EditorGUILayout.GetControlRect(false, 40);
        EditorGUI.DrawRect(rect, new Color(0.18f, 0.18f, 0.18f, 1f));
        
        // Load logo if needed
        if (bannerLogo == null)
        {
            string[] guids = AssetDatabase.FindAssets("Biperworks_CI t:Texture2D");
            if (guids.Length > 0)
            {
                bannerLogo = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(guids[0]));
            }
        }

        // Draw logo on the right with subtle transparency (gradient-like feel)
        if (bannerLogo != null)
        {
            float aspect = (float)bannerLogo.width / bannerLogo.height;
            float imageAreaHeight = rect.height; // Exactly banner height (40px)
            float imageAreaWidth = imageAreaHeight * aspect;
            
            // Subtle alpha for "gradient" effect
            var prevColor = GUI.color;
            GUI.color = new Color(1, 1, 1, 0.25f); 
            
            // Align perfectly to the right edge and fit height
            Rect logoRect = new Rect(rect.xMax - imageAreaWidth, rect.y, imageAreaWidth, imageAreaHeight);
            GUI.DrawTexture(logoRect, bannerLogo, ScaleMode.ScaleToFit);
            
            GUI.color = prevColor;
        }

        var titleStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 18,
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = Color.white }
        };
        var subStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.LowerRight,
            normal = { textColor = new Color(0.8f, 0.8f, 0.8f, 1f) }
        };

        EditorGUI.LabelField(new Rect(rect.x + 10, rect.y, rect.width - 20, rect.height), title, titleStyle);
        EditorGUI.LabelField(new Rect(rect.x + 10, rect.y, rect.width - 20, rect.height - 5), subTitle, subStyle);
        
        EditorGUILayout.Space(5);
    }

    // -------------------------
    // Header / Load / Save
    // -------------------------
    private void DrawHeader()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Prefabs", EditorStyles.boldLabel, GUILayout.Width(100));
            using (new EditorGUILayout.VerticalScope())
            {
                prefabA = (GameObject)EditorGUILayout.ObjectField("Prefab A (Target)", prefabA, typeof(GameObject), false);
                prefabB = (GameObject)EditorGUILayout.ObjectField("Prefab B (Source)", prefabB, typeof(GameObject), false);
            }
        }

        EditorGUILayout.Space(2);

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Rig Roots", EditorStyles.boldLabel, GUILayout.Width(100));
            using (new EditorGUILayout.VerticalScope())
            {
                rigRootNameA = EditorGUILayout.TextField("A Rig Root Name", rigRootNameA);
                rigRootNameB = EditorGUILayout.TextField("B Rig Root Name", rigRootNameB);
            }
        }

        EditorGUILayout.Space(2);

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Output Path", EditorStyles.boldLabel, GUILayout.Width(100));
            targetMeshesParentPathInA = EditorGUILayout.TextField(targetMeshesParentPathInA);
        }

        EditorGUILayout.Space(5);

        removeMissingScriptsBeforeSave = EditorGUILayout.ToggleLeft("Remove missing scripts before saving Prefab A (workaround)", removeMissingScriptsBeforeSave);

        if (IsLoaded())
        {
            var blockers = FindMissingScripts(loadedRootA);
            if (blockers.Count > 0)
            {
                DrawColoredHelpBox(
                    $"SAVE BLOCKER: Prefab A contains missing scripts ({blockers.Count}). Saving Prefab A will fail.\nExample: {blockers[0]}",
                    Color.red
                );
            }
        }
    }

    private bool IsLoaded() =>
        loadedRootA != null && loadedRootB != null &&
        rigA != null && rigB != null &&
        mapA != null && mapB != null;

    // ========================================
    // FUNCTION: LoadPrefabs
    // ========================================
    // PURPOSE:
    //   Loads prefab contents into memory and builds path maps for both prefabs.
    //   Clears all existing scan data (bones, meshes, components).
    //
    // MODIFICATION WARNINGS:
    //   - Must ALWAYS be paired with UnloadIfNeeded() to prevent memory leaks
    //   - Changing load order can break path map dependencies
    //   - Clears all UI state (boneItems, meshItems, compItems)
    //   - BuildPathMap depends on rig roots being found first
    // ========================================
    private void LoadPrefabs()
    {
        UnloadIfNeeded();
        boneItems.Clear();
        meshItems.Clear();
        compItems.Clear();

        var pathA = AssetDatabase.GetAssetPath(prefabA);
        var pathB = AssetDatabase.GetAssetPath(prefabB);

        if (string.IsNullOrEmpty(pathA) || string.IsNullOrEmpty(pathB))
        {
            Debug.LogError("Prefab A/B must be prefab assets in Project.");
            return;
        }

        loadedRootA = PrefabUtility.LoadPrefabContents(pathA);
        loadedRootB = PrefabUtility.LoadPrefabContents(pathB);

        rigA = FindRigRoot(loadedRootA.transform, rigRootNameA);
        rigB = FindRigRoot(loadedRootB.transform, rigRootNameB);

        mapA = BuildPathMap(rigA);
        mapB = BuildPathMap(rigB);
    }

    private void UnloadIfNeeded()
    {
        if (loadedRootA != null) PrefabUtility.UnloadPrefabContents(loadedRootA);
        if (loadedRootB != null) PrefabUtility.UnloadPrefabContents(loadedRootB);

        loadedRootA = null; loadedRootB = null;
        rigA = null; rigB = null;
        mapA = null; mapB = null;
    }

    // ========================================
    // FUNCTION: TrySavePrefabA
    // ========================================
    // PURPOSE:
    //   Saves modifications to Prefab A, with optional missing script removal.
    //   Rebuilds path map after saving to reflect new state.
    //
    // MODIFICATION WARNINGS:
    //   - Missing script removal is DESTRUCTIVE and cannot be undone
    //   - Must check for blockers before saving or save will fail silently
    //   - Rebuilds mapA after save - any references to old map become invalid
    //   - Returns false if save fails - caller must check return value
    // ========================================
    private bool TrySavePrefabA()
    {
        if (!IsLoaded()) return false;
        var pathA = AssetDatabase.GetAssetPath(prefabA);

        var blockers = FindMissingScripts(loadedRootA);
        if (blockers.Count > 0)
        {
            if (!removeMissingScriptsBeforeSave)
            {
                Debug.LogError("Cannot save Prefab A due to missing scripts. Enable the workaround or fix them manually.");
                return false;
            }

            int removed = 0;
            foreach (var t in loadedRootA.GetComponentsInChildren<Transform>(true))
                removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);

            Debug.LogWarning($"Removed {removed} missing script components before saving Prefab A.");
        }

        PrefabUtility.SaveAsPrefabAsset(loadedRootA, pathA);
        AssetDatabase.SaveAssets();
        mapA = BuildPathMap(rigA);
        return true;
    }

    // -------------------------
    // Step 1: Bones
    // -------------------------
    private void DrawStep1Bones()
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("Execution", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (DrawStyledButton("Reload & Scan", Color.white, 30))
                    RunStep1Scan();
                if (DrawStyledButton("Apply", Color.white, 30))
                    CopySelectedBonesToA();
            }
        }

        EditorGUILayout.Space(5);

        DrawBlacklistUI();
        
        EditorGUILayout.Space(5);

        // Search Island
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Search", EditorStyles.boldLabel, GUILayout.Width(50));
                boneSearch = EditorGUILayout.TextField(boneSearch, GUILayout.Height(20));
            }
        }
        
        EditorGUILayout.Space(2);

        // Filters & Actions Island
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                showOnlySelectedBones = EditorGUILayout.ToggleLeft("Only Selected", showOnlySelectedBones, GUILayout.Width(110));
                hideBlacklistedBones = EditorGUILayout.ToggleLeft("Hide Blacklisted", hideBlacklistedBones, GUILayout.Width(125));
                hideTransferredBones = EditorGUILayout.ToggleLeft("Hide Transferred", hideTransferredBones, GUILayout.Width(125));
            }
            
            cascadeUncheckChildren = EditorGUILayout.ToggleLeft("Uncheck children when parent unchecked", cascadeUncheckChildren);

            EditorGUILayout.Space(2);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (DrawStyledButton("Check Visible", Color.white, 22)) SetVisibleBones(true);
                if (DrawStyledButton("Uncheck Visible", Color.white, 22)) SetVisibleBones(false);
                if (DrawStyledButton("Invert Visible", Color.white, 22)) InvertVisibleBones();
            }
        }

        EditorGUILayout.Space(5);
        
        if (!IsLoaded())
        {
            EditorGUILayout.HelpBox("Press 'Reload & Scan' to load prefabs and view bones.", MessageType.Info);
            return;
        }

        // Count for display
        int total = boneItems.Count;
        int visible = boneItems.Count(IsBoneVisible);
        int selected = boneItems.Count(b => b.selected);
        
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField($"Bones: {visible} visible / {total} total ({selected} selected)", EditorStyles.boldLabel);
        }

        scroll1 = EditorGUILayout.BeginScrollView(scroll1, GUILayout.ExpandHeight(true));
        foreach (var m in boneItems.OrderBy(x => x.depth).ThenBy(x => x.pathInB))
        {
            if (!IsBoneVisible(m)) continue;

            var rect = EditorGUILayout.BeginHorizontal();
            {
                // Instant Hover Logic
                bool isHovered = rect.Contains(Event.current.mousePosition);
                if (Event.current.type == EventType.MouseMove) Repaint();

                // Gray out if it exists in A (visual indicator, though likely hidden by filter)
                bool exists = m.existsInA;
                using (new EditorGUI.DisabledScope(exists))
                {
                    bool prev = m.selected;
                    m.selected = EditorGUILayout.Toggle(m.selected, GUILayout.Width(18));

                    if (cascadeUncheckChildren && prev && !m.selected)
                        CascadeUncheckChildren(m.pathInB);
                }

                var labelStyle = new GUIStyle(EditorStyles.label);
                if (m.isBlacklisted) labelStyle.normal.textColor = new Color(0.75f, 0.35f, 0.05f); // Orange for blacklist
                else if (exists) labelStyle.normal.textColor = Color.gray; // Gray for already transferred
                
                // Override with black/white on hover for instant feedback
                if (isHovered && !exists && !m.isBlacklisted)
                    labelStyle.normal.textColor = EditorGUIUtility.isProSkin ? Color.white : Color.black;

                string label = $"{new string(' ', m.depth * 2)}{m.pathInB}";
                if (exists) label += " (Exists)";
                EditorGUILayout.LabelField(label, labelStyle);
            }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();

        int parentIssues = CountSelectedBoneParentFallbacks();
        if (parentIssues > 0)
        {
            DrawColoredHelpBox(
                $"WARNING: {parentIssues} selected bones will attach under a higher ancestor because their exact parent path doesn't exist in A.\n" +
                "(Usually fine: they become siblings under the closest existing parent.)",
                new Color(1f, 0.75f, 0.2f)
            );
        }
    }

    private void RunStep1Scan()
    {
        LoadPrefabs();
        CompareBonesInternal();
    }

    private void CompareBonesInternal()
    {
        boneItems.Clear();

        foreach (var kv in mapB)
        {
            var path = kv.Key;
            if (path == "") continue;

            bool inA = mapA.ContainsKey(path);
            bool isBlack = IsBlacklisted(path);

            // Default selection: Start unchecked (user manually selects what to copy)
            bool autoSelect = false;

            boneItems.Add(new BonItem
            {
                selected = autoSelect,
                isBlacklisted = isBlack,
                pathInB = path,
                parentPathInB = GetParentPath(path),
                depth = GetDepth(path),
                existsInA = inA
            });
        }
        Repaint();
    }

    private void CopySelectedBonesToA()
    {
        var selected = boneItems.Where(m => m.selected).OrderBy(m => m.depth).ToList();
        int created = 0;

        foreach (var m in selected)
        {
            if (mapA.ContainsKey(m.pathInB)) continue;
            if (!mapB.TryGetValue(m.pathInB, out var src) || src == null) continue;

            var parentInA = FindClosestExistingParent(mapA, m.parentPathInB);

            var go = new GameObject(src.name);
            var tNew = go.transform;
            tNew.SetParent(parentInA, false);
            tNew.localPosition = src.localPosition;
            tNew.localRotation = src.localRotation;
            tNew.localScale = src.localScale;

            mapA[m.pathInB] = tNew;
            created++;
        }
        
        // After copying, re-scan to update 'existsInA' status
        // After copying, re-scan to update 'existsInA' status
        if (created > 0)
        {
            CompareBonesInternal();
        }

        if (!TrySavePrefabA()) return;
        Debug.Log($"Step1: Created {created} bones in Prefab A.");
    }

    // DrawBoneFilterUI removed and integrated into DrawStep1Bones for island layout

    private bool IsBoneVisible(BonItem m)
    {
        if (showOnlySelectedBones && !m.selected) return false;
        if (hideBlacklistedBones && m.isBlacklisted) return false;
        if (hideTransferredBones && m.existsInA) return false; // New filter
        if (!string.IsNullOrWhiteSpace(boneSearch) &&
            m.pathInB.IndexOf(boneSearch, StringComparison.OrdinalIgnoreCase) < 0)
            return false;
        return true;
    }

    private void SetVisibleBones(bool value)
    {
        foreach (var m in boneItems)
            if (IsBoneVisible(m) && !m.existsInA) m.selected = value; // Don't select existing
    }

    private void InvertVisibleBones()
    {
        foreach (var m in boneItems)
            if (IsBoneVisible(m) && !m.existsInA) m.selected = !m.selected; // Don't select existing
    }

    // ========================================
    // FUNCTION: CascadeUncheckChildren
    // ========================================
    // PURPOSE:
    //   Recursively unchecks all child bones when a parent bone is unchecked.
    //   Used to maintain hierarchy consistency in bone selection.
    //
    // MODIFICATION WARNINGS:
    //   - Recursive operation - infinite loops possible if parent/child logic changes
    //   - Uses string prefix matching - changing path format breaks this
    //   - Called during UI interaction - performance matters for large hierarchies
    // ========================================
    private void CascadeUncheckChildren(string parentPath)
    {
        string prefix = parentPath + "/";
        foreach (var m in boneItems)
            if (m.pathInB.StartsWith(prefix, StringComparison.Ordinal))
                m.selected = false;
    }

    private int CountSelectedBoneParentFallbacks()
    {
        int count = 0;
        foreach (var m in boneItems.Where(x => x.selected))
            if (!mapA.ContainsKey(m.parentPathInB))
                count++;
        return count;
    }

    private void DrawBlacklistUI()
    {
        showBlacklist = EditorGUILayout.Foldout(showBlacklist, "Blacklist Settings (Filter Nodes)", true);
        if (showBlacklist)
        {
            EditorGUILayout.BeginHorizontal();
            blacklistInput = EditorGUILayout.TextField(blacklistInput);
            if (DrawStyledButton("Add", new Color(0.3f, 0.3f, 0.3f, 1f), 18, 60))
            {
                var tok = (blacklistInput ?? "").Trim();
                if (!string.IsNullOrEmpty(tok) && !blacklistTokens.Contains(tok, StringComparer.OrdinalIgnoreCase))
                    blacklistTokens.Add(tok);
                blacklistInput = "";
                RefreshBlacklistFlags();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Active Tokens (Click [x] to remove):", EditorStyles.miniBoldLabel);

            // Display tokens as list with delete buttons
            for (int i = 0; i < blacklistTokens.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"- {blacklistTokens[i]}", GUILayout.Width(200));
                if (DrawStyledButton("x", new Color(0.6f, 0.2f, 0.2f, 1f), 18, 25))
                {
                    blacklistTokens.RemoveAt(i);
                    RefreshBlacklistFlags();
                    EditorGUIUtility.ExitGUI(); // Prevent layout errors after modification
                }
                EditorGUILayout.EndHorizontal();
            }
            
            if (blacklistTokens.Count == 0)
                EditorGUILayout.LabelField("(No tokens - Blacklist disabled)", EditorStyles.miniLabel);
        }
    }

    private void RefreshBlacklistFlags()
    {
        foreach (var m in boneItems)
            m.isBlacklisted = IsBlacklisted(m.pathInB);
        Repaint();
    }

    private bool IsBlacklisted(string path)
    {
        foreach (var tok in blacklistTokens)
        {
            if (string.IsNullOrWhiteSpace(tok)) continue;
            if (path.IndexOf(tok, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    // -------------------------
    // Step 2: Meshes
    // -------------------------
    private void DrawStep2Meshes()
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("Execution", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (DrawStyledButton("Reload & Scan", Color.white, 30))
                    RunStep2Scan();
                if (DrawStyledButton("Apply", Color.white, 30))
                    AutoCopyAndBindMeshes();
            }
        }

        EditorGUILayout.Space(5);

        // Search Island
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Search", EditorStyles.boldLabel, GUILayout.Width(50));
                meshSearch = EditorGUILayout.TextField(meshSearch, GUILayout.Height(20));
            }
        }

        EditorGUILayout.Space(2);

        // Filters & Actions Island
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                showOnlySelectedMeshes = EditorGUILayout.ToggleLeft("Only Selected", showOnlySelectedMeshes, GUILayout.Width(110));
                hideTransferredMeshes = EditorGUILayout.ToggleLeft("Hide Transferred", hideTransferredMeshes, GUILayout.Width(125));
            }
            
            EditorGUILayout.Space(2);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (DrawStyledButton("Check Visible", Color.white, 22)) SetVisibleMeshes(true);
                if (DrawStyledButton("Uncheck Visible", Color.white, 22)) SetVisibleMeshes(false);
                if (DrawStyledButton("Invert Visible", Color.white, 22)) InvertVisibleMeshes();
            }
        }

        EditorGUILayout.Space(5);

        showManualStep2 = EditorGUILayout.Foldout(showManualStep2, "Manual Actions (Advanced)", true);
        if (showManualStep2)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                if (DrawStyledButton("Copy meshes only", new Color(0.4f, 0.4f, 0.4f, 1f), 24))
                    CopySelectedMeshesToA();
                if (DrawStyledButton("Create bones only", new Color(0.4f, 0.4f, 0.4f, 1f), 24))
                    CreateRequiredBonesForSelectedMeshes();
                if (DrawStyledButton("Rebind only", new Color(0.4f, 0.4f, 0.4f, 1f), 24))
                    RebindSelectedMeshesInA_ByName();
            }
        }

        EditorGUILayout.Space(5);

        if (!IsLoaded())
        {
            EditorGUILayout.HelpBox("Press 'Reload & Scan' to load prefabs and view meshes.", MessageType.Info);
            return;
        }

        EditorGUILayout.LabelField($"SMRs in B: {meshItems.Count}", EditorStyles.boldLabel);

        scroll2 = EditorGUILayout.BeginScrollView(scroll2, GUILayout.ExpandHeight(true));
        foreach (var it in meshItems)
        {
            if (!IsMeshVisible(it)) continue;

            UpdateMeshItemStatus(it);

            var rect = EditorGUILayout.BeginHorizontal();
            {
                // Hover detection
                bool isHovered = rect.Contains(Event.current.mousePosition);
                if (Event.current.type == EventType.MouseMove) Repaint();

                bool previousSelected = it.selected;
                it.selected = EditorGUILayout.Toggle(it.selected, GUILayout.Width(18));
                
                // Auto-select children when Empty GameObject is toggled
                if (it.isEmptyGO && it.selected != previousSelected)
                {
                    SelectChildrenOfPath(it.pathInB, it.selected);
                }

                var style = new GUIStyle(EditorStyles.label);
                if (it.hasProblems) style.normal.textColor = Color.red;
                else if (it.existsInA) style.normal.textColor = Color.gray;
                
                // Instant hover color
                if (isHovered && !it.existsInA && !it.hasProblems)
                    style.normal.textColor = EditorGUIUtility.isProSkin ? Color.white : Color.black;

                EditorGUILayout.LabelField(it.pathInB, style, GUILayout.MinWidth(200));
                GUILayout.FlexibleSpace();
                GUILayout.Label($"Bones:{it.boneCount}", EditorStyles.miniLabel, GUILayout.Width(60));
                GUILayout.Label($"Mats:{it.materialCount}", EditorStyles.miniLabel, GUILayout.Width(60));
                GUILayout.Label($"Root:{it.rootBoneName}", EditorStyles.miniLabel, GUILayout.Width(120));
            }
            EditorGUILayout.EndHorizontal();

            if (it.hasProblems && !string.IsNullOrEmpty(it.problemText))
                DrawColoredHelpBox(it.problemText, Color.red);
        }
        EditorGUILayout.EndScrollView();
    }

    private void RunStep2Scan()
    {
        LoadPrefabs();
        ScanMeshesInBInternal();
    }

    // ========================================
    // FUNCTION: ScanMeshesInBInternal
    // ========================================
    // PURPOSE:
    //   Scans Prefab B for all SkinnedMeshRenderers and MeshRenderers.
    //   Identifies "Empty GameObjects" (nodes with no renderers) to preserve hierarchy.
    //
    // MODIFICATION WARNINGS:
    //   - Empty GameObject detection relies on specific renderer checks (SMR/MR)
    //   - Adding new renderer types (e.g., SpriteRenderer) requires updating this logic
    //   - Only scans children of Rig Root B unless otherwise configured
    //   - Mesh name must be unique enough for rebinding logic to work
    // ========================================
    private void ScanMeshesInBInternal()
    {
        meshItems.Clear();

        var smrs = loadedRootB.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        
        // Build set of existing mesh names in A
        var existingNames = new HashSet<string>(StringComparer.Ordinal);
        var parentA = FindByPath(loadedRootA.transform, targetMeshesParentPathInA);
        if (parentA != null)
        {
            foreach (Transform child in parentA)
                existingNames.Add(child.name);
        }

        foreach (var smr in smrs)
        {
            if (smr == null) continue;
            
            // Dual-Scan: Check both Target Folder AND Hierarchy
            string relativePath = GetPathFromRoot(loadedRootB.transform, smr.transform); // Path relative to Prefab Root
            
            // Check Target Folder Path: TargetParent + "/" + relativePath
            bool existsInTargetFolder = false;
            if (!string.IsNullOrEmpty(targetMeshesParentPathInA))
            {
                var targetPath = targetMeshesParentPathInA + "/" + relativePath;
                var found = FindByPath(loadedRootA.transform, targetPath);
                if (found != null && found.gameObject.name == smr.gameObject.name) existsInTargetFolder = true;
            }

            bool existsInHierarchy = false;

            // Check hierarchy match (Global search by relative path)
            var duplicateInA = FindByPath(loadedRootA.transform, relativePath);
            if (duplicateInA != null) existsInHierarchy = true;

            bool inA = existsInTargetFolder || existsInHierarchy;
            bool underRig = smr.transform.IsChildOf(rigB);

            if (existsInTargetFolder && existsInHierarchy)
            {
                 // Found in both places? That's fine, it exists.
            }

            meshItems.Add(new MeshItem
            {
                selected = false,
                smrB = smr,
                pathInB = GetPathFromRoot(loadedRootB.transform, smr.transform),
                meshName = smr.sharedMesh != null ? smr.sharedMesh.name : "(no mesh)",
                materialCount = smr.sharedMaterials != null ? smr.sharedMaterials.Length : 0,
                boneCount = smr.bones != null ? smr.bones.Length : 0,
                rootBoneName = smr.rootBone != null ? smr.rootBone.name : "(null)",
                existsInA = inA,
                isUnderRig = underRig
            });
        }
        // Also scan static meshes (MeshRenderer + MeshFilter)
        var meshFilters = loadedRootB.GetComponentsInChildren<MeshFilter>(true);
        foreach (var mf in meshFilters)
        {
            if (mf == null) continue;
            var mr = mf.GetComponent<MeshRenderer>();
            if (mr == null) continue;

            // Skip if already accessed via SMR (unlikely but possible if same GO has both)
            if (mr.GetComponent<SkinnedMeshRenderer>() != null) continue;

            // Dual-Scan for Static Meshes
            string relativePath = GetPathFromRoot(loadedRootB.transform, mr.transform);
            
            bool existsInTargetFolder = false;
            if (!string.IsNullOrEmpty(targetMeshesParentPathInA))
            {
                var targetPath = targetMeshesParentPathInA + "/" + relativePath;
                var found = FindByPath(loadedRootA.transform, targetPath);
                if (found != null && found.gameObject.name == mr.gameObject.name) existsInTargetFolder = true;
            }

            bool existsInHierarchy = false;
            
            var duplicateInA = FindByPath(loadedRootA.transform, relativePath);
            if (duplicateInA != null) existsInHierarchy = true;

            bool inA = existsInTargetFolder || existsInHierarchy;
            bool underRig = mr.transform.IsChildOf(rigB);

            meshItems.Add(new MeshItem
            {
                selected = false,
                smrB = null,
                mrB = mr,
                pathInB = GetPathFromRoot(loadedRootB.transform, mr.transform),
                meshName = mf.sharedMesh != null ? mf.sharedMesh.name : "(no mesh)",
                materialCount = mr.sharedMaterials != null ? mr.sharedMaterials.Length : 0,
                boneCount = 0,
                rootBoneName = "N/A (Static)",
                existsInA = inA,
                isUnderRig = underRig
            });
        }
        
        // Scan Empty GameObjects (outside Rig Root)
        // These are typically used for organizing meshes in folders
        var allTransforms = loadedRootB.GetComponentsInChildren<Transform>(true);
        foreach (var t in allTransforms)
        {
            if (t == null) continue;
            
            // Skip if under Rig Root (those are handled by Step 1)
            if (t.IsChildOf(rigB)) continue;
            
            // Skip if it's the root itself
            if (t == loadedRootB.transform) continue;
            
            // Check if it's an Empty GameObject (no renderers)
            var smr = t.GetComponent<SkinnedMeshRenderer>();
            var mr = t.GetComponent<MeshRenderer>();
            if (smr != null || mr != null) continue; // Has renderer, already scanned above
            
            
            // This is an Empty GameObject outside Rig Root
            // Dual-Scan for Empty GOs
             string relativePath = GetPathFromRoot(loadedRootB.transform, t);
            
            bool existsInTargetFolder = false;
            if (!string.IsNullOrEmpty(targetMeshesParentPathInA))
            {
                var targetPath = targetMeshesParentPathInA + "/" + relativePath;
                var found = FindByPath(loadedRootA.transform, targetPath);
                if (found != null && found.gameObject.name == t.gameObject.name) existsInTargetFolder = true;
            }

            bool existsInHierarchy = false;
            
            var duplicateInA = FindByPath(loadedRootA.transform, relativePath);
            if (duplicateInA != null) existsInHierarchy = true;

            bool inA = existsInTargetFolder || existsInHierarchy;
            
            meshItems.Add(new MeshItem
            {
                selected = false,
                smrB = null,
                mrB = null,
                pathInB = GetPathFromRoot(loadedRootB.transform, t),
                meshName = "(Empty)",
                materialCount = 0,
                boneCount = 0,
                rootBoneName = "N/A",
                existsInA = inA,
                isEmptyGO = true
            });
        }
        
        Repaint();
    }

    private bool IsMeshVisible(MeshItem it)
    {
        if (hideTransferredMeshes && it.existsInA) return false;
        if (showOnlySelectedMeshes && !it.selected) return false;
        if (!string.IsNullOrWhiteSpace(meshSearch))
        {
            if (it.pathInB.IndexOf(meshSearch, StringComparison.OrdinalIgnoreCase) < 0 &&
                it.meshName.IndexOf(meshSearch, StringComparison.OrdinalIgnoreCase) < 0)
                return false;
        }
        return true;
    }

    private void SetVisibleMeshes(bool value)
    {
        foreach (var it in meshItems)
            if (IsMeshVisible(it)) it.selected = value;
    }

    private void InvertVisibleMeshes()
    {
        foreach (var it in meshItems)
            if (IsMeshVisible(it)) it.selected = !it.selected;
    }
    
    private void SelectChildrenOfPath(string parentPath, bool selected)
    {
        // Select all items whose path starts with parentPath + "/"
        string prefix = parentPath + "/";
        foreach (var it in meshItems)
        {
            if (it.pathInB.StartsWith(prefix, StringComparison.Ordinal))
            {
                it.selected = selected;
            }
        }
    }

    private void UpdateMeshItemStatus(MeshItem it)
    {
        it.hasProblems = false;
        it.problemText = "";
        it.missingBonesInA = 0;

        it.missingBonesInA = 0;

        if (it.smrB == null && it.mrB == null)
        {
            it.hasProblems = true;
            it.problemText = "Renderer is null (possibly removed).";
            return;
        }

        if (it.smrB != null)
        {
            it.missingBonesInA = CountMissingBonesInA_ByName(it.smrB);

            if (it.smrB.bones != null && it.smrB.bones.Any(b => b != null && !b.IsChildOf(rigB)))
            {
                it.hasProblems = true;
                it.problemText = "Some bones are not under B rigRoot. This SMR may not be transferable.";
            }
        }
    }

    private int CountMissingBonesInA_ByName(SkinnedMeshRenderer smrB)
    {
        int missing = 0;
        if (smrB.bones != null)
        {
            foreach (var b in smrB.bones)
            {
                if (b == null) continue;
                if (!FindByName(mapA, b.name)) missing++;
            }
        }
        if (smrB.rootBone != null && !FindByName(mapA, smrB.rootBone.name))
            missing++;
        return missing;
    }

    private void CopySelectedMeshesToA()
    {
        var selected = meshItems.Where(m => m.selected && (m.smrB != null || m.mrB != null || m.isEmptyGO)).ToList();
        if (selected.Count == 0)
        {
            Debug.LogWarning("No meshes selected.");
            return;
        }

        var parentA = EnsurePathUnderRoot(loadedRootA.transform, targetMeshesParentPathInA);
        
        // Track created objects to avoid duplicates
        var createdPaths = new HashSet<string>(StringComparer.Ordinal);

        int copied = 0;
        
        // Sort by depth to ensure parents are created before children
        var sortedItems = selected.OrderBy(it => GetDepth(it.pathInB)).ToList();
        
        foreach (var it in sortedItems)
        {
            // Get source object
            Transform sourceTransform = null;
            if (it.isEmptyGO)
            {
                sourceTransform = FindByPath(loadedRootB.transform, it.pathInB);
                if (sourceTransform == null) continue;
            }
            else
            {
                sourceTransform = it.smrB != null ? it.smrB.transform : it.mrB.transform;
            }
            
            // Calculate target path based on whether object is under Rig Root
            string targetPath;
            Transform targetRootParent;
            
            if (it.isUnderRig)
            {
                // Object is under Rig Root - place under rigA with same relative path
                string pathRelativeToRig = GetPathRelativeTo(rigB, sourceTransform);
                targetPath = GetPathRelativeTo(loadedRootA.transform, rigA) + "/" + pathRelativeToRig;
                targetRootParent = rigA;
            }
            else
            {
                // Object is outside Rig Root - place under targetMeshesParentPathInA
                targetPath = string.IsNullOrEmpty(targetMeshesParentPathInA) 
                    ? it.pathInB 
                    : targetMeshesParentPathInA + "/" + it.pathInB;
                targetRootParent = parentA;
            }
            
            // Skip if already created (can happen with nested selections)
            if (createdPaths.Contains(targetPath)) continue;
            
            // Get parent path and ensure it exists
            string parentPath = GetParentPath(targetPath);
            Transform targetParent = string.IsNullOrEmpty(parentPath)
                ? loadedRootA.transform
                : EnsurePathUnderRoot(loadedRootA.transform, parentPath);
            
            // Create the object
            GameObject clone;
            
            if (it.isEmptyGO)
            {
                // For Empty GameObjects, create simple GameObject
                clone = new GameObject(sourceTransform.gameObject.name);
                clone.transform.SetParent(targetParent, false);
                clone.transform.localPosition = sourceTransform.localPosition;
                clone.transform.localRotation = sourceTransform.localRotation;
                clone.transform.localScale = sourceTransform.localScale;
            }
            else
            {
                // For Mesh objects, instantiate
                clone = Instantiate(sourceTransform.gameObject);
                clone.name = sourceTransform.gameObject.name;
                clone.transform.SetParent(targetParent, false);
            }
            
            createdPaths.Add(targetPath);
            copied++;
        }

        if (!TrySavePrefabA()) return;
        Debug.Log($"Step2: Copied {copied} mesh objects with smart routing (Rig meshes -> rigA, others -> '{targetMeshesParentPathInA}').");
    }

    private void CreateRequiredBonesForSelectedMeshes()
    {
        var selected = meshItems.Where(m => m.selected && m.smrB != null).ToList();
        if (selected.Count == 0)
        {
            Debug.LogWarning("No meshes selected.");
            return;
        }

        var requiredPaths = new HashSet<string>(StringComparer.Ordinal);

        foreach (var it in selected)
        {
            var smr = it.smrB;

            if (smr.rootBone != null && smr.rootBone.IsChildOf(rigB))
                requiredPaths.Add(GetPathRelativeTo(rigB, smr.rootBone));

            if (smr.bones != null)
            {
                foreach (var b in smr.bones)
                {
                    if (b == null) continue;
                    if (!b.IsChildOf(rigB)) continue;
                    requiredPaths.Add(GetPathRelativeTo(rigB, b));
                }
            }
        }

        var ordered = requiredPaths.OrderBy(GetDepth).ToList();
        int created = 0;

        foreach (var path in ordered)
        {
            if (string.IsNullOrEmpty(path)) continue;
            if (mapA.ContainsKey(path)) continue;

            if (!mapB.TryGetValue(path, out var src) || src == null) continue;

            var parentInA = FindClosestExistingParent(mapA, GetParentPath(path));

            var go = new GameObject(src.name);
            var tNew = go.transform;
            tNew.SetParent(parentInA, false);
            tNew.localPosition = src.localPosition;
            tNew.localRotation = src.localRotation;
            tNew.localScale = src.localScale;

            mapA[path] = tNew;
            created++;
        }

        if (!TrySavePrefabA()) return;
        Debug.Log($"Step2: Created {created} required bones for selected meshes.");
    }

    private void RebindSelectedMeshesInA_ByName()
    {
        var parentA = FindByPath(loadedRootA.transform, targetMeshesParentPathInA);
        if (parentA == null)
        {
            Debug.LogWarning($"Target parent not found in A: {targetMeshesParentPathInA}");
            return;
        }

        var nameMapA = BuildNameMap(rigA);
        int rebound = 0;

        foreach (var it in meshItems.Where(m => m.selected))
        {
            if (it.smrB == null) continue;

            var clone = FindFirstChildByNameRecursive(parentA, it.smrB.gameObject.name);
            if (clone == null) continue;

            var smrA = clone.GetComponent<SkinnedMeshRenderer>();
            if (smrA == null) continue;

            if (it.smrB.rootBone != null && nameMapA.TryGetValue(it.smrB.rootBone.name, out var newRoot))
                smrA.rootBone = newRoot;

            if (it.smrB.bones != null)
            {
                var newBones = new Transform[it.smrB.bones.Length];
                for (int i = 0; i < newBones.Length; i++)
                {
                    var oldB = it.smrB.bones[i];
                    if (oldB != null && nameMapA.TryGetValue(oldB.name, out var nb))
                        newBones[i] = nb;
                }
                smrA.bones = newBones;
            }

            smrA.updateWhenOffscreen = true;
            EditorUtility.SetDirty(smrA);
            rebound++;
        }

        if (!TrySavePrefabA()) return;
        Debug.Log($"Step2: Rebound {rebound} meshes in Prefab A (name-based).");
        if (!TrySavePrefabA()) return;
        Debug.Log($"Step2: Rebound {rebound} meshes in Prefab A (name-based).");
    }

    private void AutoCopyAndBindMeshes()
    {
        Debug.Log(">>> Starting Auto Copy & Bind...");
        CopySelectedMeshesToA();
        CreateRequiredBonesForSelectedMeshes();
        RebindSelectedMeshesInA_ByName();
        Debug.Log("<<< Auto Copy & Bind Complete.");
        
        // Auto-rescan to show updated state
        ScanMeshesInBInternal();
    }

    // -------------------------
    // Step 3: Components
    // -------------------------
    private void DrawStep3Components()
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("Execution", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (DrawStyledButton("Reload & Scan", Color.white, 30))
                    RunStep3Scan();
                if (DrawStyledButton("Apply", Color.white, 30))
                    CopySelectedComponentsToA();
            }
        }

        EditorGUILayout.Space(5);

        EditorGUILayout.Space(5);

        // Search Island
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Search", EditorStyles.boldLabel, GUILayout.Width(50));
                compSearch = EditorGUILayout.TextField(compSearch, GUILayout.Height(20));
            }
        }

        EditorGUILayout.Space(2);

        // Filters & Actions Island
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                showOnlySelectedComps = EditorGUILayout.ToggleLeft("Only Selected", showOnlySelectedComps, GUILayout.Width(110));
                hideTransferredComps = EditorGUILayout.ToggleLeft("Hide Transferred", hideTransferredComps, GUILayout.Width(125));
                showOnlyIssues = EditorGUILayout.ToggleLeft("Show Only Issues", showOnlyIssues, GUILayout.Width(125));
            }
            
            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField("Category Filters", EditorStyles.miniBoldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                showTransforms = EditorGUILayout.ToggleLeft("Transform", showTransforms, GUILayout.Width(75));
                showRenderers = EditorGUILayout.ToggleLeft("Renderer", showRenderers, GUILayout.Width(72));
                showColliders = EditorGUILayout.ToggleLeft("Collider", showColliders, GUILayout.Width(65));
                showScripts = EditorGUILayout.ToggleLeft("Script", showScripts, GUILayout.Width(58));
                showPhysics = EditorGUILayout.ToggleLeft("Physics", showPhysics, GUILayout.Width(65));
                showAudio = EditorGUILayout.ToggleLeft("Audio", showAudio, GUILayout.Width(55));
                showLight = EditorGUILayout.ToggleLeft("Light", showLight, GUILayout.Width(52));
                showOther = EditorGUILayout.ToggleLeft("Other", showOther, GUILayout.Width(52));
                GUILayout.FlexibleSpace();
                showSourceMesh = EditorGUILayout.ToggleLeft("Mesh", showSourceMesh, GUILayout.Width(55));
                showSourceBone = EditorGUILayout.ToggleLeft("Bone", showSourceBone, GUILayout.Width(55));
            }

            EditorGUILayout.Space(2);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (DrawStyledButton("Check Visible", Color.white, 22)) SetVisibleComps(true);
                if (DrawStyledButton("Uncheck Visible", Color.white, 22)) SetVisibleComps(false);
                if (DrawStyledButton("Invert Visible", Color.white, 22)) InvertVisibleComps();
            }
        }

        EditorGUILayout.Space(5);

        if (!IsLoaded())
        {
            EditorGUILayout.HelpBox("Press 'Reload & Scan' to view components.", MessageType.Info);
            return;
        }

        // Detailed count for user feedback
        int total = compItems.Count;
        int visibleCount = compItems.Count(IsCompVisible);
        int selectedCount = compItems.Count(c => c.selected);
        int hiddenSelected = compItems.Count(c => c.selected && !IsCompVisible(c));

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField($"Components: {visibleCount} visible / {total} total ({selectedCount} selected)", EditorStyles.boldLabel);
            if (hiddenSelected > 0)
            {
                GUI.contentColor = new Color(1f, 0.75f, 0.2f);
                if (GUILayout.Button($"⚠️ {hiddenSelected} selected items hidden (Click to see)", EditorStyles.miniButton))
                {
                    showOnlySelectedComps = true;
                }
                GUI.contentColor = Color.white;
            }
        }

        scroll3 = EditorGUILayout.BeginScrollView(scroll3, GUILayout.ExpandHeight(true));
        foreach (var ci in compItems)
        {
            if (!IsCompVisible(ci)) continue;

            var rect = EditorGUILayout.BeginHorizontal();
            {
                // Hover detection
                bool isHovered = rect.Contains(Event.current.mousePosition);
                if (Event.current.type == EventType.MouseMove) Repaint();

                using (new EditorGUI.DisabledScope(ci.locked))
                {
                    ci.selected = EditorGUILayout.Toggle(ci.selected, GUILayout.Width(18));
                }

                var style = new GUIStyle(EditorStyles.label);
                if (ci.locked) style.normal.textColor = Color.gray;
                else if (ci.isMissingScript) style.normal.textColor = Color.red;
                else if (ci.externalRefCount > 0) style.normal.textColor = Color.red;
                else if (ci.nullRefCount > 0) style.normal.textColor = new Color(1f, 0.75f, 0.2f);
                else if (ci.existsInA) style.normal.textColor = Color.gray;
                
                // Active hover color
                if (isHovered && !ci.locked && !ci.existsInA && !ci.isMissingScript)
                    style.normal.textColor = EditorGUIUtility.isProSkin ? Color.white : Color.black;

                string label = $"[{ci.category}] {ci.compTypeName} on {ci.pathInB}";
                if (ci.source == CompSource.Bone) label += " [Bone]";
                if (ci.existsInA) label += " (Exists)";
                if (ci.locked) label += " (Locked)";
                
                EditorGUILayout.LabelField(label, style);

                if (ci.isMissingScript)
                    GUILayout.Label("MISSING", GUILayout.Width(60));
                else
                    GUILayout.Label($"Ext:{ci.externalRefCount} Null:{ci.nullRefCount}", EditorStyles.miniLabel, GUILayout.Width(100));
            }
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(ci.notes))
            {
                var c = (ci.isMissingScript || ci.externalRefCount > 0) ? Color.red : new Color(1f, 0.75f, 0.2f);
                DrawColoredHelpBox(ci.notes, c);
            }
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.HelpBox(
            "RED = not portable automatically (missing script OR references still pointing outside Prefab A).\n" +
            "This tool copies component values and shows what needs manual remap.",
            MessageType.Info
        );
    }

    private void RunStep3Scan()
    {
        // Smart Reload: Store selection -> Reload -> Restore selection -> Scan
        var selectedPaths = meshItems.Where(m => m.selected).Select(m => m.pathInB).ToList();
        
        LoadPrefabs();
        
        // Restore Step 2 State
        ScanMeshesInBInternal(); 
        foreach (var m in meshItems)
        {
            if (selectedPaths.Contains(m.pathInB)) m.selected = true;
        }

        BuildComponentListFromSelectedMeshesInBInternal();
    }

    // ========================================
    // FUNCTION: BuildComponentListFromSelectedMeshesInBInternal
    // ========================================
    // PURPOSE:
    //   Generates a list of components from the source prefab based on selected meshes.
    //   Filters components by category (Collider, Physics, Magica, etc.).
    //
    // MODIFICATION WARNINGS:
    //   - Only scans objects that were selected in Step 2
    //   - Category toggles in UI directly control which components are found here
    //   - Performance scales with number of selected objects and depth of hierarchy
    // ========================================
    private void BuildComponentListFromSelectedMeshesInBInternal()
    {
        compItems.Clear();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // --- Phase 1: Scan Meshes (Step 2) ---
        // Include selected meshes OR meshes that already exist in A (duplicates)
        var selectedMeshes = meshItems.Where(m => (m.selected || m.existsInA) && (m.smrB != null || m.mrB != null)).ToList();
        
        // Log scan scope for clarity
        int manualSelect = selectedMeshes.Count(m => m.selected);
        int autoSelect = selectedMeshes.Count(m => !m.selected && m.existsInA);
        Debug.Log($"Step 3 Scanning (Meshes): {selectedMeshes.Count} objects ({manualSelect} selected, {autoSelect} auto-included).");

        var meshRoots = selectedMeshes.Select(m => m.smrB != null ? m.smrB.gameObject : m.mrB.gameObject).Distinct().ToList();
        var scannedGOs = new HashSet<GameObject>(); // Track scanned GameObjects so we don't scan them twice (e.g. if bone is also a mesh)

        foreach (var root in meshRoots)
        {
            foreach (var tr in root.GetComponentsInChildren<Transform>(true))
            {
                var go = tr.gameObject;
                scannedGOs.Add(go);
                ScanGameObject(go, CompSource.Mesh, seen);
            }
        }

        // --- Phase 2: Scan Bones (Step 1) ---
        // Scan ALL bones in B that exist in A, unless already scanned in Phase 1
        int boneCount = 0;
        foreach (var kv in mapB)
        {
            var path = kv.Key;
            var boneB = kv.Value;
            if (boneB == null) continue;
            
            // Only relevant if it exists in A (transfer target valid)
            bool existsInA = mapA.ContainsKey(path);
            if (!existsInA) continue; // Cannot transfer component if bone doesn't exist in A

            var go = boneB.gameObject;
            if (scannedGOs.Contains(go)) continue; // Already scanned as part of a Mesh hierarchy

            // Scan valid bone
            ScanGameObject(go, CompSource.Bone, seen);
            scannedGOs.Add(go);
            boneCount++;
        }
        Debug.Log($"Step 3 Scanning (Bones): Scanned {boneCount} additional bone hierarchies from Step 1 map.");
        
        // --- Phase 3: Scan Empty GameObjects (Step 2) ---
        // Scan selected Empty GameObjects from Step 2 (outside Rig Root)
        var selectedEmptyGOs = meshItems.Where(m => (m.selected || m.existsInA) && m.isEmptyGO).ToList();
        int emptyGOCount = 0;
        foreach (var emptyItem in selectedEmptyGOs)
        {
            var emptyTransform = FindByPath(loadedRootB.transform, emptyItem.pathInB);
            if (emptyTransform == null) continue;
            var go = emptyTransform.gameObject;
            if (scannedGOs.Contains(go)) continue;
            foreach (var tr in go.GetComponentsInChildren<Transform>(true))
            {
                var childGO = tr.gameObject;
                if (scannedGOs.Contains(childGO)) continue;
                ScanGameObject(childGO, CompSource.Mesh, seen);
                scannedGOs.Add(childGO);
            }
            emptyGOCount++;
        }
        Debug.Log($"Step 3 Scanning (Empty GameObjects/Phase 3): Scanned {emptyGOCount} hierarchies.");

        // --- Phase 4: Scan Root Object ---
        if (!scannedGOs.Contains(loadedRootB))
        {
            ScanGameObject(loadedRootB, CompSource.Mesh, seen);
            scannedGOs.Add(loadedRootB);
        }

        if (compItems.Count == 0)
        {
            Debug.LogWarning("Step 3 Scan: Found 0 components. This is unusual given 'Scan All' logic. Check if target objects are empty.");
        }
        else
        {
            // Count per category for debug log
            var summary = compItems.GroupBy(c => c.category).Select(g => $"{g.Key}:{g.Count()}");
            Debug.Log($"Step 3 Scan: Found {compItems.Count} components ({string.Join(", ", summary)}).");
        }

        Repaint();
    }

    private void ScanGameObject(GameObject go, CompSource source, HashSet<string> seen)
    {
        foreach (var comp in go.GetComponents<Component>())
        {
            if (comp == null)
            {
                AddMissingScriptItem(go, source, seen);
                continue;
            }
            AddComponentItem(comp, source, seen);
        }
    }

    private void AddMissingScriptItem(GameObject go, CompSource source, HashSet<string> seen)
    {
        var path = GetPathFromRoot(loadedRootB.transform, go.transform);
        var key = path + "|<MissingScript>";
        if (!seen.Add(key)) return;

        compItems.Add(new CompItem
        {
            selected = true,
            locked = false,
            category = CompCategory.Script,
            source = source, // Assign source
            pathInB = path,
            compTypeName = "(Missing Script Component)",
            compB = null,
            isMissingScript = true,
            externalRefCount = 0,
            nullRefCount = 0,
            notes = "Missing script detected. Saving Prefab A will fail if this ends up in A."
        });
    }

    private void AddComponentItem(Component comp, CompSource source, HashSet<string> seen)
    {
        if (comp == null) return;
        var path = GetPathFromRoot(loadedRootB.transform, comp.transform);
        var tn = comp.GetType().Name;
        var key = path + "|" + comp.GetType().FullName;
        
        if (!seen.Add(key)) return;

        // Classify Category & Lock Status
        CompCategory cat = CompCategory.Other;
        bool isLocked = false;
        bool isSelected = true;

        if (comp is Transform || comp is RectTransform)
        {
            cat = CompCategory.Transform;
            isLocked = true; // Safety Guard: Do not overwrite Transform
            isSelected = false;
        }
        else if (comp is MeshFilter || comp is MeshRenderer || comp is SkinnedMeshRenderer)
        {
            cat = CompCategory.Renderer;
            isLocked = true; // Safety Guard: Do not overwrite Geometry
            isSelected = false;
        }
        else if (comp is Collider || comp is CharacterController || comp is Rigidbody)
        {
            cat = CompCategory.Collider;
            isLocked = false;
            isSelected = showColliders; // Default selection state
        }
        else if (comp is Rigidbody || comp is Joint || comp is Cloth) // Physics but not generic colliders
        {
            cat = CompCategory.Physics;
            isLocked = false;
            isSelected = showPhysics;
        }
        else if (comp is AudioSource || comp is AudioReverbZone)
        {
            cat = CompCategory.Audio;
            isLocked = false;
            isSelected = showAudio;
        }
        else if (comp is Light || comp is ReflectionProbe)
        {
            cat = CompCategory.Light;
            isLocked = false;
            isSelected = showLight;
        }
        else if (comp is MonoBehaviour)
        {
            cat = CompCategory.Script;
            isLocked = false;
            isSelected = showScripts;
        }
        else
        {
            cat = CompCategory.Other;
            isLocked = false;
            isSelected = showOther;
        }

        // Check existence in A
        bool exists = false;
        var aGO = FindTargetGameObjectInA(comp.gameObject, source);
        if (aGO != null && aGO.GetComponent(comp.GetType()) != null)
        {
            exists = true;
            // IMPORTANT: We no longer auto-deselect just because it exists.
            // Selection means "I want to sync/re-copy values", visibility controls display.
        }

        compItems.Add(new CompItem
        {
            selected = isSelected,
            locked = isLocked,
            category = cat,
            source = source, // Assign source
            pathInB = path,
            compTypeName = comp.GetType().FullName,
            compB = comp,
            isMissingScript = false,
            externalRefCount = 0,
            nullRefCount = 0,
            existsInA = exists,
            notes = isLocked ? "Structural component (Locked). Safety guard to prevent overwriting Step 1/2 work." : ""
        });
    }

    private bool IsCompVisible(CompItem ci)
    {
        // 1. Search is always top priority
        if (!string.IsNullOrWhiteSpace(compSearch))
        {
            if (ci.pathInB.IndexOf(compSearch, StringComparison.OrdinalIgnoreCase) < 0 &&
                ci.compTypeName.IndexOf(compSearch, StringComparison.OrdinalIgnoreCase) < 0)
                return false;
        }

        // 2. If it's selected, it SHOULD be visible regardless of other filters (unless searching)
        if (ci.selected) return true;

        // 3. If "Only Selected" is on, and we got here, it's not selected
        if (showOnlySelectedComps) return false;

        // 4. "Only Issues" mode
        if (showOnlyIssues)
        {
            bool hasIssue = ci.isMissingScript || ci.externalRefCount > 0 || ci.nullRefCount > 0;
            if (!hasIssue) return false;
        }

        // 5. Source Filter
        if (ci.source == CompSource.Mesh && !showSourceMesh) return false;
        if (ci.source == CompSource.Bone && !showSourceBone) return false;

        // 6. Category Filter
        switch (ci.category)
        {
            case CompCategory.Transform: if (!showTransforms) return false; break;
            case CompCategory.Renderer: if (!showRenderers) return false; break;
            case CompCategory.Collider: if (!showColliders) return false; break;
            case CompCategory.Script: if (!showScripts) return false; break;
            case CompCategory.Physics: if (!showPhysics) return false; break;
            case CompCategory.Audio: if (!showAudio) return false; break;
            case CompCategory.Light: if (!showLight) return false; break;
            case CompCategory.Other: if (!showOther) return false; break;
        }

        // 7. Existence Filter
        if (hideTransferredComps && ci.existsInA) return false;

        return true;
    }

    private void SetVisibleComps(bool value)
    {
        foreach (var ci in compItems)
        {
            if (IsCompVisible(ci) && !ci.locked) // Do not modify locked items
                ci.selected = value;
        }
    }

    private void InvertVisibleComps()
    {
        foreach (var ci in compItems)
        {
            if (IsCompVisible(ci) && !ci.locked) // Do not modify locked items
                ci.selected = !ci.selected;
        }
    }

    private void CopySelectedComponentsToA()
    {
        var parentA_Meshes = FindByPath(loadedRootA.transform, targetMeshesParentPathInA);
        // Note: parentA_Meshes might be null if user hasn't copied meshes yet. This is fatal for Mesh components, but OK for Bone components.

        int copied = 0, failed = 0;

        foreach (var ci in compItems.Where(c => c.selected))
        {
            if (ci.isMissingScript || ci.compB == null)
            {
                failed++;
                continue;
            }

            // 1. Find the Source GO in B
            var bGO = FindByPath(loadedRootB.transform, ci.pathInB);
            if (bGO == null)
            {
                ci.notes = "B GameObject not found.";
                failed++;
                continue;
            }


            // 2. Find the Target GO in A
            // bGO is a Transform (returned by FindByPath), so pass .gameObject
            GameObject aGO = FindTargetGameObjectInA(bGO.gameObject, ci.source);

            if (aGO != null)
            {
                 // Add specific notes if found globally (consistency check)
                 if (ci.source != CompSource.Bone)
                 {
                     // parentA_Meshes is already defined at start of method
                     bool isUnderTargetParent = false;
                     if (parentA_Meshes != null && aGO.transform.IsChildOf(parentA_Meshes))
                        isUnderTargetParent = true;
                     
                     if (!isUnderTargetParent)
                     {
                         // Check if we found it elsewhere
                         ci.notes = $"Target found globally (not under {targetMeshesParentPathInA}).";
                     }
                 }
            }
            else
            {
                 // Not found
                 if (ci.source == CompSource.Bone)
                     ci.notes = $"Target Bone not found in A (Rig Path: '{GetPathRelativeTo(rigB, bGO.transform)}'). Step 1 needed?";
                 else
                     ci.notes = $"Target Mesh '{bGO.name}' not found in A. Copy meshes first?";
                 
                 failed++;
                 continue;
            }

            // 3. Execute Copy
            try
            {
                ComponentUtility.CopyComponent(ci.compB);

                Component pasted = aGO.GetComponent(ci.compB.GetType());
                if (pasted == null)
                    pasted = aGO.gameObject.AddComponent(ci.compB.GetType());

                ComponentUtility.PasteComponentValues(pasted);

                // Remap references from B -> A
                RemapReferences(pasted);

                ValidateComponentReferences(pasted, out int ext, out int nul);
                ci.externalRefCount = ext;
                ci.nullRefCount = nul;

                ci.notes = ext > 0
                    ? "External refs detected (still pointing outside Prefab A). Needs manual remap or custom automation."
                    : (nul > 0 ? "Some object references are null." : "");

                EditorUtility.SetDirty(pasted);
                copied++;
            }
            catch (Exception e)
            {
                ci.notes = $"Copy failed: {e.Message}";
                failed++;
            }
        }

        if (!TrySavePrefabA()) return;
        Debug.Log($"Step3: Copied {copied} components, failed {failed}. Red items need attention.");
        
        // Auto-rescan to show updated state
        BuildComponentListFromSelectedMeshesInBInternal();
    }

    private void ValidateComponentReferences(Component comp, out int externalRefCount, out int nullRefCount)
    {
        externalRefCount = 0;
        nullRefCount = 0;

        try
        {
            var so = new SerializedObject(comp);
            var prop = so.GetIterator();
            bool enterChildren = true;

            while (prop.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (prop.propertyType != SerializedPropertyType.ObjectReference) continue;

                UnityEngine.Object obj = prop.objectReferenceValue;
                if (obj == null)
                {
                    nullRefCount++;
                    continue;
                }

                // External if it’s a scene/prefab object not under loadedRootA.
                if (obj is GameObject go)
                {
                    if (!go.transform.IsChildOf(loadedRootA.transform)) externalRefCount++;
                }
                else if (obj is Component c)
                {
                    if (!c.transform.IsChildOf(loadedRootA.transform)) externalRefCount++;
                }
                // Asset refs are OK (materials, meshes, profiles).
            }
        }
        catch { }
    }

    private void RemapReferences(Component comp)
    {
        if (comp == null) return;

        var so = new SerializedObject(comp);
        var prop = so.GetIterator();
        
        // Iterate all properties, identifying references to B hierarchy
        while (prop.Next(true))
        {
            if (prop.propertyType != SerializedPropertyType.ObjectReference) continue;

            var obj = prop.objectReferenceValue;
            if (obj == null) continue;

            // 1. Reference is a GameObject in B?
            if (obj is GameObject go)
            {
                if (go.transform.IsChildOf(loadedRootB.transform))
                {
                    var path = GetPathFromRoot(loadedRootB.transform, go.transform);
                    var destNode = FindByPath(loadedRootA.transform, path);
                    if (destNode != null)
                    {
                        prop.objectReferenceValue = destNode.gameObject;
                    }
                }
            }
            // 2. Reference is a Component in B?
            else if (obj is Component c)
            {
                if (c.transform.IsChildOf(loadedRootB.transform))
                {
                    var path = GetPathFromRoot(loadedRootB.transform, c.transform);
                    var destNode = FindByPath(loadedRootA.transform, path);
                    if (destNode != null)
                    {
                        // Best-effort: Find component of same type
                        var destComp = destNode.GetComponent(c.GetType());
                        if (destComp != null)
                        {
                            prop.objectReferenceValue = destComp;
                        }
                    }
                }
            }
        }
        
        so.ApplyModifiedProperties();
    }

    private GameObject FindTargetGameObjectInA(GameObject bGO, CompSource source)
    {
        if (bGO == null) return null;

        if (source == CompSource.Bone)
        {
            string pathInRig = GetPathRelativeTo(rigB, bGO.transform);
            if (mapA.TryGetValue(pathInRig, out var tA) && tA != null)
                return tA.gameObject;
        }
        else
        {
            var parentA_Meshes = FindByPath(loadedRootA.transform, targetMeshesParentPathInA);
            if (parentA_Meshes != null)
            {
                var tFound = FindFirstChildByNameRecursive(parentA_Meshes, bGO.name);
                if (tFound != null) return tFound.gameObject;
            }
            
            var tGlobal = FindFirstChildByNameRecursive(loadedRootA.transform, bGO.name);
            if (tGlobal != null) return tGlobal.gameObject;
        }
        return null;
    }

    // -------------------------
    // Helpers
    // -------------------------
    private static Transform FindRigRoot(Transform prefabRoot, string rigRootName)
    {
        if (string.IsNullOrEmpty(rigRootName)) return prefabRoot;
        var t = prefabRoot.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == rigRootName);
        if (t == null)
        {
            Debug.LogWarning($"Rig root '{rigRootName}' not found. Using prefab root instead.");
            return prefabRoot;
        }
        return t;
    }

    private static Dictionary<string, Transform> BuildPathMap(Transform rigRoot)
    {
        var map = new Dictionary<string, Transform>(StringComparer.Ordinal) { [""] = rigRoot };
        foreach (var t in rigRoot.GetComponentsInChildren<Transform>(true))
        {
            var p = GetPathRelativeTo(rigRoot, t);
            map[p] = t;
        }
        return map;
    }

    private static string GetPathRelativeTo(Transform root, Transform t)
    {
        if (t == root) return "";
        var stack = new Stack<string>();
        var cur = t;
        while (cur != null && cur != root)
        {
            stack.Push(cur.name);
            cur = cur.parent;
        }
        return string.Join("/", stack);
    }

    private static string GetParentPath(string path)
    {
        var idx = path.LastIndexOf('/');
        return idx < 0 ? "" : path.Substring(0, idx);
    }

    private static int GetDepth(string path) =>
        string.IsNullOrEmpty(path) ? 0 : path.Count(c => c == '/') + 1;

    private static Transform FindClosestExistingParent(Dictionary<string, Transform> map, string parentPath)
    {
        var cur = parentPath;
        while (true)
        {
            if (map.TryGetValue(cur, out var t) && t != null) return t;
            if (string.IsNullOrEmpty(cur)) break;
            cur = GetParentPath(cur);
        }
        return map[""];
    }

    private static bool FindByName(Dictionary<string, Transform> map, string name)
    {
        foreach (var kv in map)
            if (kv.Value != null && kv.Value.name == name)
                return true;
        return false;
    }

    private static Dictionary<string, Transform> BuildNameMap(Transform rigRoot)
    {
        var dict = new Dictionary<string, Transform>(StringComparer.Ordinal);
        foreach (var t in rigRoot.GetComponentsInChildren<Transform>(true))
            if (!dict.ContainsKey(t.name))
                dict[t.name] = t;
        return dict;
    }

    private static Transform FindByPath(Transform root, string path)
    {
        if (string.IsNullOrEmpty(path)) return root;
        var parts = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        var cur = root;
        foreach (var part in parts)
        {
            var found = cur.Find(part);
            if (found == null) return null;
            cur = found;
        }
        return cur;
    }

    private static string GetPathFromRoot(Transform root, Transform t)
    {
        if (t == root) return "";
        var stack = new Stack<string>();
        var cur = t;
        while (cur != null && cur != root)
        {
            stack.Push(cur.name);
            cur = cur.parent;
        }
        return string.Join("/", stack);
    }

    private static Transform EnsurePathUnderRoot(Transform root, string path)
    {
        if (string.IsNullOrEmpty(path)) return root;
        var parts = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        var cur = root;
        foreach (var part in parts)
        {
            var child = cur.Find(part);
            if (child == null)
            {
                var go = new GameObject(part);
                child = go.transform;
                child.SetParent(cur, false);
            }
            cur = child;
        }
        return cur;
    }

    private static Transform FindFirstChildByNameRecursive(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    private static List<string> FindMissingScripts(GameObject root)
    {
        var results = new List<string>();
        if (root == null) return results;

        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            var comps = t.GetComponents<Component>();
            foreach (var c in comps)
            {
                if (c == null)
                {
                    results.Add(GetPathFromRoot(root.transform, t) + " (missing script)");
                    break;
                }
            }
        }
        return results;
    }

    private bool DrawStyledButton(string label, Color color, float height, float width = -1)
    {
        var prevColor = GUI.backgroundColor;
        GUI.backgroundColor = color;
        
        bool clicked;
        if (width > 0)
            clicked = GUILayout.Button(label, GUILayout.Height(height), GUILayout.Width(width));
        else
            clicked = GUILayout.Button(label, GUILayout.Height(height));
            
        GUI.backgroundColor = prevColor;
        return clicked;
    }

    private void DrawColoredHelpBox(string message, Color color)
    {
        var prevColor = GUI.backgroundColor;
        GUI.backgroundColor = color;
        EditorGUILayout.HelpBox(message, MessageType.None);
        GUI.backgroundColor = prevColor;
    }
    }
}
