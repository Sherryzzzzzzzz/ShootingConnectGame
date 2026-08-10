using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using ShootingGame.Shared.Math;
using ShootingGame.Shared.Protocol;
using ShootingGame.Shared.Simulation;
using ShootingGame.Server;
using Xunit;

namespace ShootingGame.Tests
{
    /// <summary>
    /// 直接测试 BattleUdpServer + BattleRoom 管线：
    /// BattleReady → BattleStart → 输入上行 → 帧广播下行
    /// </summary>
    public class BattleRoomPipelineTests
    {
        private static int _port = 41000;

        [Fact]
        public void BattleReady_ThenOperations_FramesBroadcast()
        {
            int battlePort = _port++;

            // 初始化英雄注册表（否则 BattleRoom 构造时 heroConfig 为 null）
            ShootingGame.Shared.Hero.HeroRegistry.Initialize();

            // 1. 构造一个 2 人 battle context
            var ctx = new BattleContext
            {
                BattleId = 1,
                Mode = 0,
                Players = new List<MatchUserInfo>
                {
                    new MatchUserInfo { UserId = 101, Username = "A", TeamId = 1 },
                    new MatchUserInfo { UserId = 102, Username = "B", TeamId = 2 }
                }
            };
            ctx.UserIdToBattlePlayerId[101] = 0;
            ctx.UserIdToBattlePlayerId[102] = 1;

            var room = new BattleRoom(ctx, null);
            var udp = new BattleUdpServer(battlePort);
            udp.RegisterBattle(room);
            udp.Start();

            try
            {
                // 2. 两个 UDP 假客户端
                var c0 = new UdpClient(0, AddressFamily.InterNetwork);
                var c1 = new UdpClient(0, AddressFamily.InterNetwork);
                var serverEp = new IPEndPoint(IPAddress.Loopback, battlePort);

                bool c0Started = false, c1Started = false;
                int c0Frames = 0, c1Frames = 0;
                int c0States = 0, c1States = 0;

                var t0 = new Thread(() => Pump(c0, ref c0Started, ref c0Frames, ref c0States));
                var t1 = new Thread(() => Pump(c1, ref c1Started, ref c1Frames, ref c1States));
                t0.IsBackground = true; t1.IsBackground = true;
                t0.Start(); t1.Start();

                // 3. 发送 BattleReady
                SendReady(c0, serverEp, 1, 0);
                SendReady(c1, serverEp, 1, 1);

                Thread.Sleep(300);
                Assert.True(c0Started, "C0 should get BattleStart");
                Assert.True(c1Started, "C1 should get BattleStart");

                // 4. 发送操作
                for (int i = 0; i < 30; i++)
                {
                    SendOp(c0, serverEp, 1, 0, i, 1f, 0f, 10f + i, 0f, 0f);
                    SendOp(c1, serverEp, 1, 1, i, -1f, 0f, -10f - i, 0f, 0f);
                    Thread.Sleep(16); // ~60Hz
                }

                Thread.Sleep(500);

                Assert.True(c0Frames > 0, $"C0 should receive frames, got {c0Frames}");
                Assert.True(c1Frames > 0, $"C1 should receive frames, got {c1Frames}");
                Assert.True(c0States >= 1, $"C0 frame should have states, got {c0States}");

                c0.Close(); c1.Close();
            }
            finally
            {
                udp.Stop();
            }
        }

        private static void Pump(UdpClient c, ref bool started, ref int frames, ref int states)
        {
            try
            {
                var ep = new IPEndPoint(IPAddress.Any, 0);
                while (true)
                {
                    var data = c.Receive(ref ep);
                    var pack = ProtobufSerializer.DeserializeMainPack(data);
                    if (pack.ActionCode == ActionCode.BattleStart) started = true;
                    else if (pack.ActionCode == ActionCode.BattleFrame)
                    {
                        frames++;
                        if (pack.BattleInfo?.AllPlayerOperations?.Count > 0)
                            states = Math.Max(states, pack.BattleInfo.AllPlayerOperations[0].PlayerStates?.Count ?? 0);
                    }
                }
            }
            catch { }
        }

        private static void SendReady(UdpClient c, IPEndPoint ep, int battleId, int bpId)
        {
            var pack = new MainPack
            {
                RequestCode = RequestCode.Battle,
                ActionCode = ActionCode.BattleReady,
                BattleInfo = new BattleInfo { BattleId = battleId, OperationId = bpId }
            };
            var b = ProtobufSerializer.SerializeMainPack(pack);
            c.Send(b, b.Length, ep);
        }

        private static void SendOp(UdpClient c, IPEndPoint ep, int battleId, int bpId, int frame, float mx, float my, float px, float py, float pz)
        {
            var op = new PlayerOperation
            {
                PlayerId = bpId,
                MoveX = mx, MoveY = my,
                AimYaw = 90, AimPitch = 0,
                PosX = px, PosY = py, PosZ = pz,
                VelX = mx, VelZ = my, IsGrounded = true
            };
            var pack = new MainPack
            {
                RequestCode = RequestCode.Battle,
                ActionCode = ActionCode.BattleOperation,
                BattleInfo = new BattleInfo
                {
                    BattleId = battleId,
                    OperationId = frame,
                    ClientAckedFrame = 0,
                    SelfOperation = op
                }
            };
            var b = ProtobufSerializer.SerializeMainPack(pack);
            c.Send(b, b.Length, ep);
        }
    }
}
