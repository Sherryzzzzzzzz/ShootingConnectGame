using UnityEngine;
using ShootingGame.Shared.Protocol;

/// <summary>
/// Displays client-side hit feedback: damage screen flash (when hit), crosshair hit marker (when hitting),
/// and health bar. Subscribes to NetworkClient.OnDamage.
/// </summary>
public class HitFeedbackUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private NetworkClient networkClient;

    [Header("Damage Flash")]
    [SerializeField] private Color damageFlashColor = new Color(1f, 0f, 0f, 0.3f);
    [SerializeField] private float damageFlashDuration = 0.3f;

    [Header("Hit Marker")]
    [SerializeField] private Color hitMarkerColor = Color.white;
    [SerializeField] private float hitMarkerSize = 20f;
    [SerializeField] private float hitMarkerDuration = 0.2f;
    [SerializeField] private float hitMarkerGap = 6f;
    [SerializeField] private float hitMarkerLength = 10f;

    [Header("Health Bar")]
    [SerializeField] private Color healthBarColor = Color.green;
    [SerializeField] private Color healthBarBgColor = new Color(0.2f, 0.2f, 0.2f, 0.8f);

    // State
    private float _damageFlashTimer;
    private float _hitMarkerTimer;
    private byte _localHealth = 100;
    private Texture2D _whiteTexture;

    private void Start()
    {
        _whiteTexture = new Texture2D(1, 1);
        _whiteTexture.SetPixel(0, 0, Color.white);
        _whiteTexture.Apply();

        if (networkClient != null)
        {
            networkClient.OnDamage += OnDamageReceived;
        }
    }

    private void OnDestroy()
    {
        if (networkClient != null)
        {
            networkClient.OnDamage -= OnDamageReceived;
        }
        if (_whiteTexture != null)
        {
            Destroy(_whiteTexture);
        }
    }

    private void OnDamageReceived(NetworkClient.DamageEventData data)
    {
        if (networkClient == null) return;

        byte localId = networkClient.LocalPlayerId;

        if (data.TargetId == localId)
        {
            // We got hit — flash screen
            _damageFlashTimer = damageFlashDuration;
            _localHealth = data.NewHealth;
        }

        if (data.ShooterId == localId)
        {
            // We hit someone — show hit marker
            _hitMarkerTimer = hitMarkerDuration;
        }
    }

    /// <summary>
    /// Show hit feedback from HitEventManager.
    /// </summary>
    public void ShowHitFeedback(HitEventMsg hitEvent)
    {
        if (BattleClient.Instance != null && hitEvent.AttackerId == BattleClient.Instance.BattlePlayerId)
        {
            // We hit someone — show hit marker
            _hitMarkerTimer = hitMarkerDuration;
        }

        if (BattleClient.Instance != null && hitEvent.VictimId == BattleClient.Instance.BattlePlayerId)
        {
            // We got hit — flash screen
            _damageFlashTimer = damageFlashDuration;
            _localHealth = (byte)Mathf.Max(0, _localHealth - hitEvent.Damage);
        }
    }

    private void Update()
    {
        if (_damageFlashTimer > 0f)
            _damageFlashTimer -= Time.deltaTime;
        if (_hitMarkerTimer > 0f)
            _hitMarkerTimer -= Time.deltaTime;
    }

    private void OnGUI()
    {
        // Damage flash overlay
        if (_damageFlashTimer > 0f)
        {
            float alpha = damageFlashColor.a * (_damageFlashTimer / damageFlashDuration);
            Color c = new Color(damageFlashColor.r, damageFlashColor.g, damageFlashColor.b, alpha);
            GUI.color = c;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _whiteTexture);
            GUI.color = Color.white;
        }

        // Hit marker (crosshair lines)
        if (_hitMarkerTimer > 0f)
        {
            float alpha = _hitMarkerTimer / hitMarkerDuration;
            Color c = new Color(hitMarkerColor.r, hitMarkerColor.g, hitMarkerColor.b, alpha);
            GUI.color = c;

            float cx = Screen.width * 0.5f;
            float cy = Screen.height * 0.5f;

            // Four diagonal lines around center
            DrawLine(cx - hitMarkerGap - hitMarkerLength, cy - hitMarkerGap - hitMarkerLength,
                     cx - hitMarkerGap, cy - hitMarkerGap, 2f);
            DrawLine(cx + hitMarkerGap, cy - hitMarkerGap,
                     cx + hitMarkerGap + hitMarkerLength, cy - hitMarkerGap - hitMarkerLength, 2f);
            DrawLine(cx - hitMarkerGap - hitMarkerLength, cy + hitMarkerGap + hitMarkerLength,
                     cx - hitMarkerGap, cy + hitMarkerGap, 2f);
            DrawLine(cx + hitMarkerGap, cy + hitMarkerGap,
                     cx + hitMarkerGap + hitMarkerLength, cy + hitMarkerGap + hitMarkerLength, 2f);

            GUI.color = Color.white;
        }

        // Health bar (bottom center)
        float barWidth = 200f;
        float barHeight = 16f;
        float barX = (Screen.width - barWidth) * 0.5f;
        float barY = Screen.height - 50f;

        // Background
        GUI.color = healthBarBgColor;
        GUI.DrawTexture(new Rect(barX, barY, barWidth, barHeight), _whiteTexture);

        // Health fill
        float healthPct = _localHealth / 100f;
        Color hColor = healthPct > 0.5f ? healthBarColor : (healthPct > 0.25f ? Color.yellow : Color.red);
        GUI.color = hColor;
        GUI.DrawTexture(new Rect(barX, barY, barWidth * healthPct, barHeight), _whiteTexture);

        // Health text
        GUI.color = Color.white;
        GUI.Label(new Rect(barX, barY, barWidth, barHeight),
                  $"  HP: {_localHealth}/100",
                  new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontSize = 12 });
    }

    private void DrawLine(float x1, float y1, float x2, float y2, float width)
    {
        float dx = x2 - x1;
        float dy = y2 - y1;
        float len = Mathf.Sqrt(dx * dx + dy * dy);
        if (len < 0.001f) return;

        Vector2 pivot = new Vector2(x1, y1);
        float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;

        GUIUtility.RotateAroundPivot(angle, pivot);
        GUI.DrawTexture(new Rect(x1, y1 - width * 0.5f, len, width), _whiteTexture);
        GUIUtility.RotateAroundPivot(-angle, pivot);
    }
}
