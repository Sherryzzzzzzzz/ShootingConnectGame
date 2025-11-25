using System;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

public class SimpleClient : MonoBehaviour
{
    private TcpClient client;
    private NetworkStream stream;
    private byte[] buffer = new byte[1024];
    public string serverIP = "127.0.0.1";
    public int port = 7777;

    void Start()
    {
        ConnectToServer();
    }

    void ConnectToServer()
    {
        try
        {
            client = new TcpClient();
            client.Connect(serverIP, port);
            stream = client.GetStream();

            Debug.Log("已连接到服务器。");

            // 开始异步接收
            stream.BeginRead(buffer, 0, buffer.Length, OnReceive, null);
        }
        catch (Exception e)
        {
            Debug.LogError($"连接失败: {e.Message}");
        }
    }

    void OnReceive(IAsyncResult ar)
    {
        try
        {
            int len = stream.EndRead(ar);
            if (len <= 0) return;

            string msg = Encoding.UTF8.GetString(buffer, 0, len);
            Debug.Log($"收到服务器消息: {msg}");

            // 继续接收
            stream.BeginRead(buffer, 0, buffer.Length, OnReceive, null);
        }
        catch (Exception e)
        {
            Debug.LogError($"接收错误: {e.Message}");
        }
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Return))
        {
            Send("Hello Server!");
        }
    }

    public void Send(string message)
    {
        if (client == null || !client.Connected) return;

        byte[] data = Encoding.UTF8.GetBytes(message);
        stream.Write(data, 0, data.Length);
        Debug.Log($"发送消息: {message}");
    }

    private void OnApplicationQuit()
    {
        stream?.Close();
        client?.Close();
    }
}