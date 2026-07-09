using ShootingGame.Shared.Math;
using ShootingGame.Shared.Protocol;
using ShootingGame.Shared.Simulation;
using Xunit;

namespace ShootingGame.Tests
{
    public class ProtocolTests
    {
        const float E = 0.001f;

        [Fact]
        public void MainPack_RoundTrip_Basic()
        {
            var pack = new MainPack
            {
                RequestCode = RequestCode.User,
                ActionCode = ActionCode.Login,
                ReturnCode = ReturnCode.Success,
                Str = "test_user",
                IntVal = 42
            };

            byte[] data = ProtobufSerializer.SerializeMainPack(pack);
            var result = ProtobufSerializer.DeserializeMainPack(data);

            Assert.Equal(RequestCode.User, result.RequestCode);
            Assert.Equal(ActionCode.Login, result.ActionCode);
            Assert.Equal(ReturnCode.Success, result.ReturnCode);
            Assert.Equal("test_user", result.Str);
            Assert.Equal(42, result.IntVal);
        }

        [Fact]
        public void MainPack_RoundTrip_UserInfo()
        {
            var pack = new MainPack
            {
                RequestCode = RequestCode.User,
                ActionCode = ActionCode.Login,
                UserInfo = new UserInfo
                {
                    UserId = 100,
                    Username = "PlayerOne"
                }
            };

            byte[] data = ProtobufSerializer.SerializeMainPack(pack);
            var result = ProtobufSerializer.DeserializeMainPack(data);

            Assert.NotNull(result.UserInfo);
            Assert.Equal(100, result.UserInfo.UserId);
            Assert.Equal("PlayerOne", result.UserInfo.Username);
        }

        [Fact]
        public void MainPack_RoundTrip_RoomInfo()
        {
            var pack = new MainPack
            {
                RequestCode = RequestCode.Matching,
                ActionCode = ActionCode.CreateRoom,
                RoomInfo = new RoomInfo
                {
                    RoomId = 5,
                    RoomName = "TestRoom",
                    MaxPlayers = 4
                }
            };

            byte[] data = ProtobufSerializer.SerializeMainPack(pack);
            var result = ProtobufSerializer.DeserializeMainPack(data);

            Assert.NotNull(result.RoomInfo);
            Assert.Equal(5, result.RoomInfo.RoomId);
            Assert.Equal("TestRoom", result.RoomInfo.RoomName);
            Assert.Equal(4, result.RoomInfo.MaxPlayers);
        }

        [Fact]
        public void MainPack_RoundTrip_BattleInfo()
        {
            var pack = new MainPack
            {
                RequestCode = RequestCode.Battle,
                ActionCode = ActionCode.BattleReady,
                BattleInfo = new BattleInfo
                {
                    BattleId = 99,
                    OperationId = 1
                }
            };

            byte[] data = ProtobufSerializer.SerializeMainPack(pack);
            var result = ProtobufSerializer.DeserializeMainPack(data);

            Assert.NotNull(result.BattleInfo);
            Assert.Equal(99, result.BattleInfo.BattleId);
        }

        [Fact]
        public void MainPackFrame_RoundTrip()
        {
            var pack = new MainPack
            {
                RequestCode = RequestCode.Matching,
                ActionCode = ActionCode.JoinQueue,
                Str = "hello"
            };

            byte[] frame = ProtobufSerializer.SerializeMainPackFrame(pack);
            // Frame has 4-byte BE length prefix + body
            Assert.True(frame.Length > 4);

            int length = (frame[0] << 24) | (frame[1] << 16) | (frame[2] << 8) | frame[3];
            Assert.Equal(frame.Length - 4, length);

            byte[] body = new byte[length];
            System.Buffer.BlockCopy(frame, 4, body, 0, length);
            var result = ProtobufSerializer.DeserializeMainPack(body);

            Assert.Equal(ActionCode.JoinQueue, result.ActionCode);
            Assert.Equal("hello", result.Str);
        }

        [Fact]
        public void GameMessage_InputBatch_RoundTrip()
        {
            var frames = new InputFrame[]
            {
                new InputFrame { Tick = 100, Movement = new Vec2(0.5f, -0.3f), Jump = true, Run = false, Aim = true, Fire = false, AimYaw = 45f, AimPitch = -10f },
                new InputFrame { Tick = 99, Movement = new Vec2(-1f, 0f), Jump = false, Run = true, Aim = false, Fire = true, AimYaw = 90f, AimPitch = 5f },
                new InputFrame { Tick = 98, Movement = Vec2.Zero, Jump = false, Run = false, Aim = false, Fire = false, AimYaw = 0f, AimPitch = 0f },
            };

            var batch = new InputBatchMsg();
            for (int i = 0; i < 3; i++)
                batch.Frames.Add(ProtobufSerializer.ToInputFrameMsg(frames[i]));

            var msg = new GameMessage
            {
                MsgType = GameMessageType.InputMessage,
                InputBatch = batch
            };

            byte[] data = ProtobufSerializer.SerializeGameMessage(msg);
            var result = ProtobufSerializer.DeserializeGameMessage(data);

            Assert.Equal(GameMessageType.InputMessage, result.MsgType);
            Assert.NotNull(result.InputBatch);
            Assert.NotNull(result.InputBatch.Frames);
            Assert.Equal(3, result.InputBatch.Frames.Count);

            var f0 = ProtobufSerializer.ToInputFrame(result.InputBatch.Frames[0]);
            Assert.Equal(100, f0.Tick);
            Assert.InRange(f0.Movement.x, 0.5f - E, 0.5f + E);
            Assert.True(f0.Jump);
            Assert.False(f0.Run);
            Assert.True(f0.Aim);
            Assert.False(f0.Fire);
            Assert.InRange(f0.AimYaw, 45f - E, 45f + E);

            var f1 = ProtobufSerializer.ToInputFrame(result.InputBatch.Frames[1]);
            Assert.Equal(99, f1.Tick);
            Assert.True(f1.Run);
            Assert.True(f1.Fire);

            Assert.Equal(98, ProtobufSerializer.ToInputFrame(result.InputBatch.Frames[2]).Tick);
        }

        [Fact]
        public void GameMessage_WorldState_RoundTrip()
        {
            var players = new PlayerSnapshot[]
            {
                new PlayerSnapshot
                {
                    Tick = 500, Position = new Vec3(10, 0, 5), Rotation = Quat.Identity,
                    Velocity = new Vec3(3, 0, 0), VerticalVelocity = -2f,
                    IsGrounded = true, State = PlayerStateEnum.Ground,
                    FireCooldown = 0.05f, Health = 75
                },
                new PlayerSnapshot
                {
                    Tick = 500, Position = new Vec3(-5, 2, 3), Rotation = Quat.Euler(0, 90, 0),
                    Velocity = Vec3.Zero, VerticalVelocity = 5f,
                    IsGrounded = false, State = PlayerStateEnum.Sky,
                    FireCooldown = 0f, Health = 100
                }
            };

            var ws = new WorldStateMsg
            {
                ServerTick = 500,
                LastProcessedInputTicks = new int[] { 497, 495 }
            };
            foreach (var p in players)
                ws.Players.Add(ProtobufSerializer.ToPlayerSnapMsg(p));

            var msg = new GameMessage
            {
                MsgType = GameMessageType.WorldStateMessage,
                WorldState = ws
            };

            byte[] data = ProtobufSerializer.SerializeGameMessage(msg);
            var result = ProtobufSerializer.DeserializeGameMessage(data);

            Assert.Equal(GameMessageType.WorldStateMessage, result.MsgType);
            Assert.Equal(500, result.WorldState.ServerTick);
            Assert.Equal(2, result.WorldState.Players.Count);
            Assert.Equal(497, result.WorldState.LastProcessedInputTicks[0]);
            Assert.Equal(495, result.WorldState.LastProcessedInputTicks[1]);

            var p0 = ProtobufSerializer.ToPlayerSnapshot(result.WorldState.Players[0]);
            Assert.InRange(p0.Position.x, 10f - E, 10f + E);
            Assert.True(p0.IsGrounded);
            Assert.Equal(PlayerStateEnum.Ground, p0.State);
            Assert.Equal((byte)75, p0.Health);

            var p1 = ProtobufSerializer.ToPlayerSnapshot(result.WorldState.Players[1]);
            Assert.False(p1.IsGrounded);
            Assert.Equal(PlayerStateEnum.Sky, p1.State);
        }

        [Fact]
        public void GameMessage_DamageEvent_RoundTrip()
        {
            var msg = new GameMessage
            {
                MsgType = GameMessageType.DamageEvent,
                DamageEvent = new DamageEventMsg
                {
                    TargetId = 1, ShooterId = 0, Damage = 25, NewHealth = 75,
                    HitPoint = new Vec3(5, 1, 3)
                }
            };

            byte[] data = ProtobufSerializer.SerializeGameMessage(msg);
            var result = ProtobufSerializer.DeserializeGameMessage(data);

            Assert.Equal(GameMessageType.DamageEvent, result.MsgType);
            Assert.Equal((byte)1, result.DamageEvent.TargetId);
            Assert.Equal((byte)0, result.DamageEvent.ShooterId);
            Assert.Equal((byte)25, result.DamageEvent.Damage);
            Assert.Equal((byte)75, result.DamageEvent.NewHealth);
            Assert.InRange(result.DamageEvent.HitPoint.x, 5f - E, 5f + E);
        }

        [Fact]
        public void GameMessage_ConnectionRequest_RoundTrip()
        {
            var msg = new GameMessage
            {
                MsgType = GameMessageType.ConnectionRequest,
                ConnectionRequest = new ConnectionRequestMsg { ProtocolVersion = 1 }
            };

            byte[] data = ProtobufSerializer.SerializeGameMessage(msg);
            var result = ProtobufSerializer.DeserializeGameMessage(data);

            Assert.Equal(GameMessageType.ConnectionRequest, result.MsgType);
            Assert.Equal((byte)1, result.ConnectionRequest.ProtocolVersion);
        }

        [Fact]
        public void GameMessage_ConnectionAccepted_RoundTrip()
        {
            var msg = new GameMessage
            {
                MsgType = GameMessageType.ConnectionAccepted,
                ConnectionAccepted = new ConnectionAcceptedMsg
                {
                    PlayerId = 0, TickRate = 60, ServerTick = 1234
                }
            };

            byte[] data = ProtobufSerializer.SerializeGameMessage(msg);
            var result = ProtobufSerializer.DeserializeGameMessage(data);

            Assert.Equal(GameMessageType.ConnectionAccepted, result.MsgType);
            Assert.Equal((byte)0, result.ConnectionAccepted.PlayerId);
            Assert.Equal(60, result.ConnectionAccepted.TickRate);
            Assert.Equal(1234, result.ConnectionAccepted.ServerTick);
        }

        [Fact]
        public void GameMessage_Disconnect_RoundTrip()
        {
            var msg = new GameMessage
            {
                MsgType = GameMessageType.Disconnect,
                Disconnect = new DisconnectMsg { Reason = 99 }
            };

            byte[] data = ProtobufSerializer.SerializeGameMessage(msg);
            var result = ProtobufSerializer.DeserializeGameMessage(data);

            Assert.Equal(GameMessageType.Disconnect, result.MsgType);
            Assert.Equal((byte)99, result.Disconnect.Reason);
        }

        [Fact]
        public void GameMessage_Heartbeat_RoundTrip()
        {
            var msg = new GameMessage
            {
                MsgType = GameMessageType.Heartbeat,
                Heartbeat = new HeartbeatMsg { Timestamp = 42000 }
            };

            byte[] data = ProtobufSerializer.SerializeGameMessage(msg);
            var result = ProtobufSerializer.DeserializeGameMessage(data);

            Assert.Equal(GameMessageType.Heartbeat, result.MsgType);
            Assert.Equal(42000, result.Heartbeat.Timestamp);
        }
    }
}
