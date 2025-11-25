using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using Animancer; // ✅ 引入 Animancer 命名空间

[CustomEditor(typeof(PlayerAnimationSet))]
public class PlayerAnimationSetEditor : Editor
{
    private ReorderableList list;
    private SerializedProperty animationsProp;

    private void OnEnable()
    {
        animationsProp = serializedObject.FindProperty("animations");

        list = new ReorderableList(serializedObject, animationsProp, true, true, true, true);

        // 标题
        list.drawHeaderCallback = rect => {
            EditorGUI.LabelField(rect, "动画列表");
        };

        // 每行绘制
        list.drawElementCallback = (rect, index, isActive, isFocused) =>
        {
            var element = animationsProp.GetArrayElementAtIndex(index);
            var nameProp = element.FindPropertyRelative("name");
            var clipProp = element.FindPropertyRelative("clip");

            float half = rect.width / 2f - 5f;
            rect.y += 2;

            EditorGUI.PropertyField(
                new Rect(rect.x, rect.y, half, EditorGUIUtility.singleLineHeight),
                nameProp, GUIContent.none);

            EditorGUI.PropertyField(
                new Rect(rect.x + half + 10f, rect.y, half, EditorGUIUtility.singleLineHeight),
                clipProp, GUIContent.none);
        };

        // 添加按钮逻辑
        list.onAddCallback = l =>
        {
            animationsProp.arraySize++;
            var element = animationsProp.GetArrayElementAtIndex(animationsProp.arraySize - 1);
            element.FindPropertyRelative("name").stringValue = "NewAnimation";
            element.FindPropertyRelative("clip").objectReferenceValue = null;
        };

        // 删除按钮确认
        list.onRemoveCallback = l =>
        {
            if (EditorUtility.DisplayDialog("删除确认", "确定要删除该动画？", "删除", "取消"))
                ReorderableList.defaultBehaviours.DoRemoveButton(l);
        };
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        list.DoLayoutList();
        serializedObject.ApplyModifiedProperties();

        GUILayout.Space(10);

        if (GUILayout.Button("📁 从文件夹导入所有动画"))
        {
            string folderPath = EditorUtility.OpenFolderPanel("选择动画文件夹", "Assets", "");
            if (!string.IsNullOrEmpty(folderPath))
            {
                ImportAnimationsFromFolder((PlayerAnimationSet)target, folderPath);
            }
        }

        GUILayout.Space(5);

        if (GUILayout.Button("🧩 生成 PlayerAnimType 枚举"))
        {
            GenerateEnumFromAnimations((PlayerAnimationSet)target);
        }
    }

    public static void ImportAnimationsFromFolder(PlayerAnimationSet set, string folderPath)
    {
        string projectPath = Application.dataPath;
        if (!folderPath.StartsWith(projectPath))
        {
            Debug.LogError("⚠️ 必须选择 Assets 下的文件夹！");
            return;
        }

        string relativePath = "Assets" + folderPath.Substring(projectPath.Length);
        string[] guids = AssetDatabase.FindAssets("t:AnimationClip", new[] { relativePath });

        int countBefore = set.animations.Count;

        foreach (string guid in guids)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(guid));
            if (clip == null) continue;

            string safeName = MakeSafeEnumName(clip.name);

            // ✅ 新版 Animancer 的写法：先 new 出对象再赋值
            if (!set.animations.Any(a => a.name == safeName))
            {
                var transition = new ClipTransition
                {
                    Clip = clip,
                    FadeDuration = 0.25f
                };

                set.animations.Add(new PlayerAnimationSet.AnimationEntry
                {
                    name = safeName,
                    clip = transition
                });
            }
        }

        EditorUtility.SetDirty(set);
        AssetDatabase.SaveAssets();

        Debug.Log($"✅ 导入完成：新增 {set.animations.Count - countBefore} 个动画。");
        GenerateEnumFromAnimations(set);
    }


    // ===============================================================
    // 自动生成枚举逻辑（ClipTransition => clip.Clip）
    // ===============================================================
    public static void GenerateEnumFromAnimations(PlayerAnimationSet set)
    {
        string enumName = "PlayerAnimType";
        string savePath = "Assets/Scripts/Generated/" + enumName + ".cs";
        Directory.CreateDirectory(Path.GetDirectoryName(savePath));

        // ✅ 取 ClipTransition 内部 AnimationClip 的名字
        List<string> validNames = set.animations
            .Where(a => a.clip != null && a.clip.Clip != null)
            .Select(a => MakeSafeEnumName(a.clip.Clip.name))
            .Distinct()
            .ToList();

        if (validNames.Count == 0)
        {
            Debug.LogWarning("⚠️ 没有动画可生成枚举。");
            return;
        }

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("// 自动生成文件，请勿手动修改");
        sb.AppendLine("public enum " + enumName);
        sb.AppendLine("{");

        foreach (var name in validNames)
            sb.AppendLine($"    {name},");

        sb.AppendLine("}");

        File.WriteAllText(savePath, sb.ToString(), Encoding.UTF8);
        AssetDatabase.Refresh();
        Debug.Log($"🧱 枚举已生成: {savePath}");
    }

    // ===============================================================
    // 名字合法化
    // ===============================================================
    public static string MakeSafeEnumName(string name)
    {
        string result = new string(name.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
        if (string.IsNullOrEmpty(result))
            result = "Unnamed";
        if (char.IsDigit(result.FirstOrDefault()))
            result = "_" + result;
        return char.ToUpper(result[0]) + result.Substring(1);
    }
}
