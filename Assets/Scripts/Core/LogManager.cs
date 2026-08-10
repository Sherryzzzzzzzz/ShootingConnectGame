using UnityEngine;

/// <summary>
/// 分级日志系统。替代裸 Debug.Log，可全局控制输出级别。
/// </summary>
public enum LogLevel { Debug, Info, Warning, Error, None }

public static class Log
{
    public static LogLevel Level = LogLevel.Info;

    public static void Debug(string msg)
    {
        if (Level <= LogLevel.Debug) UnityEngine.Debug.Log(msg);
    }

    public static void Info(string msg)
    {
        if (Level <= LogLevel.Info) UnityEngine.Debug.Log(msg);
    }

    public static void Warning(string msg)
    {
        if (Level <= LogLevel.Warning) UnityEngine.Debug.LogWarning(msg);
    }

    public static void Error(string msg)
    {
        if (Level <= LogLevel.Error) UnityEngine.Debug.LogError(msg);
    }
}
