// ========================================
// BiperworksMissingChecker.cs
// ========================================

// PURPOSE:
//   Editor tool for comparing two prefabs and detecting missing/different properties.
//   Helps identify broken references and value discrepancies between source and target prefabs.
//
// WORKFLOW:
//   Two-tab system:
//   1. Missing Check Tab: Finds broken references and unassigned properties
//   2. Value Compare Tab: Compares property values with type filtering
//
// USAGE:
//   1. Assign Prefab A (target) and Prefab B (source)
//   2. Optionally configure Rig Roots and Output Path for path resolution
//   3. Optionally link Scene instance for click-to-select functionality
//   4. Choose tab and configure options
//   5. Click scan button to generate issue list
//
// PATH RESOLUTION STRATEGY:
//   The tool uses a 3-tier fallback system to find matching objects:
//   1. Rig Root Path (highest priority): If object is under rig root in B, look in rig root in A
//   2. Output Path: If configured, prepend output path to relative path
//   3. Global Path (fallback): Use relative path from prefab root
//
// DEPENDENCIES:
//   - Unity PrefabUtility (for loading prefab contents)
//   - Unity SerializedObject/SerializedProperty (for property inspection)
//   - AssetDatabase (for asset path resolution)
//
// MODIFICATION WARNINGS:
//   - Path resolution order is critical - changing it can cause wrong object matches
//   - SerializedProperty iteration is fragile - must use NextVisible(true) correctly
//   - Filter logic inversion will break the UI (no filters = show all)
//
// ========================================

using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

namespace Biperworks.Editor
{
    public class BiperworksMissingChecker : EditorWindow
    {
        // Enums
        private enum Tab { MissingCheck, ValueCompare }
        
        // Inputs
        private GameObject prefabA;
        private GameObject prefabB;
        private string targetParentPathInA = "";
        
        // Rig Roots
        private string rigRootNameA = "";
        private string rigRootNameB = "";

        // Linking
        private GameObject instanceRootA;

        // Search
        private string searchString = "";

        // Tabs
        private Tab currentTab = Tab.MissingCheck;
        private Texture2D bannerLogo;

        // Loaded Contents
        private GameObject loadedRootA;
        private GameObject loadedRootB;
        private Transform loadedRigRootA;
        private Transform loadedRigRootB;

        // UI State
        private Vector2 scrollPosition;
        private List<IssueItem> issues = new List<IssueItem>();
        private bool includeUnassigned = true; // For Missing Check

        // Property Type Filters (Value Check)
        [SerializeField] private bool showInteger = false;
        [SerializeField] private bool showFloat = false;
        [SerializeField] private bool showBoolean = false;
        [SerializeField] private bool showString = false;
        [SerializeField] private bool showColor = false;
        [SerializeField] private bool showEnum = false;
        [SerializeField] private bool showObjectReference = false;
        [SerializeField] private bool showOther = false;

        private struct IssueItem
        {
            public string path;
            public string componentName;
            public string propertyName;
            public string message;
            public MessageType type;
        }

        [MenuItem("Tools/Biperworks Tool/Biperworks-Missing Checker")]
        public static void ShowWindow()
        {
            GetWindow<BiperworksMissingChecker>("Biperworks-Missing Checker");
        }

        private void OnGUI()
        {
            DrawUIBanner("Biperworks-Missing Checker", "Developed by Biperworks");

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                // --- Shared Header ---
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
                    targetParentPathInA = EditorGUILayout.TextField(targetParentPathInA);
                }

                EditorGUILayout.Space(2);

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Scene Link", EditorStyles.boldLabel, GUILayout.Width(100));
                    instanceRootA = (GameObject)EditorGUILayout.ObjectField(instanceRootA, typeof(GameObject), true);
                }
            }

            EditorGUILayout.Space(5);
            
            // --- Tab Selection ---
            currentTab = (Tab)GUILayout.Toolbar((int)currentTab, new string[] { "Missing Check", "Value Check" }, GUILayout.Height(25));
            EditorGUILayout.Space(5);

            // --- Tab Content ---
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (currentTab == Tab.MissingCheck)
                {
                    DrawMissingCheckOptions();
                }
                else
                {
                    DrawValueCompareOptions();
                }
            }

            EditorGUILayout.Space(5);

            // --- Action & Results ---
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(prefabA == null || prefabB == null))
                {
                    string btnLabel = currentTab == Tab.MissingCheck ? "Scan Missing Components & Props" : "Compare Property Values";
                    if (DrawStyledButton(btnLabel, Color.white, 30))
                    {
                        RunScan();
                    }
                }
            }

            // Search
            EditorGUILayout.Space(5);
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                EditorGUILayout.LabelField($"Issues: {issues.Count}", EditorStyles.miniBoldLabel, GUILayout.Width(100));
                searchString = EditorGUILayout.TextField(searchString, EditorStyles.toolbarSearchField);
                if (DrawStyledButton("Clear", new Color(0.4f, 0.4f, 0.4f, 1f), 18, 50))
                {
                    searchString = "";
                    GUI.FocusControl(null);
                }
            }
            
            DrawIssueList();
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

            // Draw logo on the right with subtle transparency
            if (bannerLogo != null)
            {
                float aspect = (float)bannerLogo.width / bannerLogo.height;
                float imageAreaHeight = rect.height; // Exactly banner height (40px)
                float imageAreaWidth = imageAreaHeight * aspect;
                
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

        private void DrawMissingCheckOptions()
        {
            includeUnassigned = EditorGUILayout.ToggleLeft("Report 'None' in A (if B has value)", includeUnassigned);
        }

        private void DrawValueCompareOptions()
        {
            EditorGUILayout.LabelField("Property Type Filters", EditorStyles.boldLabel);
            
            using (new EditorGUILayout.HorizontalScope())
            {
                showInteger = EditorGUILayout.ToggleLeft("Int", showInteger, GUILayout.Width(45));
                showFloat = EditorGUILayout.ToggleLeft("Flt", showFloat, GUILayout.Width(45));
                showBoolean = EditorGUILayout.ToggleLeft("Bool", showBoolean, GUILayout.Width(50));
                showString = EditorGUILayout.ToggleLeft("Str", showString, GUILayout.Width(45));
            }
            
            using (new EditorGUILayout.HorizontalScope())
            {
                showColor = EditorGUILayout.ToggleLeft("Col", showColor, GUILayout.Width(45));
                showEnum = EditorGUILayout.ToggleLeft("Enum", showEnum, GUILayout.Width(55));
                showObjectReference = EditorGUILayout.ToggleLeft("ObjRef", showObjectReference, GUILayout.Width(65));
                showOther = EditorGUILayout.ToggleLeft("Oth", showOther, GUILayout.Width(45));
            }
        }

        private void RunScan()
        {
            Debug.Log($"RunScan Started: {currentTab}");
            if (!LoadPrefabs()) 
            {
                Debug.LogError("Failed to load prefabs. Make sure A and B are assigned and are Reference Prefabs.");
                return;
            }

            // Find Rig Roots
            loadedRigRootA = FindRigRoot(loadedRootA.transform, rigRootNameA);
            loadedRigRootB = FindRigRoot(loadedRootB.transform, rigRootNameB);

            issues.Clear();
            
            var allTransformsB = loadedRootB.GetComponentsInChildren<Transform>(true);
            int total = allTransformsB.Length;
            int current = 0;

            try
            {
                foreach (var tB in allTransformsB)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("Scanning...", tB.name, (float)current / total))
                        break;

                    CheckObject(tB.gameObject);
                    current++;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                UnloadPrefabs(); 
                Debug.Log($"Scan Complete. Issues found: {issues.Count}");
            }
        }

        private void CheckObject(GameObject goB)
        {
            GameObject goA = FindTargetInA(goB);

            if (goA == null) return;

            Component[] compsB = goB.GetComponents<Component>();
            foreach (var cB in compsB)
            {
                if (cB == null) continue;
                if (!(cB is MonoBehaviour)) continue; 

                var cA = goA.GetComponent(cB.GetType());

                if (currentTab == Tab.MissingCheck)
                {
                    if (cA == null)
                    {
                        issues.Add(new IssueItem
                        {
                            path = GetPathFromRoot(loadedRootA.transform, goA.transform),
                            componentName = cB.GetType().Name,
                            propertyName = "(Entire Component)",
                            message = "Component missing in A",
                            type = MessageType.Error
                        });
                    }
                    else
                    {
                        CheckMissingProperties(cB, cA, goA);
                    }
                }
                else // Tab.ValueCompare
                {
                    if (cA != null)
                    {
                        CompareValues(cB, cA, goA);
                    }
                }
            }
        }

        // ========================================
        // FUNCTION: CheckMissingProperties
        // ========================================
        // PURPOSE:
        //   Iterates through all ObjectReference properties in component B
        //   and checks if they are missing or unassigned in component A.
        //
        // MODIFICATION WARNINGS:
        //   - Only checks ObjectReference properties - other types are ignored
        //   - Must call NextVisible(true) to iterate properly - false will skip nested properties
        //   - Modifying iteration logic can skip properties or cause infinite loops
        //   - objectReferenceInstanceIDValue != 0 detects broken refs (instanceID exists but value is null)
        // ========================================
        private void CheckMissingProperties(Component cB, Component cA, GameObject goA)
        {
            SerializedObject soB = new SerializedObject(cB);
            SerializedObject soA = new SerializedObject(cA);

            SerializedProperty iterB = soB.GetIterator();
            while (iterB.NextVisible(true))
            {
                if (iterB.propertyType != SerializedPropertyType.ObjectReference) continue;

                SerializedProperty propA = soA.FindProperty(iterB.propertyPath);
                if (propA == null) continue;

                if (propA.objectReferenceValue == null && propA.objectReferenceInstanceIDValue != 0)
                {
                    issues.Add(new IssueItem
                    {
                        path = GetPathFromRoot(loadedRootA.transform, goA.transform),
                        componentName = cA.GetType().Name,
                        propertyName = propA.displayName,
                        message = "Broken Reference in A (Missing)",
                        type = MessageType.Error
                    });
                    continue;
                }

                if (includeUnassigned)
                {
                    bool bHasValue = iterB.objectReferenceValue != null;
                    bool aIsNone = propA.objectReferenceValue == null && propA.objectReferenceInstanceIDValue == 0;

                    if (bHasValue && aIsNone)
                    {
                        issues.Add(new IssueItem
                        {
                            path = GetPathFromRoot(loadedRootA.transform, goA.transform),
                            componentName = cA.GetType().Name,
                            propertyName = propA.displayName,
                            message = $"Unassigned in A (B has '{iterB.objectReferenceValue.name}')",
                            type = MessageType.Warning
                        });
                    }
                }
            }
        }

        // ========================================
        // FUNCTION: CompareValues
        // ========================================
        // PURPOSE:
        //   Compares all property values between component B and A,
        //   respecting the property type filters set by the user.
        //
        // MODIFICATION WARNINGS:
        //   - DataEquals is strict - may report false positives for floating point values
        //   - Filter logic must be checked BEFORE comparison to avoid spam
        //   - Skips m_Script field to avoid false positives
        //   - GetValueString fallback is needed because DataEquals can be overly strict
        // ========================================
        private void CompareValues(Component cB, Component cA, GameObject goA)
        {
            SerializedObject soB = new SerializedObject(cB);
            SerializedObject soA = new SerializedObject(cA);
            SerializedProperty iterB = soB.GetIterator();

            while (iterB.NextVisible(true))
            {
                // Skip script field
                if (iterB.name == "m_Script") continue;
                
                // Skip filtered property types
                if (ShouldSkipPropertyType(iterB)) continue;

                SerializedProperty propA = soA.FindProperty(iterB.propertyPath);
                if (propA == null) continue;

                // Compare Values
                if (!SerializedProperty.DataEquals(iterB, propA))
                {
                    string valB = GetValueString(iterB);
                    string valA = GetValueString(propA);

                    // Ignore if really same (DataEquals is sometimes strict)
                    if (valB != valA)
                    {
                        issues.Add(new IssueItem
                        {
                            path = GetPathFromRoot(loadedRootA.transform, goA.transform),
                            componentName = cA.GetType().Name,
                            propertyName = propA.displayName,
                            message = $"Diff: B[{valB}] vs A[{valA}]",
                            type = MessageType.Warning // Light warning for diff
                        });
                    }
                }
            }
        }

        // ========================================
        // FUNCTION: ShouldSkipPropertyType
        // ========================================
        // PURPOSE:
        //   Determines if a property should be skipped based on type filters.
        //   Implements "no filters = show all" logic.
        //
        // MODIFICATION WARNINGS:
        //   - "No filters enabled" means show ALL - inverting this breaks the UI
        //   - Adding new property types requires updating both filter UI and this function
        //   - Return value is inverted (true = skip, false = show)
        // ========================================
        private bool ShouldSkipPropertyType(SerializedProperty prop)
        {
            // If no filters are enabled, show all
            bool anyFilterEnabled = showInteger || showFloat || showBoolean || showString ||
                                   showColor || showEnum || showObjectReference || showOther;
            
            if (!anyFilterEnabled)
                return false; // Show all when no filters active
            
            // If filters are enabled, only show types that are checked
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer:
                    return !showInteger;
                case SerializedPropertyType.Float:
                    return !showFloat;
                case SerializedPropertyType.Boolean:
                    return !showBoolean;
                case SerializedPropertyType.String:
                    return !showString;
                case SerializedPropertyType.Color:
                    return !showColor;
                case SerializedPropertyType.Enum:
                    return !showEnum;
                case SerializedPropertyType.ObjectReference:
                    return !showObjectReference;
                default:
                    return !showOther;
            }
        }

        private string GetValueString(SerializedProperty prop)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer: return prop.intValue.ToString();
                case SerializedPropertyType.Boolean: return prop.boolValue.ToString();
                case SerializedPropertyType.Float: return prop.floatValue.ToString("F3");
                case SerializedPropertyType.String: return prop.stringValue;
                case SerializedPropertyType.Color: return prop.colorValue.ToString();
                case SerializedPropertyType.Enum: return prop.enumNames[prop.enumValueIndex];
                case SerializedPropertyType.ObjectReference:
                    return prop.objectReferenceValue != null ? prop.objectReferenceValue.name : "None";
                default:
                    return "(Complex/Other)";
            }
        }

        // --- Helpers (Same as before) ---

        // ========================================
        // FUNCTION: FindTargetInA
        // ========================================
        // PURPOSE:
        //   Finds the corresponding GameObject in Prefab A for a given GameObject in Prefab B.
        //   Uses a 3-tier fallback strategy for path resolution.
        //
        // ALGORITHM:
        //   1. Rig Root Path (Priority 1): If goB is under rig root B, look in rig root A
        //   2. Output Path (Priority 2): Prepend targetParentPathInA to relative path
        //   3. Global Path (Priority 3): Use relative path from prefab root
        //
        // MODIFICATION WARNINGS:
        //   - Path resolution has 3 fallback strategies - ORDER MATTERS
        //   - Rig root path takes priority over output path
        //   - Changing resolution order can cause wrong objects to be matched
        //   - Name verification (found.gameObject.name == goB.name) prevents false matches
        // ========================================
        private GameObject FindTargetInA(GameObject goB)
        {
            if (loadedRigRootA != null && loadedRigRootB != null)
            {
                if (IsChildOf(goB.transform, loadedRigRootB))
                {
                    string pathInRig = GetPathFromRoot(loadedRigRootB, goB.transform);
                    if (string.IsNullOrEmpty(pathInRig)) return loadedRigRootA.gameObject; 
                    var foundInRig = loadedRigRootA.Find(pathInRig);
                    if (foundInRig != null) return foundInRig.gameObject;
                }
            }

            string relativePath = GetPathFromRoot(loadedRootB.transform, goB.transform);

            if (!string.IsNullOrEmpty(targetParentPathInA))
            {
                string cleanTargetParent = targetParentPathInA.Trim('/');
                string targetPath = cleanTargetParent + "/" + relativePath;
                Transform found = FindByPath(loadedRootA.transform, targetPath);
                if (found != null && found.gameObject.name == goB.name) return found.gameObject;
            }

            Transform globalFound = FindByPath(loadedRootA.transform, relativePath);
            if (globalFound != null) return globalFound.gameObject;

            return null;
        }

        private bool IsChildOf(Transform t, Transform parent)
        {
            if (t == parent) return true;
            if (t.parent == null) return false;
            return t.IsChildOf(parent);
        }

        private static Transform FindRigRoot(Transform prefabRoot, string rigRootName)
        {
            if (string.IsNullOrEmpty(rigRootName)) return null;
            if (prefabRoot.name == rigRootName) return prefabRoot;
            return prefabRoot.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == rigRootName);
        }

        private bool LoadPrefabs()
        {
            UnloadPrefabs();

            if (prefabA == null || prefabB == null) return false;

            string pathA = AssetDatabase.GetAssetPath(prefabA);
            string pathB = AssetDatabase.GetAssetPath(prefabB);

            if (string.IsNullOrEmpty(pathA) || string.IsNullOrEmpty(pathB)) return false;

            loadedRootA = PrefabUtility.LoadPrefabContents(pathA);
            loadedRootB = PrefabUtility.LoadPrefabContents(pathB);

            return true;
        }

        private void UnloadPrefabs()
        {
            if (loadedRootA != null) PrefabUtility.UnloadPrefabContents(loadedRootA);
            if (loadedRootB != null) PrefabUtility.UnloadPrefabContents(loadedRootB);
            loadedRootA = null;
            loadedRootB = null;
        }

        private string GetPathFromRoot(Transform root, Transform target)
        {
            if (root == target) return "";
            if (target == null) return "";
            
            string path = target.name;
            Transform curr = target.parent;
            while (curr != null && curr != root)
            {
                path = curr.name + "/" + path;
                curr = curr.parent;
            }
            return path;
        }

        private Transform FindByPath(Transform root, string path)
        {
            if (string.IsNullOrEmpty(path)) return root;
            return root.Find(path);
        }

        private void DrawIssueList()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.ExpandHeight(true));
            
            var filteredIssues = issues;
            if (!string.IsNullOrEmpty(searchString))
            {
                string lowerSearch = searchString.ToLower();
                filteredIssues = issues.Where(i => 
                    i.componentName.ToLower().Contains(lowerSearch) ||
                    i.propertyName.ToLower().Contains(lowerSearch) ||
                    i.message.ToLower().Contains(lowerSearch) ||
                    i.path.ToLower().Contains(lowerSearch)
                ).ToList();
            }

            if (filteredIssues.Count == 0 && issues.Count > 0)
            {
                EditorGUILayout.HelpBox("No issues match the search criteria.", MessageType.Info);
            }
            else if (issues.Count == 0 && prefabA != null)
            {
                EditorGUILayout.HelpBox("Clean! No issues found.", MessageType.Info);
            }

            foreach (var issue in filteredIssues)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    var rect = EditorGUILayout.BeginHorizontal();
                    
                    var iconName = issue.type == MessageType.Error ? "console.erroricon.sml" : "console.warnicon.sml";
                    GUILayout.Label(EditorGUIUtility.IconContent(iconName), GUILayout.Width(20), GUILayout.Height(20));

                    using (new EditorGUILayout.VerticalScope())
                    {
                        // Instant Hover Logic: Check if mouse is inside the horizontal group
                        bool isHovered = rect.Contains(Event.current.mousePosition);
                        
                        // Force immediate color change without transition
                        Color pathColor = isHovered 
                            ? (EditorGUIUtility.isProSkin ? Color.white : Color.black) 
                            : new Color(0.7f, 0.7f, 0.7f);

                        var pathStyle = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = pathColor } };
                        GUILayout.Label($"{issue.path}", pathStyle);
                        GUILayout.Label($"{issue.componentName} > {issue.propertyName}", EditorStyles.boldLabel);
                        GUILayout.Label(issue.message);
                    }

                    EditorGUILayout.EndHorizontal();
                    
                    // Request repaint on mouse move to ensure instant hover visibility
                    if (Event.current.type == EventType.MouseMove) Repaint();

                    if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
                    {
                        bool selected = false;
                        if (instanceRootA != null)
                        {
                            Transform found = string.IsNullOrEmpty(issue.path) ? instanceRootA.transform : instanceRootA.transform.Find(issue.path);
                            if (found != null)
                            {
                                Selection.activeGameObject = found.gameObject;
                                EditorGUIUtility.PingObject(found.gameObject);
                                selected = true;
                            }
                        }

                        if (!selected)
                        {
                            EditorGUIUtility.systemCopyBuffer = issue.path;
                            Debug.Log($"Path copied to clipboard: {issue.path}");
                        }
                        Event.current.Use();
                    }
                }
            }
            EditorGUILayout.EndScrollView();
            
            if (issues.Count > 0)
            {
                string hint = instanceRootA == null ? "Click to copy path." : "Click to select in Scene.";
                EditorGUILayout.LabelField(hint, EditorStyles.centeredGreyMiniLabel);
            }
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
    }
}
