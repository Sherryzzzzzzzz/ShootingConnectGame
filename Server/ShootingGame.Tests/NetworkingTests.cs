using System.Net;
using System.Threading;
using ShootingGame.Shared.Protocol;
using Xunit;

namespace ShootingGame.Tests
{
    public class NetworkingTests
    {
        [Fact]
        public void UdpTransport_SendReceive_Localhost()
        {
            using var server = new UdpTransport();
            using var client = new UdpTransport();

            server.Start(0); // random port
            client.Start(0);

            int serverPort = server.LocalPort;
            var serverEp = new IPEndPoint(IPAddress.Loopback, serverPort);

            // Client sends to server
            byte[] data = new byte[] { 1, 2, 3, 4, 5 };
            client.Send(data, data.Length, serverEp);

            Thread.Sleep(50); // wait for delivery

            Assert.True(server.TryReceive(out var packet));
            Assert.Equal(5, packet.Length);
            Assert.Equal(1, packet.Data[0]);
            Assert.Equal(5, packet.Data[4]);
        }

        [Fact]
        public void KcpChannel_ReliableSendReceive()
        {
            byte[] received = null;
            KcpChannel receiver = null;

            var sender = new KcpChannel(1, (data, len) =>
            {
                receiver.Input(data, len, out _);
            });
            receiver = new KcpChannel(1, (data, len) =>
            {
                sender.Input(data, len, out _);
            });

            byte[] payload = new byte[] { 10, 20, 30 };
            sender.SendReliable(payload);

            uint now = 100;
            for (int i = 0; i < 100; i++)
            {
                now += 10;
                sender.Update(now);
                receiver.Update(now);
                if (receiver.TryRecv(out received)) break;
            }

            Assert.NotNull(received);
            Assert.Equal(3, received.Length);
            Assert.Equal(10, received[0]);
            Assert.Equal(20, received[1]);
            Assert.Equal(30, received[2]);
        }

        [Fact]
        public void KcpChannel_UnreliableWrapAndExtract()
        {
            var sender = new KcpChannel(42, (_, _) => { });
            var receiver = new KcpChannel(42, (_, _) => { });

            byte[] payload = new byte[] { 1, 2, 3 };
            byte[] packet = sender.WrapUnreliable(payload);

            Assert.True(packet.Length >= KcpChannel.UnreliableHeaderSize + 3);
            Assert.Equal(KcpChannel.UnreliableMarker, packet[0]);

            bool ok = receiver.Input(packet, packet.Length, out byte[] extracted);
            Assert.True(ok);
            Assert.NotNull(extracted);
            Assert.Equal(3, extracted.Length);
            Assert.Equal(1, extracted[0]);
            Assert.Equal(3, extracted[2]);
        }

        [Fact]
        public void KcpChannel_UnreliableWrongConv_Ignored()
        {
            var receiver = new KcpChannel(99, (_, _) => { });
            var sender = new KcpChannel(100, (_, _) => { });

            byte[] payload = new byte[] { 42 };
            byte[] packet = sender.WrapUnreliable(payload);

            bool ok = receiver.Input(packet, packet.Length, out byte[] extracted);
            Assert.False(ok);
            Assert.Null(extracted);
        }

        [Fact]
        public void KcpChannel_ReliableRetransmitsWhenDropped()
        {
            // Simulate packet loss: sender's first output is dropped, retransmit should still deliver
            byte[] received = null;
            bool firstOutputDropped = false;
            KcpChannel receiver = null;

            var sender = new KcpChannel(10, (data, len) =>
            {
                if (!firstOutputDropped)
                {
                    firstOutputDropped = true;
                    return;
                }
                receiver.Input(data, len, out _);
            });
            receiver = new KcpChannel(10, (data, len) =>
            {
                sender.Input(data, len, out _);
            });

            byte[] payload = new byte[] { 7, 8, 9 };
            sender.SendReliable(payload);

            uint now = 100;
            for (int i = 0; i < 500; i++)
            {
                now += 10;
                sender.Update(now);
                receiver.Update(now);
                if (receiver.TryRecv(out received)) break;
            }

            Assert.NotNull(received);
            Assert.Equal(3, received.Length);
            Assert.Equal(7, received[0]);
            Assert.True(firstOutputDropped, "First output should have been dropped");
        }

        [Fact]
        public void FullRoundTrip_KcpChannel()
        {
            // Two KcpChannels simulating client and server with in-memory pipe
            byte[] serverReceived = null;
            byte[] clientReceived = null;
            KcpChannel server = null;

            uint conv = 77;

            var client = new KcpChannel(conv, (data, len) =>
            {
                server.Input(data, len, out _);
            });
            server = new KcpChannel(conv, (data, len) =>
            {
                client.Input(data, len, out _);
            });

            // Client sends ConnectionRequest via reliable
            var connReq = new GameMessage
            {
                MsgType = GameMessageType.ConnectionRequest,
                ConnectionRequest = new ConnectionRequestMsg { ProtocolVersion = 1 }
            };
            byte[] reqPayload = ProtobufSerializer.SerializeGameMessage(connReq);
            client.SendReliable(reqPayload);

            // Pump both sides
            uint now = 100;
            for (int i = 0; i < 200; i++)
            {
                now += 10;
                client.Update(now);
                server.Update(now);
                if (serverReceived == null && server.TryRecv(out serverReceived)) break;
            }

            Assert.NotNull(serverReceived);
            var parsed = ProtobufSerializer.DeserializeGameMessage(serverReceived);
            Assert.Equal(GameMessageType.ConnectionRequest, parsed.MsgType);
            Assert.Equal((byte)1, parsed.ConnectionRequest.ProtocolVersion);

            // Server sends ConnectionAccepted
            var connAcc = new GameMessage
            {
                MsgType = GameMessageType.ConnectionAccepted,
                ConnectionAccepted = new ConnectionAcceptedMsg
                {
                    PlayerId = 0, TickRate = 60, ServerTick = 100
                }
            };
            byte[] accPayload = ProtobufSerializer.SerializeGameMessage(connAcc);
            server.SendReliable(accPayload);

            for (int i = 0; i < 200; i++)
            {
                now += 10;
                client.Update(now);
                server.Update(now);
                if (clientReceived == null && client.TryRecv(out clientReceived)) break;
            }

            Assert.NotNull(clientReceived);
            var accMsg = ProtobufSerializer.DeserializeGameMessage(clientReceived);
            Assert.Equal(GameMessageType.ConnectionAccepted, accMsg.MsgType);
            Assert.Equal((byte)0, accMsg.ConnectionAccepted.PlayerId);
            Assert.Equal(60, accMsg.ConnectionAccepted.TickRate);
            Assert.Equal(100, accMsg.ConnectionAccepted.ServerTick);
        }
    }
}
