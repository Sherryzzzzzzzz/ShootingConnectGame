// 网络连接诊断工具
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Net;
using System.Net.Sockets;

/// <summary>
/// 网络诊断工具。帮助排查连接问题。
/// </summary>
public class NetworkDiagnostics : MonoBehaviour
{
    [Header("显示区域")]
    [SerializeField] private TMP_Text outputText;

    [Header("测试设置")]
    [SerializeField] private string testIP = "127.0.0.1";
    [SerializeField] private int testPort = 7778;

    public void RunDiagnostics()
    {
        string result = "=== 网络诊断报告 ===\n\n";

        // 1. 检查网络状态
        result += "【网络状态】\n";
        result += $"本机IP: {GetLocalIP()}\n";
        result += $"网络可用: {System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable()}\n\n";

        // 2. 检查服务器连接
        result += "【服务器连接测试】\n";
        result += TestServerConnection(testIP, testPort);
        result += "\n";

        // 3. 检查端口
        result += "【端口检查】\n";
        result += CheckPorts();
        result += "\n";

        // 4. 检查LobbyClient状态
        result += "【LobbyClient状态】\n";
        if (LobbyClient.Instance != null)
        {
            result += $"服务器地址: {LobbyClient.Instance.serverIP}:{LobbyClient.Instance.serverPort}\n";
            result += $"连接状态: {(LobbyClient.Instance.IsConnected ? "已连接" : "未连接")}\n";
            result += $"登录状态: {(LobbyClient.Instance.IsLoggedIn ? "已登录" : "未登录")}\n";
            result += $"用户ID: {LobbyClient.Instance.userId}\n";
            result += $"用户名: {LobbyClient.Instance.username}\n";
        }
        else
        {
            result += "LobbyClient 实例不存在！\n";
        }
        result += "\n";

        // 5. 检查BattleClient状态
        result += "【BattleClient状态】\n";
        if (BattleClient.Instance != null)
        {
            result += $"服务器地址: {BattleClient.Instance.serverIP}:{BattleClient.Instance.serverPort}\n";
            result += $"连接状态: {(BattleClient.Instance.IsConnected ? "已连接" : "未连接")}\n";
            result += $"战斗ID: {BattleClient.Instance.BattleId}\n";
            result += $"玩家ID: {BattleClient.Instance.BattlePlayerId}\n";
        }
        else
        {
            result += "BattleClient 实例不存在！\n";
        }
        result += "\n";

        // 6. 建议
        result += "【排查建议】\n";
        result += "1. 确保服务器已启动 (服务器窗口显示 'LobbyServer started on port 7778')\n";
        result += "2. 检查防火墙是否允许端口 7777 和 7778\n";
        result += "3. 确认服务器IP地址正确 (127.0.0.1 为本地测试)\n";
        result += "4. 如果用WSL或其他虚拟机，使用主机IP而非127.0.0.1\n";

        if (outputText != null)
        {
            outputText.text = result;
        }

        Debug.Log(result);
    }

    private string GetLocalIP()
    {
        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    return ip.ToString();
                }
            }
        }
        catch { }
        return "未知";
    }

    private string TestServerConnection(string ip, int port)
    {
        string result = "";
        try
        {
            // TCP连接测试
            result += $"测试 TCP {ip}:{port}...\n";

            var client = new TcpClient();
            var asyncResult = client.BeginConnect(ip, port, null, null);
            bool success = asyncResult.AsyncWaitHandle.WaitOne(3000); // 3秒超时

            if (success && client.Connected)
            {
                result += "✓ TCP连接成功！服务器正在运行。\n";
                client.EndConnect(asyncResult);
                client.Close();
            }
            else
            {
                result += "✗ TCP连接失败！\n";
                result += "  可能原因:\n";
                result += "  - 服务器未启动\n";
                result += "  - 端口错误\n";
                result += "  - 防火墙阻止\n";
                client.Close();
            }
        }
        catch (System.Exception ex)
        {
            result += $"✗ 连接异常: {ex.Message}\n";
        }

        return result;
    }

    private string CheckPorts()
    {
        string result = "";
        try
        {
            // 检查端口是否被占用
            result += $"端口 7778 (TCP Lobby): {CheckPortTcp(7778)}\n";
            result += $"端口 7777 (UDP Battle): {CheckPortUdp(7777)}\n";
        }
        catch (System.Exception ex)
        {
            result += $"检查失败: {ex.Message}\n";
        }
        return result;
    }

    private string CheckPortTcp(int port)
    {
        try
        {
            var listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            listener.Stop();
            return "可用 (服务器未使用)";
        }
        catch
        {
            return "被占用 (服务器可能正在运行)";
        }
    }

    private string CheckPortUdp(int port)
    {
        try
        {
            var client = new UdpClient(port);
            client.Close();
            return "可用";
        }
        catch
        {
            return "被占用";
        }
    }

    /// <summary>
    /// 尝试连接服务器
    /// </summary>
    public void TryConnect()
    {
        if (LobbyClient.Instance == null)
        {
            Debug.LogError("[NetworkDiagnostics] LobbyClient不存在！");
            return;
        }

        Debug.Log($"[NetworkDiagnostics] 尝试连接 {LobbyClient.Instance.serverIP}:{LobbyClient.Instance.serverPort}");
        LobbyClient.Instance.Connect();
    }

    /// <summary>
    /// 尝试登录
    /// </summary>
    public void TryLogin()
    {
        if (LobbyClient.Instance == null || !LobbyClient.Instance.IsConnected)
        {
            Debug.LogError("[NetworkDiagnostics] 未连接到服务器！");
            return;
        }

        Debug.Log($"[NetworkDiagnostics] 尝试登录，用户ID: {LobbyClient.Instance.userId}");
        LobbyClient.Instance.Login();
    }
}