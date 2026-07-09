// 服务器启动器（编辑器工具）
#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Diagnostics;
using System.IO;

/// <summary>
/// 编辑器工具：启动游戏服务器
/// </summary>
[InitializeOnLoad]
public static class ServerLauncher
{
    private static Process _serverProcess;

    static ServerLauncher()
    {
        // 在编辑器退出时关闭服务器
        EditorApplication.quitting += StopServer;
    }

    [MenuItem("游戏/启动服务器", false, 1)]
    public static void StartServer()
    {
        if (_serverProcess != null && !_serverProcess.HasExited)
        {
            UnityEngine.Debug.LogWarning("[ServerLauncher] 服务器已在运行");
            return;
        }

        // 查找服务器可执行文件路径
        string projectPath = Directory.GetParent(Application.dataPath).FullName;
        string serverPath = Path.Combine(projectPath, "Server", "ShootingGame.Server");

        // 检查 Debug 版本
        string debugExe = Path.Combine(serverPath, "bin", "Debug", "net8.0", "ShootingGame.Server.exe");
        string releaseExe = Path.Combine(serverPath, "bin", "Release", "net8.0", "ShootingGame.Server.exe");

        string exePath = File.Exists(debugExe) ? debugExe : (File.Exists(releaseExe) ? releaseExe : null);

        if (exePath == null)
        {
            UnityEngine.Debug.LogError($"[ServerLauncher] 找不到服务器可执行文件\n请先编译服务器项目:\n  cd Server && dotnet build");
            EditorUtility.DisplayDialog("启动服务器失败", "找不到服务器可执行文件。\n请先在 Server 目录下运行 'dotnet build' 编译服务器项目。", "确定");
            return;
        }

        // 启动服务器进程
        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            WorkingDirectory = Path.GetDirectoryName(exePath),
            UseShellExecute = true,
            CreateNoWindow = false,
            WindowStyle = ProcessWindowStyle.Normal
        };

        try
        {
            _serverProcess = Process.Start(startInfo);
            UnityEngine.Debug.Log($"[ServerLauncher] 服务器已启动 (PID: {_serverProcess.Id})");
        }
        catch (System.Exception ex)
        {
            UnityEngine.Debug.LogError($"[ServerLauncher] 启动服务器失败: {ex.Message}");
        }
    }

    [MenuItem("游戏/停止服务器", false, 2)]
    public static void StopServer()
    {
        if (_serverProcess != null && !_serverProcess.HasExited)
        {
            _serverProcess.Kill();
            _serverProcess = null;
            UnityEngine.Debug.Log("[ServerLauncher] 服务器已停止");
        }
    }

    [MenuItem("游戏/编译服务器", false, 3)]
    public static void BuildServer()
    {
        string projectPath = Directory.GetParent(Application.dataPath).FullName;
        string serverPath = Path.Combine(projectPath, "Server");

        // 使用 dotnet build 编译
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = "build",
            WorkingDirectory = serverPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        try
        {
            var process = Process.Start(startInfo);
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode == 0)
            {
                UnityEngine.Debug.Log($"[ServerLauncher] 编译成功\n{output}");
            }
            else
            {
                UnityEngine.Debug.LogError($"[ServerLauncher] 编译失败\n{error}");
            }
        }
        catch (System.Exception ex)
        {
            UnityEngine.Debug.LogError($"[ServerLauncher] 编译失败: {ex.Message}");
        }
    }

    [MenuItem("游戏/编译并启动", false, 0)]
    public static void BuildAndStart()
    {
        BuildServer();
        if (_serverProcess == null || _serverProcess.HasExited)
        {
            // 编译成功后启动
            EditorApplication.delayCall += () =>
            {
                StartServer();
            };
        }
    }

    [MenuItem("游戏/启动服务器", true)]
    public static bool ValidateStartServer()
    {
        return _serverProcess == null || _serverProcess.HasExited;
    }

    [MenuItem("游戏/停止服务器", true)]
    public static bool ValidateStopServer()
    {
        return _serverProcess != null && !_serverProcess.HasExited;
    }
}
#endif