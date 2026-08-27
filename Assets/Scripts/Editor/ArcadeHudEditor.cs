#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>Builds and persists the Fight HUD hierarchy in edit mode.</summary>
public static class ArcadeHudEditor
{
    private const string FightSceneName = "Fight";

    [MenuItem("ShootingGame/UI/Generate Arcade Fight HUD", false, 110)]
    public static void GenerateArcadeFightHud()
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.name != FightSceneName)
        {
            EditorUtility.DisplayDialog("Arcade HUD", "Open the Fight scene before generating its HUD.", "OK");
            return;
        }

        var battleUi = Object.FindFirstObjectByType<BattleUI>(FindObjectsInactive.Include);
        if (battleUi == null)
        {
            var go = new GameObject("BattleUI");
            Undo.RegisterCreatedObjectUndo(go, "Create BattleUI");
            battleUi = go.AddComponent<BattleUI>();
        }

        battleUi.GenerateDefaultUIInEditor();
        var canvas = battleUi.GetComponentInChildren<Canvas>(true);
        if (canvas == null)
        {
            EditorUtility.DisplayDialog("Arcade HUD", "The default HUD canvas could not be created.", "OK");
            return;
        }

        var layout = ReferenceHudLayout.Ensure(canvas.transform);
        Undo.RecordObject(canvas.gameObject, "Configure Arcade HUD Canvas");
        var scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler != null)
        {
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;
            EditorUtility.SetDirty(scaler);
        }

        var visuals = canvas.GetComponent<ArcadeHudVisuals>();
        if (visuals == null)
        {
            visuals = Undo.AddComponent<ArcadeHudVisuals>(canvas.gameObject);
        }
        visuals.RebuildInEditor();

        var abilityBar = battleUi.GetComponent<AbilityBar>();
        if (abilityBar != null)
            abilityBar.GenerateInEditor();

        EditorUtility.SetDirty(battleUi);
        EditorUtility.SetDirty(visuals);
        if (layout != null)
            EditorUtility.SetDirty(layout);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = canvas.gameObject;
        Debug.Log("[ArcadeHudEditor] Fight HUD generated and saved in the active scene.");
    }
}
#endif
