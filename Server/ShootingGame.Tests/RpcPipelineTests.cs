using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using ShootingGame.Network;
using ShootingGame.Server;
using ShootingGame.Shared.Protocol;
using Xunit;

namespace ShootingGame.Tests
{
    /// <summary>
    /// RPC 链路端到端测试（B-② 路径 X）：
    /// 客户端经 UDP 发 MainPack{RpcCall, RpcPayload} → BattleUdpServer 路由
    /// → BattleRoom.HandleRpcCall 解析 NetId/MethodHash → 事件触发。
    /// </summary>
    public class RpcPipelineTests
    {
        private static int s_port = 28000;

        [Fact]
        public void RpcCall_FromUdpClient_ReachesBattleRoom()
        {
            int battlePort = s_port++;

            // 初始化英雄注册表（BattleRoom 构造需要）
            ShootingGame.Shared.Hero.HeroRegistry.Initialize();

            var ctx = new BattleContext
            {
                BattleId = 1,
                Mode = 0,
                Players = new System.Collections.Generic.List<MatchUserInfo>
                {
                    new MatchUserInfo { UserId = 101, Username = "A", TeamId = 1 },
                    new MatchUserInfo { UserId = 102, Username = "B", TeamId = 2 }
                }
            };
            ctx.UserIdToBattlePlayerId[101] = 0;
            ctx.UserIdToBattlePlayerId[102] = 1;

            var room = new BattleRoom(ctx, null);
            long receivedHash = 0;
            int receivedBp = -1;
            var signal = new ManualResetEventSlim(false);
            room.OnRpcCallReceived += (bpId, hash) =>
            {
                receivedBp = bpId;
                receivedHash = hash;
                signal.Set();
            };

            // 注册一个真实处理器：签名与客户端 [ServerRpc] 方法一致
            int dispatchedBp = -1;
            int dispatchedArg = 0;
            var dispatchSignal = new ManualResetEventSlim(false);
            room.RegisterRpcHandler("global::PlayerCombatBehaviour", "RequestShoot",
                new[] { "System.Single", "System.Single", "System.Single", "System.Int32" },
                (bpId, r) =>
                {
                    dispatchedBp = bpId;
                    dispatchedArg = r.ReadInt32();
                    dispatchSignal.Set();
                });

            var udp = new BattleUdpServer(battlePort);
            udp.RegisterBattle(room);
            udp.Start();

            try
            {
                var client = new UdpClient(0, AddressFamily.InterNetwork);
                var client2 = new UdpClient(0, AddressFamily.InterNetwork);
                var serverEp = new IPEndPoint(IPAddress.Loopback, battlePort);

                // 1. 两人房满员 BattleReady：绑定 endpoint → bpId 路由 + 启动房间
                var ready = new MainPack
                {
                    RequestCode = RequestCode.Battle,
                    ActionCode = ActionCode.BattleReady,
                    BattleInfo = new BattleInfo { BattleId = 1, OperationId = 0 }
                };
                client.Send(ProtobufSerializer.SerializeMainPack(ready), serverEp);

                var ready2 = new MainPack
                {
                    RequestCode = RequestCode.Battle,
                    ActionCode = ActionCode.BattleReady,
                    BattleInfo = new BattleInfo { BattleId = 1, OperationId = 1 }
                };
                client2.Send(ProtobufSerializer.SerializeMainPack(ready2), serverEp);

                // 等待房间启动
                int waited = 0;
                while (!room.IsStarted && waited < 50) { Thread.Sleep(20); waited++; }
                Assert.True(room.IsStarted, "房间未在超时内启动（需两人 BattleReady）");

                // 2. RpcCall：NetId(4) + MethodHash(8) + 参数
                const uint testNetId = 1001;
                // 签名: global::PlayerCombatBehaviour.RequestShoot(System.Single,System.Single,System.Single,System.Int32)
                long testHash = ShootingGame.Network.RpcMethodHash.Compute(
                    "global::PlayerCombatBehaviour.RequestShoot(System.Single,System.Single,System.Single,System.Int32)");
                var w = new PacketWriter();
                w.WriteUInt32(testNetId);
                w.WriteInt64(testHash);
                w.WriteUInt32(0); // reqId
                w.WriteInt32(42); // 客户端帧号参数

                var rpcPack = new MainPack
                {
                    RequestCode = RequestCode.Battle,
                    ActionCode = ActionCode.RpcCall,
                    RpcPayload = w.ToArray()
                };
                client.Send(ProtobufSerializer.SerializeMainPack(rpcPack), serverEp);

                Assert.True(signal.Wait(3000), "BattleRoom.HandleRpcCall 未在超时内触发");
                Assert.Equal(0, receivedBp);            // bpId 由 endpoint 反查
                Assert.Equal(testHash, receivedHash);   // methodHash 原样到达

                // 3. 验证按 methodHash 分发到注册处理器
                Assert.True(dispatchSignal.Wait(3000), "RPC 处理器未在超时内执行");
                Assert.Equal(0, dispatchedBp);
                Assert.Equal(42, dispatchedArg);        // 参数原样到达
            }
            finally
            {
                udp.Stop();
            }
        }

        [Fact]
        public void KcpReliable_Rpc_EndToEnd()
        {
            int battlePort = s_port++;
            ShootingGame.Shared.Hero.HeroRegistry.Initialize();

            var ctx = new BattleContext
            {
                BattleId = 4,
                Mode = 0,
                Players = new System.Collections.Generic.List<MatchUserInfo>
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
                var c0 = new UdpClient(0, AddressFamily.InterNetwork);
                var c1 = new UdpClient(0, AddressFamily.InterNetwork);
                var serverEp = new IPEndPoint(IPAddress.Loopback, battlePort);

                // 1. 两个客户端 BattleReady（原始 UDP）→ 建立 KCP 会话 + 启动房间
                foreach (var (client, bpId) in new[] { (c0, 0), (c1, 1) })
                {
                    client.Send(ProtobufSerializer.SerializeMainPack(new MainPack
                    {
                        RequestCode = RequestCode.Battle,
                        ActionCode = ActionCode.BattleReady,
                        BattleInfo = new BattleInfo { BattleId = 4, OperationId = bpId }
                    }), serverEp);
                }
                int waited = 0;
                while (!room.IsStarted && waited < 50) { Thread.Sleep(20); waited++; }
                Assert.True(room.IsStarted, "房间未启动");

                // 2. c0 建立 KCP 会话（conv = BattleId = 4），可靠发送技能 RPC
                var kcp0 = new KcpChannel(4u, (buf, len) => c0.Send(buf, len, serverEp));
                kcp0.SendReliable(ProtobufSerializer.SerializeMainPack(new MainPack
                {
                    RequestCode = RequestCode.Battle,
                    ActionCode = ActionCode.RpcCall,
                    RpcPayload = BuildAbilityRpcPayload(assetId: 1, predictedId: 9)
                }));

                // 3. 驱动客户端 KCP，等服务器回程（可靠通道）
                byte[] resp = null;
                var deadline = DateTime.UtcNow.AddSeconds(3);
                while (resp == null && DateTime.UtcNow < deadline)
                {
                    // 收服务器 UDP 包 → 喂给客户端 KCP
                    while (c0.Available > 0)
                    {
                        byte[] pkt = c0.Receive(ref serverEp);
                        kcp0.Input(pkt, pkt.Length, out _);
                    }
                    kcp0.Update((uint)Environment.TickCount);
                    kcp0.TryRecv(out resp);
                    if (resp == null) Thread.Sleep(10);
                }

                // 4. 断言：回程是 ConfirmAbility / RejectAbility（可靠到达）
                Assert.NotNull(resp);
                var pack = ProtobufSerializer.DeserializeMainPack(resp);
                Assert.Equal(ActionCode.RpcCall, pack.ActionCode);
                Assert.NotNull(pack.RpcPayload);

                var pr = new PacketReader(pack.RpcPayload);
                pr.ReadUInt32();
                long hash = pr.ReadInt64();
                long confirmHash = RpcMethodHash.Compute("global::PlayerCombatBehaviour.ConfirmAbility(System.Int32,System.Int32)");
                long rejectHash = RpcMethodHash.Compute("global::PlayerCombatBehaviour.RejectAbility(System.Int32)");
                Assert.True(hash == confirmHash || hash == rejectHash,
                    $"回程应为 Confirm/Reject，实际 0x{hash:X}");

                // 5. predictedId 贯穿验证（9 原样回传）
                pr.ReadUInt32(); // reqId
                int echoedPredicted = pr.ReadInt32();
                Assert.Equal(9, echoedPredicted);
            }
            finally
            {
                udp.Stop();
            }
        }

        private static byte[] BuildAbilityRpcPayload(int assetId, int predictedId)
        {
            var w = new PacketWriter();
            w.WriteUInt32(0); // NetId
            w.WriteInt64(RpcMethodHash.Compute("global::PlayerCombatBehaviour.RequestActivateAbility(System.Int32,System.Int32)"));
            w.WriteUInt32(0); // reqId
            w.WriteInt32(assetId);
            w.WriteInt32(predictedId);
            return w.ToArray();
        }

        [Fact]
        public void RpcCall_MalformedPayload_DoesNotCrash()
        {
            var room = new BattleRoom(new BattleContext { BattleId = 2, Mode = 0 }, null);
            room.ForceStart(); // 启动房间，避免 _hasStarted 短路
            // 短负载（<12B）应被安全忽略，不抛异常
            room.HandleRpcCall(0, new byte[] { 1, 2, 3 });
        }
    }
}
