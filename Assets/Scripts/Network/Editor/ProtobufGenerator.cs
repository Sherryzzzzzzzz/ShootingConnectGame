// ============================================================
// ProtobufGenerator.cs — 从 .proto 生成 C#（参考 SpaceBuilder）
//
// 触发方式：Unity Editor 菜单 Tools → ShootingGame → Update Protobuf
//
// 工作原理：
//   1. 读取 ProtobufConfig ScriptableObject
//   2. 遍历所有 .proto 源目录
//   3. 对每个 .proto 文件调用 protoc.exe 生成 C#
//   4. 刷新 AssetDatabase
//
// 要求：
//   - 安装 Google.Protobuf NuGet（Google.Protobuf.dll 用于运行时）
//   - protoc.exe 在系统 PATH 或配置路径中
// ============================================================

#if UNITY_EDITOR
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace ShootingGame.Editor
{
    public static class ProtobufGenerator
    {
        private const string ConfigSearchFilter = "t:ProtobufConfig";

        [MenuItem("Tools/ShootingGame/Update Protobuf", priority = 100)]
        public static void GenerateProtobuf()
        {
            // 查找 ProtobufConfig
            var guids = AssetDatabase.FindAssets(ConfigSearchFilter);
            if (guids.Length == 0)
            {
                if (!EditorUtility.DisplayDialog(
                    "未找到 ProtobufConfig",
                    "需要先创建 ProtobufConfig 配置文件。\n\n是否现在创建？",
                    "创建", "取消"))
                    return;

                CreateDefaultConfig();
                return;
            }

            var configPath = AssetDatabase.GUIDToAssetPath(guids[0]);
            var config = AssetDatabase.LoadAssetAtPath<ProtobufConfig>(configPath);
            if (config == null)
            {
                EditorUtility.DisplayDialog("错误", "无法加载 ProtobufConfig", "OK");
                return;
            }

            GenerateFromConfig(config);
        }

        private static void CreateDefaultConfig()
        {
            // 确保目录存在
            var dir = "Assets/Scripts/Network/Editor";
            if (!AssetDatabase.IsValidFolder(dir))
            {
                var parent = Path.GetDirectoryName(dir);
                var folder = Path.GetFileName(dir);
                AssetDatabase.CreateFolder(parent, folder);
            }

            var config = ScriptableObject.CreateInstance<ProtobufConfig>();
            AssetDatabase.CreateAsset(config, $"{dir}/ProtobufConfig.asset");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog(
                "已创建 ProtobufConfig",
                $"已在 {dir}/ProtobufConfig.asset 创建配置文件。\n\n请设置 protoc.exe 路径后重新执行 Tools → ShootingGame → Update Protobuf。",
                "OK");
        }

        private static void GenerateFromConfig(ProtobufConfig config)
        {
            var protocPath = ResolveProtocPath(config.ProtocPath);
            if (string.IsNullOrEmpty(protocPath) || !File.Exists(protocPath))
            {
                EditorUtility.DisplayDialog(
                    "protoc 未找到",
                    "请设置正确的 protoc.exe 路径。\n\n" +
                    "可以从 https://github.com/protocolbuffers/protobuf/releases 下载。\n" +
                    "或使用包管理器: choco install protoc",
                    "OK");
                return;
            }

            var projectRoot = Application.dataPath.Replace("/Assets", "").Replace("\\Assets", "");
            int successCount = 0;
            int errorCount = 0;

            for (int i = 0; i < config.ProtoSourceDirs.Count; i++)
            {
                var protoDirRel = config.ProtoSourceDirs[i];
                var protoDir = Path.GetFullPath(Path.Combine(projectRoot, protoDirRel));

                var outputDirRel = i < config.GeneratedOutputDirs.Count
                    ? config.GeneratedOutputDirs[i]
                    : protoDirRel + "/../Generated";
                var outputDir = Path.GetFullPath(Path.Combine(projectRoot, outputDirRel));

                if (!Directory.Exists(protoDir))
                {
                    Debug.LogWarning($"[ProtobufGenerator] Proto 目录不存在: {protoDir}");
                    continue;
                }

                if (!Directory.Exists(outputDir))
                    Directory.CreateDirectory(outputDir);

                var protoFiles = Directory.GetFiles(protoDir, "*.proto", SearchOption.AllDirectories);

                if (protoFiles.Length == 0)
                {
                    Debug.LogWarning($"[ProtobufGenerator] {protoDir} 中没有 .proto 文件");
                    continue;
                }

                foreach (var protoFile in protoFiles)
                {
                    if (RunProtoc(protocPath, protoFile, protoDir, outputDir))
                        successCount++;
                    else
                        errorCount++;
                }
            }

            AssetDatabase.Refresh();

            var msg = $"已处理 {successCount} 个 .proto 文件。";
            if (errorCount > 0)
                msg += $"\n{errorCount} 个文件生成失败，请查看 Console。";

            EditorUtility.DisplayDialog("Protobuf 生成完成", msg, "OK");
        }

        private static string ResolveProtocPath(string configuredPath)
        {
            // 1. 使用配置路径
            if (!string.IsNullOrEmpty(configuredPath) && File.Exists(configuredPath))
                return configuredPath;

            // 2. 尝试 PATH 环境变量
            var pathExt = System.Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT";
            var extensions = pathExt.Split(';');

            foreach (var dir in (System.Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            {
                foreach (var ext in extensions)
                {
                    var fullPath = Path.Combine(dir.Trim(), "protoc" + ext.ToLower());
                    if (File.Exists(fullPath))
                        return fullPath;
                }
            }

            // 3. 常见安装路径
            var commonPaths = new[]
            {
                @"C:\Program Files\protoc\bin\protoc.exe",
                @"C:\protoc\bin\protoc.exe",
                "/usr/local/bin/protoc",
                "/usr/bin/protoc"
            };

            foreach (var path in commonPaths)
            {
                if (File.Exists(path))
                    return path;
            }

            return null;
        }

        private static bool RunProtoc(string protocPath, string protoFile, string protoDir, string outputDir)
        {
            var fileName = Path.GetFileName(protoFile);

            var args = $"--proto_path=\"{protoDir}\" --csharp_out=\"{outputDir}\" \"{protoFile}\"";

            var startInfo = new ProcessStartInfo
            {
                FileName = protocPath,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            try
            {
                using (var process = Process.Start(startInfo))
                {
                    var error = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    if (process.ExitCode != 0 || !string.IsNullOrEmpty(error))
                    {
                        Debug.LogError($"[ProtobufGenerator] 生成失败: {fileName}\n{error}");
                        return false;
                    }

                    Debug.Log($"[ProtobufGenerator] ✅ {fileName} → {outputDir}");
                    return true;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ProtobufGenerator] 执行 protoc 失败: {e.Message}");
                return false;
            }
        }
    }
}
#endif
