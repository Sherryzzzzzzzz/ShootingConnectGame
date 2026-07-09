using System.Collections.Generic;
using UnityEngine;
using ShootingGame.Shared.Math;
using ShootingGame.Shared.Simulation;

/// <summary>
/// 直观的网络调试面板。显示本地/远程玩家的位置、朝向(可视化方向指示器)、输入和状态。
/// 挂到场景中的任意 GameObject 上即可生效。
/// </summary>
public class NetworkDebugOverlay : MonoBehaviour
{
    [Header("Display")]
    [SerializeField] private bool showPanel = true;
    [SerializeField] private int panelWidth = 320;
    [SerializeField] private int panelHeight = 260;
    [SerializeField] private int margin = 10;

    // 缓存
    private GUIStyle _headerStyle;
    private GUIStyle _labelStyle;
    private GUIStyle _valueStyle;
    private GUIStyle _localHeader;
    private GUIStyle _remoteHeader;
    private Texture2D _bgTex;
    private Texture2D _dirTex;

    // 本地玩家最后一次发送的输入
    public float LastSentAimYaw;
    public float LastSentMoveX;
    public float LastSentMoveZ;
    public bool LastSentAim;
    public bool LastSentRun;
    public int LastTick;

    // 远程玩家最后收到的状态
    public struct RemoteDebugInfo
    {
        public int PlayerId;
        public int TeamId;
        public float RotationY;
        public float PositionX;
        public float PositionZ;
        public float VelocityX;
        public float VelocityZ;
        public bool IsRunning;
        public int Hp;
        public float ActualTransformYaw;
    }
    public readonly List<RemoteDebugInfo> RemotePlayers = new List<RemoteDebugInfo>();

    private NetPlayerController _localController;
    private BattleClient _battleClient;
    private int _lastSentTick;

    private void Start()
    {
        _localController = FindFirstObjectByType<NetPlayerController>();
        _battleClient = BattleClient.Instance;
    }

    private void Update()
    {
        if (_localController != null && _battleClient != null && _battleClient.IsInBattle)
        {
            var snap = _localController.CurrentSnapshot;
            LastTick = snap.Tick;
            LastSentMoveX = snap.Velocity.x;
            LastSentMoveZ = snap.Velocity.z;
            LastSentAimYaw = snap.Rotation.EulerAngles.y;
        }

        // 同步远程玩家列表：添加新玩家，移除断开玩家，更新实时数据
        var currentIds = new HashSet<int>();
        foreach (var kvp in RemotePlayerController.AllRemotePlayers)
        {
            var ctrl = kvp.Value;
            int id = ctrl.PlayerId;
            currentIds.Add(id);

            var existing = FindRemoteInfo(id);
            if (existing < 0)
            {
                RemotePlayers.Add(new RemoteDebugInfo
                {
                    PlayerId = id,
                    TeamId = ctrl.TeamId,
                    Hp = ctrl.CurrentHp,
                    ActualTransformYaw = ctrl.transform.eulerAngles.y
                });
            }
            else
            {
                var info = RemotePlayers[existing];
                info.Hp = ctrl.CurrentHp;
                info.ActualTransformYaw = ctrl.transform.eulerAngles.y;
                info.TeamId = ctrl.TeamId;
                RemotePlayers[existing] = info;
            }
        }

        // 移除已断开的远程玩家
        for (int i = RemotePlayers.Count - 1; i >= 0; i--)
        {
            if (!currentIds.Contains(RemotePlayers[i].PlayerId))
                RemotePlayers.RemoveAt(i);
        }
    }

    private int FindRemoteInfo(int playerId)
    {
        for (int i = 0; i < RemotePlayers.Count; i++)
        {
            if (RemotePlayers[i].PlayerId == playerId)
                return i;
        }
        return -1;
    }

    // 由 NetPlayerController 调用，记录发送的输入
    public void RecordSentInput(float aimYaw, bool aim, bool run, float moveX, float moveZ)
    {
        LastSentAimYaw = aimYaw;
        LastSentAim = aim;
        LastSentRun = run;
        LastSentMoveX = moveX;
        LastSentMoveZ = moveZ;
    }

    // 由 RemotePlayerController 调用，记录收到的状态
    public void RecordRemoteState(int playerId, int teamId, float rotationY, float posX, float posZ,
        float velX, float velZ, bool isRunning, int hp)
    {
        int idx = FindRemoteInfo(playerId);
        if (idx < 0)
        {
            // 帧数据先于 Update 到达，直接添加
            RemotePlayers.Add(new RemoteDebugInfo
            {
                PlayerId = playerId,
                TeamId = teamId,
                RotationY = rotationY,
                PositionX = posX,
                PositionZ = posZ,
                VelocityX = velX,
                VelocityZ = velZ,
                IsRunning = isRunning,
                Hp = hp
            });
            return;
        }

        var info = RemotePlayers[idx];
        info.RotationY = rotationY;
        info.PositionX = posX;
        info.PositionZ = posZ;
        info.VelocityX = velX;
        info.VelocityZ = velZ;
        info.IsRunning = isRunning;
        info.Hp = hp;
        RemotePlayers[idx] = info;
    }

    private void OnGUI()
    {
        if (!showPanel) return;
        InitStyles();

        // === 本地玩家面板（左上角）===
        DrawLocalPanel();

        // === 远程玩家面板（右侧排列）===
        int remoteIndex = 0;
        foreach (var info in RemotePlayers)
        {
            DrawRemotePanel(info, remoteIndex);
            remoteIndex++;
        }
    }

    private void DrawLocalPanel()
    {
        int x = margin;
        int y = margin + 40;
        var rect = new Rect(x, y, panelWidth, panelHeight);
        GUI.DrawTexture(rect, _bgTex);
        GUI.Label(new Rect(x + 8, y + 4, panelWidth - 16, 22), $"LOCAL PLAYER  (ID={_battleClient?.BattlePlayerId ?? -1})", _localHeader);

        int lineY = y + 32;
        float dirYaw = LastSentAimYaw;

        // 朝向可视化（上半部分）
        DrawDirectionIndicator(x + 15, lineY, 80, dirYaw, "Sent AimYaw");

        // 右侧数据
        int dataX = x + 115;
        DrawLabel(dataX, lineY, $"Tick: {LastTick}");
        DrawLabel(dataX, lineY + 18, $"AimYaw: {LastSentAimYaw:F1}°");
        DrawLabel(dataX, lineY + 36, $"Aim: {(LastSentAim ? "ON" : "OFF")}");
        DrawLabel(dataX, lineY + 54, $"Run: {(LastSentRun ? "ON" : "OFF")}");
        DrawLabel(dataX, lineY + 72, $"Move: ({LastSentMoveX:F2}, {LastSentMoveZ:F2})");

        lineY += 100;

        // 快照朝向（RotationY from snapshot）
        if (_localController != null)
        {
            var snap = _localController.CurrentSnapshot;
            float snapYaw = snap.Rotation.EulerAngles.y;
            DrawDirectionIndicator(x + 15, lineY, 60, snapYaw, "Snap RotY");
            DrawLabel(dataX, lineY, $"Snap RotY: {snapYaw:F1}°");
            DrawLabel(dataX, lineY + 18, $"Pos: ({snap.Position.x:F2}, {snap.Position.z:F2})");
            DrawLabel(dataX, lineY + 36, $"Vel: ({snap.Velocity.x:F2}, {snap.Velocity.z:F2})");
            DrawLabel(dataX, lineY + 54, $"HP: {snap.Health}");
        }
    }

    private void DrawRemotePanel(RemoteDebugInfo info, int index)
    {
        int x = margin + (index % 2) * (panelWidth + margin);
        int y = margin + 40 + (index / 2) * (panelHeight + margin);

        // 如果只有一个远程玩家，放在右侧
        if (RemotePlayers.Count == 1)
        {
            x = Screen.width - panelWidth - margin;
            y = margin + 40;
        }

        var rect = new Rect(x, y, panelWidth, panelHeight);
        GUI.DrawTexture(rect, _bgTex);

        string teamLabel = info.TeamId == 1 ? "BLUE" : "RED";
        GUI.Label(new Rect(x + 8, y + 4, panelWidth - 16, 22), $"REMOTE PLAYER {info.PlayerId}  ({teamLabel})", _remoteHeader);

        int lineY = y + 32;

        // 朝向可视化：协议收到的 RotationY
        DrawDirectionIndicator(x + 15, lineY, 70, info.RotationY, "Net RotY");

        // 数据
        int dataX = x + 105;
        DrawLabel(dataX, lineY, $"Net RotY: {info.RotationY:F1}°");
        DrawLabel(dataX, lineY + 18, $"ActualYaw: {info.ActualTransformYaw:F1}°");
        DrawLabel(dataX, lineY + 36, $"Diff: {Mathf.DeltaAngle(info.RotationY, info.ActualTransformYaw):F1}°");
        DrawLabel(dataX, lineY + 54, $"Run: {(info.IsRunning ? "ON" : "OFF")}");
        DrawLabel(dataX, lineY + 72, $"HP: {info.Hp}");

        lineY += 95;

        DrawLabel(x + 15, lineY, $"Pos: ({info.PositionX:F2}, {info.PositionZ:F2})");
        DrawLabel(x + 15, lineY + 18, $"Vel: ({info.VelocityX:F2}, {info.VelocityZ:F2})");

        // 位置差距（相对于本地玩家）
        if (_localController != null)
        {
            var localPos = _localController.CurrentSnapshot.Position;
            float dist = Mathf.Sqrt(
                (info.PositionX - localPos.x) * (info.PositionX - localPos.x) +
                (info.PositionZ - localPos.z) * (info.PositionZ - localPos.z));
            DrawLabel(x + 15, lineY + 36, $"距离本地: {dist:F1}m");
        }
    }

    /// <summary>
    /// 绘制可视化朝向指示器：一个圆 + 一条线指向朝向。
    /// </summary>
    private void DrawDirectionIndicator(int x, int y, int size, float yawDeg, string label)
    {
        var center = new Vector2(x + size / 2, y + size / 2);
        float radius = size / 2 - 4;

        // 背景圆
        var circleRect = new Rect(x, y, size, size);
        GUI.color = new Color(0.2f, 0.2f, 0.2f, 0.8f);
        GUI.DrawTexture(circleRect, _dirTex);

        // 朝向线
        float yawRad = yawDeg * Mathf.Deg2Rad;
        float endX = center.x + Mathf.Sin(yawRad) * radius;
        float endY = center.y - Mathf.Cos(yawRad) * radius;

        // 用 GL 或简单的线条纹理绘制（这里用简单的办法: 画一条短线表示朝向）
        Vector2 dir = new Vector2(Mathf.Sin(yawRad), -Mathf.Cos(yawRad));
        Vector2 start = center - dir * (radius * 0.3f);
        Vector2 end = center + dir * radius;

        // 使用 Handles 替代方案：GUI 绘制一条粗线
        DrawThickLine(start, end, 3f, Color.green);

        // N 标记（北方=0°）
        GUI.color = Color.gray;
        var nLabelRect = new Rect(center.x - 8, y - 2, 20, 14);
        GUI.Label(nLabelRect, "N", _labelStyle);

        // 标签
        GUI.color = Color.white;
        var labelRect = new Rect(x, y + size + 2, size, 16);
        GUI.Label(labelRect, label, _labelStyle);
    }

    private void DrawThickLine(Vector2 start, Vector2 end, float thickness, Color color)
    {
        Vector2 dir = (end - start).normalized;
        Vector2 normal = new Vector2(-dir.y, dir.x);
        Vector2 a = start + normal * thickness;
        Vector2 b = end + normal * thickness;
        Vector2 c = end - normal * thickness;
        Vector2 d = start - normal * thickness;

        var prevColor = GUI.color;
        GUI.color = color;

        // 使用三角形绘制
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        var matrix = GUI.matrix;
        float length = Vector2.Distance(start, end);
        GUIUtility.RotateAroundPivot(angle, start);
        var rect = new Rect(start.x, start.y - thickness, length, thickness * 2);
        GUI.DrawTexture(rect, _dirTex);
        GUI.matrix = matrix;

        GUI.color = prevColor;
    }

    private void DrawLabel(int x, int y, string text)
    {
        GUI.Label(new Rect(x, y, 200, 20), text, _labelStyle);
    }

    private void InitStyles()
    {
        if (_bgTex == null)
        {
            _bgTex = MakeTex(1, 1, new Color(0.1f, 0.1f, 0.15f, 0.85f));
            _dirTex = MakeTex(1, 1, Color.white);
        }

        if (_headerStyle == null)
        {
            _headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };

            _localHeader = new GUIStyle(_headerStyle)
            {
                normal = { textColor = Color.green }
            };

            _remoteHeader = new GUIStyle(_headerStyle)
            {
                normal = { textColor = new Color(1f, 0.6f, 0.2f) }
            };

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                normal = { textColor = new Color(0.85f, 0.85f, 0.85f) },
                alignment = TextAnchor.MiddleLeft
            };

            _valueStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white },
                alignment = TextAnchor.MiddleLeft
            };
        }
    }

    private Texture2D MakeTex(int w, int h, Color col)
    {
        var pix = new Color[w * h];
        for (int i = 0; i < pix.Length; i++)
            pix[i] = col;
        var tex = new Texture2D(w, h);
        tex.SetPixels(pix);
        tex.Apply();
        return tex;
    }
}
