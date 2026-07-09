using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using ShootingGame.Shared.Math;
using ShootingGame.Shared.Protocol;
using ShootingGame.Shared.Simulation;
using Xunit;

namespace ShootingGame.Tests
{
    public class EndToEndTests
    {
        /// <summary>
        /// Two fake clients connect. Player 0 aims at Player 1 and fires.
        /// Verifies that a DamageEvent is received by both clients.
        /// </summary>
        [Fact]
        public void PlayerA_ShootsPlayerB_DamageEventReceived()
        {
            int port = 17778;
            var server = new ShootingGame.Server.GameServer(port: port);
            var serverTask = Task.Run(() =>
            {
                try { server.Run(null); }
                catch { }
            });

            Thread.Sleep(200);

            try
            {
                // --- Connect Player 0 ---
                using var t0 = new UdpTransport();
                t0.Start(0);
                var ep = new IPEndPoint(IPAddress.Loopback, port);

                uint conv0 = 1001;
                var kcp0 = new KcpChannel(conv0, (data, len) => t0.Send(data, len, ep));
                SendConnectionRequest(kcp0);
                byte id0 = 255;
                int tick0 = 0;
                PumpUntilAccepted(t0, kcp0, out id0, out tick0);
                Assert.True(id0 < 2, "Player 0 should be accepted");

                // --- Connect Player 1 ---
                using var t1 = new UdpTransport();
                t1.Start(0);

                uint conv1 = 2002;
                var kcp1 = new KcpChannel(conv1, (data, len) => t1.Send(data, len, ep));
                SendConnectionRequest(kcp1);
                byte id1 = 255;
                int tick1 = 0;
                PumpUntilAccepted(t1, kcp1, out id1, out tick1);
                Assert.True(id1 < 2, "Player 1 should be accepted");
                Assert.NotEqual(id0, id1);

                // Let server run a few ticks so world history populates
                Thread.Sleep(200);

                // Drain any queued messages
                DrainKcp(t0, kcp0);
                DrainKcp(t1, kcp1);

                // --- Player 0 aims at Player 1's spawn position and fires ---
                Vec3 p0Pos = new Vec3(0, 0, 0);
                Vec3 p1Pos = new Vec3(5, 0, 5);
                float eyeHeight = GameConstants.PlayerHeight * 0.85f;
                Vec3 aimDir = (p1Pos + new Vec3(0, eyeHeight, 0)) - (p0Pos + new Vec3(0, eyeHeight, 0));
                aimDir = aimDir.Normalized;

                float yaw = GameMath.Atan2(aimDir.x, aimDir.z) * GameMath.Rad2Deg;
                float pitch = GameMath.Asin(-aimDir.y) * GameMath.Rad2Deg;

                // Send fire input from player 0 for several ticks
                int baseTick = tick0 + 20;
                for (int i = 0; i < 20; i++)
                {
                    SendInputFrame(t0, kcp0, ep, new InputFrame
                    {
                        Tick = baseTick + i, Movement = new Vec2(0, 0),
                        Aim = true, Fire = true, AimYaw = yaw, AimPitch = pitch,
                    });
                    Thread.Sleep(17);
                }

                // Also keep player 1 sending idle input
                for (int i = 0; i < 20; i++)
                {
                    SendInputFrame(t1, kcp1, ep, new InputFrame
                    {
                        Tick = tick1 + 20 + i, Movement = new Vec2(0, 0),
                    });
                }

                // --- Wait for DamageEvent on either transport ---
                bool damageReceived0 = false;
                bool damageReceived1 = false;
                byte damagedTarget = 255;
                byte damagedNewHealth = 255;

                for (int attempt = 0; attempt < 100; attempt++)
                {
                    Thread.Sleep(20);
                    uint now = (uint)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                    // Check player 0
                    while (t0.TryReceive(out var recv))
                    {
                        kcp0.Input(recv.Data, recv.Length, out _);
                    }
                    kcp0.Update(now);
                    while (kcp0.TryRecv(out var reliableMsg))
                    {
                        var gameMsg = ProtobufSerializer.DeserializeGameMessage(reliableMsg);
                        if (gameMsg.MsgType == GameMessageType.DamageEvent)
                        {
                            damagedTarget = gameMsg.DamageEvent.TargetId;
                            damagedNewHealth = gameMsg.DamageEvent.NewHealth;
                            damageReceived0 = true;
                        }
                    }

                    // Check player 1
                    while (t1.TryReceive(out var recv))
                    {
                        kcp1.Input(recv.Data, recv.Length, out _);
                    }
                    kcp1.Update(now);
                    while (kcp1.TryRecv(out var reliableMsg))
                    {
                        var gameMsg = ProtobufSerializer.DeserializeGameMessage(reliableMsg);
                        if (gameMsg.MsgType == GameMessageType.DamageEvent)
                        {
                            damagedTarget = gameMsg.DamageEvent.TargetId;
                            damagedNewHealth = gameMsg.DamageEvent.NewHealth;
                            damageReceived1 = true;
                        }
                    }

                    if (damageReceived0 && damageReceived1) break;
                }

                Assert.True(damageReceived0, "Player 0 should receive DamageEvent");
                Assert.True(damageReceived1, "Player 1 should receive DamageEvent");
                Assert.Equal(id1, damagedTarget);
                Assert.True(damagedNewHealth < GameConstants.MaxHealth, $"Player 1 health should decrease (got {damagedNewHealth})");
            }
            finally
            {
                server.Stop();
                serverTask.Wait(2000);
            }
        }

        [Fact]
        public void ClientConnects_SendsInput_ReceivesWorldState()
        {
            int port = 17777;
            var server = new ShootingGame.Server.GameServer(port: port);

            var cts = new CancellationTokenSource();
            var serverTask = Task.Run(() =>
            {
                try { server.Run(null); }
                catch { }
            });

            Thread.Sleep(200);

            try
            {
                // Create a fake client
                using var transport = new UdpTransport();
                transport.Start(0);
                var serverEp = new IPEndPoint(IPAddress.Loopback, port);

                uint conv = 42;
                var kcp = new KcpChannel(conv, (data, len) => transport.Send(data, len, serverEp));

                // 1. Send ConnectionRequest
                SendConnectionRequest(kcp);

                // 2. Wait for ConnectionAccepted
                byte playerId = 255;
                int serverTick = 0;
                PumpUntilAccepted(transport, kcp, out playerId, out serverTick);

                Assert.True(playerId < 2, "Player ID should be 0 or 1");
                Assert.True(serverTick > 0, "Server tick should be positive");

                // 3. Send some input (move forward)
                for (int i = 0; i < 10; i++)
                {
                    SendInputFrame(transport, kcp, serverEp, new InputFrame
                    {
                        Tick = serverTick + i,
                        Movement = new Vec2(0, 1), // forward
                    });
                    Thread.Sleep(17);
                }

                // 4. Wait and collect world states
                bool receivedWorldState = false;
                Vec3 lastPosition = Vec3.Zero;

                for (int attempt = 0; attempt < 50; attempt++)
                {
                    Thread.Sleep(20);
                    uint now = (uint)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                    while (transport.TryReceive(out var recv))
                    {
                        if (kcp.Input(recv.Data, recv.Length, out byte[] unreliablePayload))
                        {
                            // Unreliable — check for WorldState
                            if (unreliablePayload != null)
                            {
                                var gameMsg = ProtobufSerializer.DeserializeGameMessage(unreliablePayload);
                                if (gameMsg.MsgType == GameMessageType.WorldStateMessage)
                                {
                                    var ws = gameMsg.WorldState;
                                    if (ws.Players.Count > playerId)
                                    {
                                        var snap = ProtobufSerializer.ToPlayerSnapshot(ws.Players[playerId]);
                                        lastPosition = snap.Position;
                                        receivedWorldState = true;
                                    }
                                }
                            }
                        }
                    }
                    kcp.Update(now);
                    if (receivedWorldState) break;
                }

                Assert.True(receivedWorldState, "Should receive at least one WorldStateMessage");
                Assert.True(lastPosition.z > 0.01f || lastPosition.x != 0f || lastPosition.y != 0f,
                    $"Player should have moved. Position: {lastPosition}");
            }
            finally
            {
                server.Stop();
                serverTask.Wait(2000);
            }
        }

        // ---- Helpers ----

        private static readonly GameMessage s_connReqMsg = new GameMessage
        {
            MsgType = GameMessageType.ConnectionRequest,
            ConnectionRequest = new ConnectionRequestMsg { ProtocolVersion = 1 }
        };
        private static readonly byte[] s_connReqPayload = ProtobufSerializer.SerializeGameMessage(s_connReqMsg);

        private static void SendConnectionRequest(KcpChannel kcp)
        {
            kcp.SendReliable(s_connReqPayload);
            uint now = (uint)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            kcp.Update(now);
        }

        private static void SendInputFrame(UdpTransport transport, KcpChannel kcp, IPEndPoint ep, InputFrame frame)
        {
            var batch = new InputBatchMsg();
            batch.Frames.Add(ProtobufSerializer.ToInputFrameMsg(frame));
            var msg = new GameMessage
            {
                MsgType = GameMessageType.InputMessage,
                InputBatch = batch
            };
            byte[] payload = ProtobufSerializer.SerializeGameMessage(msg);
            byte[] packet = kcp.WrapUnreliable(payload);
            transport.Send(packet, packet.Length, ep);
        }

        private static void PumpUntilAccepted(UdpTransport transport, KcpChannel kcp, out byte playerId, out int serverTick)
        {
            playerId = 255;
            serverTick = 0;

            for (int attempt = 0; attempt < 50; attempt++)
            {
                Thread.Sleep(20);
                uint now = (uint)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                while (transport.TryReceive(out var recv))
                {
                    kcp.Input(recv.Data, recv.Length, out _);
                }
                kcp.Update(now);
                while (kcp.TryRecv(out var reliableMsg))
                {
                    var gameMsg = ProtobufSerializer.DeserializeGameMessage(reliableMsg);
                    if (gameMsg.MsgType == GameMessageType.ConnectionAccepted)
                    {
                        playerId = gameMsg.ConnectionAccepted.PlayerId;
                        serverTick = gameMsg.ConnectionAccepted.ServerTick;
                        return;
                    }
                }
            }
        }

        private static void DrainKcp(UdpTransport transport, KcpChannel kcp)
        {
            uint now = (uint)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            while (transport.TryReceive(out var recv))
            {
                kcp.Input(recv.Data, recv.Length, out _);
            }
            kcp.Update(now);
            while (kcp.TryRecv(out _)) { }
        }
    }
}
