// 简单的连接测试脚本
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 简单的服务器连接测试。挂载到任意GameObject上测试。
/// </summary>
public class SimpleConnectionTest : MonoBehaviour
{
    [Header("服务器设置")]
    public string serverIP = "127.0.0.1";
    public int serverPort = 7778;

    [Header("用户设置")]
    public int userId = 1;
    public string username = "TestPlayer";

    [Header("UI (可选)")]
    public TMP_Text statusText;
    public Button connectButton;
    public Button loginButton;

    private void Start()
    {
        // 确保必要的组件存在
        EnsureComponents();

        if (connectButton != null)
            connectButton.onClick.AddListener(OnConnectClick);
        if (loginButton != null)
            loginButton.onClick.AddListener(OnLoginClick);

        UpdateStatus();
    }

    private void EnsureComponents()
    {
        // 确保主线程调度器存在
        if (UnityMainThreadDispatcher.Instance == null)
        {
            var go = new GameObject("UnityMainThreadDispatcher");
            go.AddComponent<UnityMainThreadDispatcher>();
            Debug.Log("[SimpleConnectionTest] 创建 UnityMainThreadDispatcher");
        }

        // 确保 LobbyClient 存在
        if (LobbyClient.Instance == null)
        {
            var go = new GameObject("LobbyClient");
            var client = go.AddComponent<LobbyClient>();
            client.serverIP = serverIP;
            client.serverPort = serverPort;
            client.userId = userId;
            client.username = username;
            Debug.Log("[SimpleConnectionTest] 创建 LobbyClient");
        }

        // 订阅事件
        LobbyClient.Instance.OnConnected += OnConnected;
        LobbyClient.Instance.OnDisconnected += OnDisconnected;
        LobbyClient.Instance.OnLoginResult += OnLoginResult;
    }

    private void OnDestroy()
    {
        if (LobbyClient.Instance != null)
        {
            LobbyClient.Instance.OnConnected -= OnConnected;
            LobbyClient.Instance.OnDisconnected -= OnDisconnected;
            LobbyClient.Instance.OnLoginResult -= OnLoginResult;
        }
    }

    private void Update()
    {
        UpdateStatus();
    }

    public void OnConnectClick()
    {
        Debug.Log("[SimpleConnectionTest] 点击连接按钮");

        if (LobbyClient.Instance == null)
        {
            Debug.LogError("[SimpleConnectionTest] LobbyClient 不存在！");
            return;
        }

        // 更新设置
        LobbyClient.Instance.serverIP = serverIP;
        LobbyClient.Instance.serverPort = serverPort;

        if (!LobbyClient.Instance.IsConnected)
        {
            SetStatus("正在连接...");
            LobbyClient.Instance.Connect();
        }
    }

    public void OnLoginClick()
    {
        Debug.Log("[SimpleConnectionTest] 点击登录按钮");

        if (LobbyClient.Instance == null)
        {
            Debug.LogError("[SimpleConnectionTest] LobbyClient 不存在！");
            return;
        }

        if (LobbyClient.Instance.IsConnected && !LobbyClient.Instance.IsLoggedIn)
        {
            // 更新用户信息
            LobbyClient.Instance.userId = userId;
            LobbyClient.Instance.username = username;

            SetStatus("正在登录...");
            LobbyClient.Instance.Login();
        }
    }

    private void OnConnected()
    {
        Debug.Log("[SimpleConnectionTest] 连接成功！");
        SetStatus("连接成功！请登录。");
    }

    private void OnDisconnected()
    {
        Debug.Log("[SimpleConnectionTest] 连接断开");
        SetStatus("连接断开");
    }

    private void OnLoginResult(bool success, string message)
    {
        if (success)
        {
            Debug.Log($"[SimpleConnectionTest] 登录成功: {message}");
            SetStatus($"登录成功！欢迎 {LobbyClient.Instance.username}");
        }
        else
        {
            Debug.LogWarning($"[SimpleConnectionTest] 登录失败: {message}");
            SetStatus($"登录失败: {message}");
        }
    }

    private void UpdateStatus()
    {
        if (LobbyClient.Instance != null)
        {
            string status = "";
            if (!LobbyClient.Instance.IsConnected)
            {
                status = "未连接";
            }
            else if (!LobbyClient.Instance.IsLoggedIn)
            {
                status = "已连接 - 请登录";
            }
            else
            {
                status = $"已登录 - {LobbyClient.Instance.username}";
            }

            if (statusText != null && !statusText.text.Contains("..."))
            {
                statusText.text = status;
            }
        }
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
        Debug.Log($"[Status] {message}");
    }

    // 在 Inspector 中显示当前状态
    private void OnValidate()
    {
        if (LobbyClient.Instance != null)
        {
            serverIP = LobbyClient.Instance.serverIP;
            serverPort = LobbyClient.Instance.serverPort;
            userId = LobbyClient.Instance.userId;
            username = LobbyClient.Instance.username;
        }
    }
}