using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;

namespace ShootingGame.Shared.Protocol
{
    /// <summary>
    /// Packet strategy for network simulation.
    /// </summary>
    public enum PacketStrategy
    {
        /// <summary>Data packets: can be dropped and delayed.</summary>
        Data = 0,
        /// <summary>Control packets: delayed but never dropped.</summary>
        Control = 1,
        /// <summary>Route setup packets: no simulation (pass through).</summary>
        RouteSetup = 2,
    }

    /// <summary>
    /// Network simulator for testing weak network conditions.
    /// Supports configurable drop rate, delay range, and per-packet strategy.
    /// Uses a priority queue + dedicated scheduler thread.
    /// </summary>
    public class NetSimulator
    {
        private float _dropRate;
        private int _delayMinMs;
        private int _delayMaxMs;
        private bool _enabled;

        private readonly Action<byte[], int, IPEndPoint> _sendCallback;
        private readonly Dictionary<PacketStrategy, (float dropRate, int delayMin, int delayMax)> _strategyOverrides
            = new Dictionary<PacketStrategy, (float, int, int)>();

        // Delayed packet queue
        private readonly List<DelayedPacket> _delayQueue = new List<DelayedPacket>();
        private readonly object _queueLock = new object();

        // Scheduler thread
        private Thread _schedulerThread;
        private volatile bool _running;

        // Random
        private readonly Random _rng = new Random();

        public float DropRate => _dropRate;
        public int DelayMinMs => _delayMinMs;
        public int DelayMaxMs => _delayMaxMs;
        public bool Enabled
        {
            get => _enabled;
            set
            {
                _enabled = value;
                if (_enabled) EnsureSchedulerRunning();
            }
        }

        public NetSimulator(Action<byte[], int, IPEndPoint> sendCallback)
        {
            _sendCallback = sendCallback;
        }

        /// <summary>Configure global simulation parameters.</summary>
        public void Configure(float dropRate, int delayMinMs, int delayMaxMs)
        {
            _dropRate = System.Math.Clamp(dropRate, 0f, 1f);
            _delayMinMs = System.Math.Max(0, delayMinMs);
            _delayMaxMs = System.Math.Max(_delayMinMs, delayMaxMs);
        }

        /// <summary>Configure per-strategy override.</summary>
        public void ConfigureStrategy(PacketStrategy strategy, float dropRate, int delayMinMs, int delayMaxMs)
        {
            _strategyOverrides[strategy] = (System.Math.Clamp(dropRate, 0f, 1f),
                System.Math.Max(0, delayMinMs), System.Math.Max(delayMinMs, delayMaxMs));
        }

        /// <summary>Start the simulation with given parameters.</summary>
        public void Start(float dropRate, int delayMinMs, int delayMaxMs)
        {
            Configure(dropRate, delayMinMs, delayMaxMs);
            _enabled = true;
            EnsureSchedulerRunning();
        }

        /// <summary>Stop all simulation (flush pending packets).</summary>
        public void Stop()
        {
            _enabled = false;
            _running = false;

            // Flush all pending packets immediately
            lock (_queueLock)
            {
                foreach (var dp in _delayQueue)
                {
                    _sendCallback(dp.Data, dp.Length, dp.Endpoint);
                }
                _delayQueue.Clear();
            }
        }

        /// <summary>
        /// Process an outgoing packet. May drop, delay, or pass through.
        /// </summary>
        public void ProcessOutgoing(byte[] data, int length, IPEndPoint endpoint, PacketStrategy strategy)
        {
            if (!_enabled || strategy == PacketStrategy.RouteSetup)
            {
                _sendCallback(data, length, endpoint);
                return;
            }

            float effectiveDropRate = _dropRate;
            int effectiveDelayMin = _delayMinMs;
            int effectiveDelayMax = _delayMaxMs;

            if (_strategyOverrides.TryGetValue(strategy, out var ovr))
            {
                effectiveDropRate = ovr.dropRate;
                effectiveDelayMin = ovr.delayMin;
                effectiveDelayMax = ovr.delayMax;
            }

            // Drop check (Control strategy never drops)
            if (strategy != PacketStrategy.Control && effectiveDropRate > 0)
            {
                if (_rng.NextDouble() < effectiveDropRate)
                    return; // Packet dropped
            }

            // Delay
            int delayMs = 0;
            if (effectiveDelayMax > 0)
            {
                delayMs = effectiveDelayMin + _rng.Next(effectiveDelayMax - effectiveDelayMin + 1);
            }

            if (delayMs <= 0)
            {
                _sendCallback(data, length, endpoint);
                return;
            }

            // Copy data for delayed delivery
            var delayedData = new byte[length];
            Buffer.BlockCopy(data, 0, delayedData, 0, length);

            long deliverAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + delayMs;

            lock (_queueLock)
            {
                _delayQueue.Add(new DelayedPacket
                {
                    Data = delayedData,
                    Length = length,
                    Endpoint = endpoint,
                    DeliverAtMs = deliverAt
                });
                // Sort by delivery time (ascending)
                _delayQueue.Sort((a, b) => a.DeliverAtMs.CompareTo(b.DeliverAtMs));
                Monitor.Pulse(_queueLock);
            }
        }

        private void EnsureSchedulerRunning()
        {
            if (_schedulerThread != null && _schedulerThread.IsAlive) return;

            _running = true;
            _schedulerThread = new Thread(SchedulerLoop)
            {
                IsBackground = true,
                Name = "NetSim_Scheduler"
            };
            _schedulerThread.Start();
        }

        private void SchedulerLoop()
        {
            while (_running)
            {
                DelayedPacket next;
                long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                lock (_queueLock)
                {
                    if (_delayQueue.Count == 0)
                    {
                        // No packets — wait for a signal or timeout
                        Monitor.Wait(_queueLock, 100);
                        continue;
                    }

                    next = _delayQueue[0];
                    long waitMs = next.DeliverAtMs - now;

                    if (waitMs > 0)
                    {
                        Monitor.Wait(_queueLock, System.Math.Min((int)waitMs, 100));
                        continue;
                    }

                    _delayQueue.RemoveAt(0);
                }

                // Deliver the packet
                _sendCallback(next.Data, next.Length, next.Endpoint);
            }
        }

        private struct DelayedPacket
        {
            public byte[] Data;
            public int Length;
            public IPEndPoint Endpoint;
            public long DeliverAtMs;
        }
    }
}
