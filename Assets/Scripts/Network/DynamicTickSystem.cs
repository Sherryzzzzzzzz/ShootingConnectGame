using System;
using UnityEngine;

/// <summary>
/// Dynamic Tick System for client-side tick adjustment.
/// Uses EWMA-smoothed RTT to adjust tick speed and catch up with the server.
/// </summary>
public class DynamicTickSystem : MonoBehaviour
{
    [Header("Tick Settings")]
    [SerializeField] private float baseTickInterval = 1f / 60f; // 60 Hz
    [SerializeField] private float minTickInterval = 1f / 120f; // 120 Hz max
    [SerializeField] private float maxTickInterval = 1f / 30f;  // 30 Hz min

    [Header("Catchup Settings")]
    [SerializeField] private float catchupThreshold = 0.05f; // 50ms behind
    [SerializeField] private float catchupFactor = 0.1f; // How much to speed up
    [SerializeField] private float slowdownThreshold = 0.02f; // 20ms ahead
    [SerializeField] private float slowdownFactor = 0.1f; // How much to slow down

    [Header("RTT Smoothing")]
    [SerializeField] private float ewmaAlpha = 0.125f; // EWMA smoothing factor

    // State
    private float _currentTickInterval;
    private float _smoothedRtt = 0.05f; // Start at 50ms
    private float _accumulator;
    private int _clientFrameId;
    private int _serverFrameId;
    private int _frameDiff;

    // History for EWMA
    private readonly float[] _rttHistory = new float[8];
    private int _rttHistoryIndex;
    private int _rttHistoryCount;

    // Public accessors
    public float CurrentTickInterval => _currentTickInterval;
    public float SmoothedRtt => _smoothedRtt;
    public int ClientFrameId => _clientFrameId;
    public int ServerFrameId => _serverFrameId;
    public int FrameDiff => _frameDiff;
    public float TickRate => 1f / _currentTickInterval;

    // Singleton
    public static DynamicTickSystem Instance { get; private set; }

    public event Action OnTick;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        _currentTickInterval = baseTickInterval;
    }

    private void Update()
    {
        _accumulator += Time.deltaTime;

        while (_accumulator >= _currentTickInterval)
        {
            OnTick?.Invoke();
            _clientFrameId++;
            _accumulator -= _currentTickInterval;
        }
    }

    /// <summary>
    /// Update with new RTT sample (called when Pong is received).
    /// </summary>
    public void UpdateRtt(float rtt)
    {
        // Add to history
        _rttHistory[_rttHistoryIndex] = rtt;
        _rttHistoryIndex = (_rttHistoryIndex + 1) % _rttHistory.Length;
        _rttHistoryCount = Mathf.Min(_rttHistoryCount + 1, _rttHistory.Length);

        // EWMA smoothing
        _smoothedRtt = (1f - ewmaAlpha) * _smoothedRtt + ewmaAlpha * rtt;

        // Update tick interval based on frame difference
        UpdateTickInterval();
    }

    /// <summary>
    /// Update with server frame info (called when frame is received).
    /// </summary>
    public void UpdateServerFrame(int serverFrameId)
    {
        _serverFrameId = serverFrameId;
        _frameDiff = _clientFrameId - _serverFrameId;

        UpdateTickInterval();
    }

    private void UpdateTickInterval()
    {
        // Calculate desired frame offset based on RTT
        // We want to be half-RTT ahead of server
        float halfRttFrames = _smoothedRtt / baseTickInterval;
        float desiredOffset = halfRttFrames / 2f;

        // Calculate actual offset
        float actualOffset = _frameDiff;

        // Adjust tick interval
        float diff = actualOffset - desiredOffset;

        if (diff > catchupThreshold)
        {
            // We're too far ahead, slow down
            _currentTickInterval = baseTickInterval * (1f + slowdownFactor * (diff - catchupThreshold));
            _currentTickInterval = Mathf.Min(_currentTickInterval, maxTickInterval);
        }
        else if (diff < -catchupThreshold)
        {
            // We're too far behind, speed up
            _currentTickInterval = baseTickInterval * (1f - catchupFactor * (-diff - catchupThreshold));
            _currentTickInterval = Mathf.Max(_currentTickInterval, minTickInterval);
        }
        else
        {
            // We're in the sweet spot, use base interval
            _currentTickInterval = baseTickInterval;
        }
    }

    /// <summary>
    /// Reset tick system for a new battle.
    /// </summary>
    public void Reset(int startFrameId = 0)
    {
        _clientFrameId = startFrameId;
        _serverFrameId = startFrameId;
        _frameDiff = 0;
        _accumulator = 0f;
        _currentTickInterval = baseTickInterval;
        _smoothedRtt = 0.05f;
        _rttHistoryCount = 0;
        _rttHistoryIndex = 0;
    }

    /// <summary>
    /// Force the client frame to a specific value.
    /// </summary>
    public void SetClientFrame(int frameId)
    {
        _clientFrameId = frameId;
    }

    /// <summary>
    /// Get average RTT from history.
    /// </summary>
    public float GetAverageRtt()
    {
        if (_rttHistoryCount == 0) return _smoothedRtt;

        float sum = 0f;
        for (int i = 0; i < _rttHistoryCount; i++)
        {
            sum += _rttHistory[i];
        }
        return sum / _rttHistoryCount;
    }

    /// <summary>
    /// Get RTT variance.
    /// </summary>
    public float GetRttVariance()
    {
        if (_rttHistoryCount < 2) return 0f;

        float avg = GetAverageRtt();
        float sumSquares = 0f;
        for (int i = 0; i < _rttHistoryCount; i++)
        {
            float diff = _rttHistory[i] - avg;
            sumSquares += diff * diff;
        }
        return Mathf.Sqrt(sumSquares / (_rttHistoryCount - 1));
    }

    /// <summary>
    /// Get RTT status string for debug display.
    /// </summary>
    public string GetStatusString()
    {
        return $"RTT: {_smoothedRtt * 1000f:F0}ms (avg: {GetAverageRtt() * 1000f:F0}ms, var: {GetRttVariance() * 1000f:F0}ms)\n" +
               $"Tick: {TickRate:F1}Hz | Frame: {_clientFrameId} (server: {_serverFrameId}, diff: {_frameDiff})";
    }
}