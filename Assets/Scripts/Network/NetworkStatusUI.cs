using UnityEngine;

/// <summary>
/// Displays connection status and ping/RTT information on screen.
/// </summary>
public class NetworkStatusUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private NetworkClient networkClient;

    [Header("Display")]
    [SerializeField] private bool showPing = true;
    [SerializeField] private bool showStatus = true;

    private GUIStyle _labelStyle;
    private GUIStyle _statusStyle;

    private void OnGUI()
    {
        if (networkClient == null) return;

        if (_labelStyle == null)
        {
            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                alignment = TextAnchor.UpperRight,
                normal = { textColor = Color.white }
            };
            _statusStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                alignment = TextAnchor.UpperCenter,
                fontStyle = FontStyle.Bold
            };
        }

        // Connection status (top center)
        if (showStatus)
        {
            string statusText;
            Color statusColor;

            if (networkClient.IsConnected)
            {
                statusText = $"Connected (Player {networkClient.LocalPlayerId})";
                statusColor = Color.green;
            }
            else
            {
                statusText = "Connecting...";
                statusColor = Color.yellow;
            }

            _statusStyle.normal.textColor = statusColor;
            GUI.Label(new Rect(0, 10, Screen.width, 30), statusText, _statusStyle);
        }

        // Ping/RTT (top right)
        if (showPing && networkClient.IsConnected)
        {
            float rttMs = networkClient.Rtt * 1000f;
            Color pingColor;
            if (rttMs < 50f)
                pingColor = Color.green;
            else if (rttMs < 100f)
                pingColor = Color.yellow;
            else
                pingColor = Color.red;

            _labelStyle.normal.textColor = pingColor;
            GUI.Label(new Rect(Screen.width - 160, 10, 150, 30), $"Ping: {rttMs:F0}ms", _labelStyle);

            _labelStyle.normal.textColor = Color.white;
            GUI.Label(new Rect(Screen.width - 160, 30, 150, 30), $"Tick: {networkClient.ServerTick}", _labelStyle);
        }
    }
}
