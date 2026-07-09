using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Threading;
using ShootingGame.Shared.Math;
using ShootingGame.Shared.Physics;
using ShootingGame.Shared.Protocol;
using ShootingGame.Shared.Protocol.Kcp;
using ShootingGame.Shared.Simulation;
using ShootingGame.Shared.ECS;
using ShootingGame.Server.ECS;

namespace ShootingGame.Server
{
    [Obsolete("GameServer is the legacy monolithic server. Use LobbyServer + BattleUdpServer + BattleRoom for new code. Kept for EndToEndTests.")]
    public class GameServer
    {
        private readonly int _port;
        private readonly int _tickRate;
        private UdpTransport _transport;
        private CollisionWorld _collisionWorld;
        private ServerECSWorld _ecsWorld;

        // Players
        private readonly Connection[] _connections = new Connection[GameConstants.MaxPlayers];
        private readonly PlayerSnapshot[] _playerSnapshots = new PlayerSnapshot[GameConstants.MaxPlayers];
        private readonly InputBuffer[] _inputBuffers = new InputBuffer[GameConstants.MaxPlayers];
        private readonly int[] _lastProcessedInputTick = new int[GameConstants.MaxPlayers];
        private readonly bool[] _playerConnected = new bool[GameConstants.MaxPlayers];
        private readonly float[] _respawnTimers = new float[GameConstants.MaxPlayers];

        // World history for lag compensation
        private readonly WorldHistory _worldHistory = new WorldHistory();

        // Session manager for reconnection support
        private readonly SessionManager _sessionManager = new SessionManager();

        // Tick state
        private int _currentTick;
        private volatile bool _running;

        // Spawn positions
        private static readonly Vec3[] SpawnPositions = new Vec3[]
        {
            new Vec3(0, 0, 0),
            new Vec3(5, 0, 5),
            new Vec3(-5, 0, 5),
            new Vec3(5, 0, -5),
            new Vec3(-5, 0, -5),
            new Vec3(10, 0, 0),
            new Vec3(-10, 0, 0),
            new Vec3(0, 0, 10),
            new Vec3(0, 0, -10),
            new Vec3(10, 0, 10)
        };

        public GameServer(int port = 7777, int tickRate = GameConstants.TickRate)
        {
            _port = port;
            _tickRate = tickRate;
        }

        public void Stop() => _running = false;

        public void Run(string collisionDataPath = null)
        {
            // Load collision data
            if (collisionDataPath != null && System.IO.File.Exists(collisionDataPath))
            {
                _collisionWorld = CollisionWorld.Load(collisionDataPath);
                Log($"Loaded collision data: {_collisionWorld.Count} boxes");
            }
            else
            {
                _collisionWorld = new CollisionWorld();
                // Add a default floor
                _collisionWorld.AddBox(new AABB(new Vec3(-50, -1, -50), new Vec3(50, 0, 50)));
                Log("No collision data file. Using default floor.");
            }

            // Initialize ECS world
            _ecsWorld = new ServerECSWorld();
            _ecsWorld.SetCollisionWorld(_collisionWorld);

            // Initialize input buffers
            for (int i = 0; i < GameConstants.MaxPlayers; i++)
            {
                _inputBuffers[i] = new InputBuffer();
                _playerSnapshots[i] = PlayerSnapshot.Default(SpawnPositions[i]);
                _ecsWorld.RegisterPlayer(i, _playerSnapshots[i]);
            }

            // Start networking
            _transport = new UdpTransport();
            _transport.Start(_port);
            Log($"Server started on port {_port}, tick rate {_tickRate}");

            _running = true;
            Console.CancelKeyPress += (s, e) => { e.Cancel = true; _running = false; };

            // Main game loop
            var sw = Stopwatch.StartNew();
            float tickInterval = 1f / _tickRate;
            long tickIntervalTicks = (long)(tickInterval * Stopwatch.Frequency);
            long nextTickTime = sw.ElapsedTicks;

            while (_running)
            {
                long now = sw.ElapsedTicks;
                if (now < nextTickTime)
                {
                    int sleepMs = (int)((nextTickTime - now) * 1000 / Stopwatch.Frequency);
                    if (sleepMs > 1) Thread.Sleep(sleepMs - 1);
                    // Spin-wait for sub-ms precision
                    while (sw.ElapsedTicks < nextTickTime) { Thread.SpinWait(10); }
                }

                float currentTime = (float)sw.ElapsedTicks / Stopwatch.Frequency;

                // 1. Drain receive queue
                DrainReceiveQueue(currentTime);

                // 2. Simulate all connected players
                for (int i = 0; i < GameConstants.MaxPlayers; i++)
                {
                    if (!_playerConnected[i]) continue;
                    if (_playerSnapshots[i].Health <= 0) continue; // dead players don't simulate

                    var input = _inputBuffers[i].Get(_currentTick);
                    input.Tick = _currentTick; // force tick alignment
                    var entity = _ecsWorld.GetEntity(i);
                    if (entity.IsValid && _ecsWorld.EntityManager.IsValid(entity))
                    {
                        _ecsWorld.TickPlayer(i, entity, input, tickInterval);
                        _playerSnapshots[i] = _ecsWorld.GetSnapshot(i, _currentTick);
                    }
                    else
                    {
                        _playerSnapshots[i] = PlayerSimulation.Simulate(_playerSnapshots[i], input, tickInterval, _collisionWorld);
                    }
                    _lastProcessedInputTick[i] = _inputBuffers[i].LastReceivedTick;
                }

                // 3. Store world history
                var worldSnap = new WorldSnapshot
                {
                    Tick = _currentTick,
                    Players = (PlayerSnapshot[])_playerSnapshots.Clone()
                };
                _worldHistory.Store(_currentTick, worldSnap);

                // 3.5. Process fire requests with lag compensation
                ProcessFireRequests(currentTime);

                // 3.6. Process respawns
                ProcessRespawns(tickInterval);

                // 4. Broadcast world state
                BroadcastWorldState();

                // 5. Process reliable retransmits and heartbeats
                ProcessReliables(currentTime);

                // 6. Check timeouts
                CheckTimeouts(currentTime);

                _currentTick++;
                nextTickTime += tickIntervalTicks;

                // Catch-up cap: don't let it spiral
                long maxCatchup = tickIntervalTicks * 3;
                if (sw.ElapsedTicks - nextTickTime > maxCatchup)
                    nextTickTime = sw.ElapsedTicks;
            }

            // Shutdown
            Log("Shutting down...");
            _transport.Stop();
        }

        private void DrainReceiveQueue(float currentTime)
        {
            while (_transport.TryReceive(out var packet))
            {
                ProcessPacket(packet, currentTime);
            }
        }

        private void ProcessPacket(ReceivedPacket packet, float currentTime)
        {
            if (packet.Length < 1) return;

            int playerIdx = FindConnection(packet.RemoteEndPoint);

            if (playerIdx >= 0)
            {
                _connections[playerIdx].MarkReceived(currentTime);

                if (_connections[playerIdx].Session.Input(packet.Data, packet.Length, out byte[] unreliablePayload))
                {
                    // Unreliable message — dispatch directly
                    var gameMsg = ProtobufSerializer.DeserializeGameMessage(unreliablePayload);
                    DispatchGameMessage(gameMsg, playerIdx);
                }
                // KCP data is fed internally; reliable messages extracted during KcpUpdate
            }
            else
            {
                // Unknown endpoint — handle new connection
                if (packet.Length < KcpChannel.KcpMinHeaderSize) return;

                // Extract conv from KCP segment header
                uint conv = KcpChannel.ExtractConv(packet.Data, 0);
                if (conv == 0) return;

                HandleNewConnection(packet.RemoteEndPoint, conv, packet.Data, packet.Length, currentTime);
            }
        }

        private void DispatchGameMessage(GameMessage gameMsg, int playerIdx)
        {
            switch (gameMsg.MsgType)
            {
                case GameMessageType.InputMessage:
                    HandleInputMessage(playerIdx, gameMsg.InputBatch);
                    break;

                case GameMessageType.Disconnect:
                    HandleDisconnect(playerIdx);
                    break;

                case GameMessageType.Heartbeat:
                    break;
            }
        }

        private void HandleNewConnection(IPEndPoint remote, uint conv, byte[] data, int length, float currentTime)
        {
            // Create a temporary KcpChannel to process the ConnectionRequest
            var tempKcp = new KcpChannel(conv, (buf, len) =>
            {
                _transport.Send(buf, len, remote);
            });
            tempKcp.Input(data, length, out _);
            tempKcp.Update((uint)(currentTime * 1000));

            if (tempKcp.TryRecv(out byte[] reliableMsg))
            {
                var gameMsg = ProtobufSerializer.DeserializeGameMessage(reliableMsg);
                if (gameMsg.MsgType == GameMessageType.ConnectionRequest)
                {
                    HandleConnectionRequest(remote, conv, gameMsg.ConnectionRequest, currentTime);
                }
            }
        }

        private void HandleConnectionRequest(IPEndPoint remote, uint conv, ConnectionRequestMsg request, float currentTime)
        {
            // Check if already connected
            if (FindConnection(remote) >= 0)
                return;

            // Find free slot
            int slot = -1;
            for (int i = 0; i < GameConstants.MaxPlayers; i++)
            {
                if (!_playerConnected[i])
                {
                    slot = i;
                    break;
                }
            }

            if (slot < 0)
            {
                Log($"Connection rejected from {remote} — server full");
                return;
            }

            // Accept
            _connections[slot] = new Connection(remote, (byte)slot, conv, (buf, len) =>
            {
                _transport.Send(buf, len, remote);
            });
            _connections[slot].MarkConnected(currentTime);
            _playerConnected[slot] = true;
            _playerSnapshots[slot] = PlayerSnapshot.Default(SpawnPositions[slot]);
            _inputBuffers[slot] = new InputBuffer();

            Log($"Player {slot} connected from {remote} (conv={conv})");

            // Send ConnectionAccepted (reliable via KCP)
            var acceptMsg = new GameMessage
            {
                MsgType = GameMessageType.ConnectionAccepted,
                ConnectionAccepted = new ConnectionAcceptedMsg
                {
                    PlayerId = (byte)slot,
                    TickRate = _tickRate,
                    ServerTick = _currentTick
                }
            };
            byte[] acceptPayload = ProtobufSerializer.SerializeGameMessage(acceptMsg);
            _connections[slot].Session.SendReliable(acceptPayload);

            // Notify other players
            var joinMsg = new GameMessage
            {
                MsgType = GameMessageType.PlayerJoined,
                PlayerJoined = new PlayerJoinedMsg { PlayerId = (byte)slot }
            };
            byte[] joinPayload = ProtobufSerializer.SerializeGameMessage(joinMsg);
            for (int i = 0; i < GameConstants.MaxPlayers; i++)
            {
                if (i != slot && _playerConnected[i])
                {
                    _connections[i].Session.SendReliable(joinPayload);
                }
            }
        }

        private void HandleInputMessage(int playerIdx, InputBatchMsg batch)
        {
            if (batch.Frames == null) return;
            foreach (var frameMsg in batch.Frames)
            {
                _inputBuffers[playerIdx].Store(ProtobufSerializer.ToInputFrame(frameMsg));
            }
        }

        private void HandleDisconnect(int playerIdx)
        {
            DisconnectPlayer(playerIdx, "disconnected");
        }

        private void DisconnectPlayer(int playerIdx, string reason)
        {
            Log($"Player {playerIdx} {reason}");

            // Notify remaining players
            var leaveMsg = new GameMessage
            {
                MsgType = GameMessageType.PlayerLeft,
                PlayerLeft = new PlayerLeftMsg { PlayerId = (byte)playerIdx }
            };
            byte[] leavePayload = ProtobufSerializer.SerializeGameMessage(leaveMsg);
            for (int i = 0; i < GameConstants.MaxPlayers; i++)
            {
                if (i != playerIdx && _playerConnected[i])
                {
                    float time = _connections[i].LastReceiveTime;
                    SendReliable(i, leavePayload, time);
                }
            }

            // Free the slot
            _playerConnected[playerIdx] = false;
            _connections[playerIdx] = null;

            // Reset snapshot to default
            _playerSnapshots[playerIdx] = PlayerSnapshot.Default(SpawnPositions[playerIdx]);
            _inputBuffers[playerIdx] = new InputBuffer();
            _lastProcessedInputTick[playerIdx] = 0;
        }

        private void BroadcastWorldState()
        {
            var wsMsg = new GameMessage
            {
                MsgType = GameMessageType.WorldStateMessage,
                WorldState = new WorldStateMsg
                {
                    ServerTick = _currentTick,
                    PlayerCount = GameConstants.MaxPlayers,
                    LastProcessedInputTicks = (int[])_lastProcessedInputTick.Clone()
                }
            };
            for (int i = 0; i < GameConstants.MaxPlayers; i++)
            {
                wsMsg.WorldState.Players.Add(ProtobufSerializer.ToPlayerSnapMsg(_playerSnapshots[i]));
            }

            byte[] payload = ProtobufSerializer.SerializeGameMessage(wsMsg);

            for (int i = 0; i < GameConstants.MaxPlayers; i++)
            {
                if (!_playerConnected[i]) continue;
                SendUnreliable(i, payload, payload.Length);
            }
        }

        private void SendReliable(int playerIdx, byte[] payload, float currentTime)
        {
            _connections[playerIdx].Session.SendReliable(payload);
            _connections[playerIdx].MarkReceived(currentTime);
            // KCP output is handled via its output callback → _transport.Send
        }

        private void SendUnreliable(int playerIdx, byte[] payload, int payloadLen)
        {
            byte[] packet = _connections[playerIdx].Session.Channel.WrapUnreliable(payload);
            _transport.Send(packet, packet.Length, _connections[playerIdx].RemoteEndPoint);
            _connections[playerIdx].Session.MarkUnreliableSent(packet);
        }

        private void ProcessReliables(float currentTime)
        {
            for (int i = 0; i < GameConstants.MaxPlayers; i++)
            {
                if (!_playerConnected[i]) continue;

                // Drive KCP session (update + heartbeat + timeout handled internally)
                _connections[i].Session.Update(currentTime);

                // Drain incoming reliable messages
                while (true)
                {
                    byte[] reliableMsg = _connections[i].Session.TryRecv();
                    if (reliableMsg == null) break;
                    var gameMsg = ProtobufSerializer.DeserializeGameMessage(reliableMsg);
                    DispatchReliableMessage(gameMsg, i, currentTime);
                }

                // Heartbeat handled by KcpSession internally
                if (_connections[i].Session.IsTimedOut)
                {
                    DebugLog($"Player {i} timed out, disconnecting");
                    HandlePlayerDisconnect(i, currentTime);
                }
            }
        }

        private void DispatchReliableMessage(GameMessage gameMsg, int playerIdx, float currentTime)
        {
            switch (gameMsg.MsgType)
            {
                case GameMessageType.Disconnect:
                    HandleDisconnect(playerIdx);
                    break;
                // ConnectionRequest, ConnectionAccepted, PlayerJoined, PlayerLeft, DamageEvent
                // are handled specifically; other reliable messages can be added here
            }
        }

        private void CheckTimeouts(float currentTime)
        {
            for (int i = 0; i < GameConstants.MaxPlayers; i++)
            {
                if (!_playerConnected[i]) continue;

                if (_connections[i].IsTimedOut(currentTime))
                {
                    DisconnectPlayer(i, "timed out");
                }
            }
        }

        private void ProcessFireRequests(float currentTime)
        {
            float tickInterval = 1f / _tickRate;

            for (int i = 0; i < GameConstants.MaxPlayers; i++)
            {
                if (!_playerConnected[i]) continue;
                if (_playerSnapshots[i].Health <= 0) continue; // dead players can't fire

                var input = _inputBuffers[i].Get(_currentTick);
                if (!input.Fire) continue;

                // Server-side fire rate enforcement: only fire if cooldown just reset this tick
                // (PlayerSimulation already set FireCooldown = FireRate when Fire && cooldown <= 0)
                // We detect a new shot by checking if the cooldown was just set (close to FireRate)
                if (_playerSnapshots[i].FireCooldown < GameConstants.FireRate - tickInterval)
                    continue;

                // Aim direction validation: pitch must be within [-90, 90] range
                float pitch = input.AimPitch;
                if (pitch < -90f || pitch > 90f)
                {
                    Log($"Player {i} invalid aim pitch: {pitch}");
                    continue;
                }

                // Compute lag-compensated tick: rewind by half RTT (one-way latency in ticks)
                float rtt = _connections[i].Rtt;
                int rewindTicks = (int)(rtt * 0.5f / tickInterval + 0.5f); // round to nearest
                rewindTicks = System.Math.Min(rewindTicks, GameConstants.MaxCompensationTicks);
                int compensatedTick = _currentTick - rewindTicks;

                // Retrieve historical world snapshot
                if (!_worldHistory.HasTick(compensatedTick))
                    continue;

                var historicalSnapshot = _worldHistory.Get(compensatedTick);

                // Compute fire direction from aim angles
                Quat aimRotation = Quat.Euler(pitch, input.AimYaw, 0f);
                Vec3 fireDirection = aimRotation * Vec3.Forward;
                fireDirection = fireDirection.Normalized;

                // Fire origin: player position + eye height offset
                Vec3 fireOrigin = _playerSnapshots[i].Position + new Vec3(0f, GameConstants.PlayerHeight * 0.85f, 0f);

                // Resolve hitscan against historical snapshot
                var hitResult = HitscanResolver.Resolve(fireOrigin, fireDirection, historicalSnapshot, (byte)i, GameConstants.HitscanRange);

                if (hitResult.Hit && _playerConnected[hitResult.TargetId] && _playerSnapshots[hitResult.TargetId].Health > 0)
                {
                    // Apply damage
                    byte damage = GameConstants.HitscanDamage;
                    byte oldHealth = _playerSnapshots[hitResult.TargetId].Health;
                    byte newHealth = oldHealth > damage ? (byte)(oldHealth - damage) : (byte)0;
                    _playerSnapshots[hitResult.TargetId].Health = newHealth;

                    Log($"Player {i} hit Player {hitResult.TargetId} for {damage} damage (health: {oldHealth} → {newHealth}, rewind: {rewindTicks} ticks)");

                    // Broadcast DamageEvent to all connected clients (reliable)
                    var dmgMsg = new GameMessage
                    {
                        MsgType = GameMessageType.DamageEvent,
                        DamageEvent = new DamageEventMsg
                        {
                            TargetId = hitResult.TargetId,
                            ShooterId = (byte)i,
                            Damage = damage,
                            NewHealth = newHealth,
                            HitPoint = hitResult.HitPoint
                        }
                    };
                    byte[] dmgPayload = ProtobufSerializer.SerializeGameMessage(dmgMsg);
                    for (int j = 0; j < GameConstants.MaxPlayers; j++)
                    {
                        if (!_playerConnected[j]) continue;
                        SendReliable(j, dmgPayload, currentTime);
                    }
                }
            }
        }

        private void ProcessRespawns(float tickInterval)
        {
            for (int i = 0; i < GameConstants.MaxPlayers; i++)
            {
                if (!_playerConnected[i]) continue;
                if (_playerSnapshots[i].Health > 0) continue;

                _respawnTimers[i] += tickInterval;
                if (_respawnTimers[i] >= GameConstants.RespawnDelay)
                {
                    _respawnTimers[i] = 0f;
                    _playerSnapshots[i] = PlayerSnapshot.Default(SpawnPositions[i]);
                    Log($"Player {i} respawned");
                }
            }
        }

        private int FindConnection(IPEndPoint remote)
        {
            for (int i = 0; i < GameConstants.MaxPlayers; i++)
            {
                if (_playerConnected[i] && _connections[i] != null &&
                    _connections[i].RemoteEndPoint.Equals(remote))
                    return i;
            }
            return -1;
        }

        private void Log(string message)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
        }
    }
}
