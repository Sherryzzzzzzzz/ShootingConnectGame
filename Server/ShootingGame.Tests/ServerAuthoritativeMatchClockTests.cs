using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ShootingGame.Server;
using ShootingGame.Shared.Hero;
using ShootingGame.Shared.Protocol;
using ShootingGame.Shared.Simulation;
using Xunit;

namespace ShootingGame.Tests
{
    public sealed class ServerAuthoritativeMatchClockTests
    {
        [Fact]
        public void BattleFrame_SendsSameDecreasingRemainingTicksToEveryClient()
        {
            HeroRegistry.Initialize();
            var room = CreateRoom(timeLimit: 1f);
            var frames = new List<(string Endpoint, int FrameId, long RemainingTicks)>();
            room.OnSendPacket += (endpoint, pack) =>
            {
                if (pack.ActionCode != ActionCode.BattleFrame)
                    return;
                lock (frames)
                    frames.Add((endpoint, pack.BattleInfo.OperationId, pack.Timestamp));
            };

            try
            {
                room.HandleBattleReady(0, "client-a");
                room.HandleBattleReady(1, "client-b");
                Assert.True(SpinWait.SpinUntil(() =>
                {
                    lock (frames)
                        return frames.Select(frame => frame.FrameId).Distinct().Count() >= 3;
                }, TimeSpan.FromSeconds(2)));

                lock (frames)
                {
                    foreach (var group in frames.GroupBy(frame => frame.FrameId))
                    {
                        Assert.Equal(2, group.Count());
                        Assert.Single(group.Select(frame => frame.RemainingTicks).Distinct());
                    }

                    var remaining = frames
                        .Where(frame => frame.Endpoint == "client-a")
                        .OrderBy(frame => frame.FrameId)
                        .Select(frame => frame.RemainingTicks)
                        .ToArray();
                    Assert.True(remaining.Length >= 3);
                    Assert.InRange(remaining[0], 1, (long)Math.Ceiling(1f / GameConstants.TickDelta));
                    Assert.True(remaining[1] < remaining[0]);
                    Assert.True(remaining[2] < remaining[1]);
                }
            }
            finally
            {
                room.Stop();
            }
        }

        [Fact]
        public void TeamBattle_TimeLimitEndsMatchAndNeverExceedsThreeHundredSeconds()
        {
            HeroRegistry.Initialize();
            var room = CreateRoom(timeLimit: 0.05f);
            var packets = new List<MainPack>();
            room.OnSendPacket += (_, pack) =>
            {
                lock (packets)
                    packets.Add(pack);
            };

            try
            {
                room.HandleBattleReady(0, "client-a");
                room.HandleBattleReady(1, "client-b");
                Assert.True(SpinWait.SpinUntil(() =>
                {
                    lock (packets)
                        return packets.Any(pack => pack.ActionCode == ActionCode.GameOver);
                }, TimeSpan.FromSeconds(2)));

                lock (packets)
                {
                    var firstFrame = packets.First(pack => pack.ActionCode == ActionCode.BattleFrame);
                    Assert.InRange(firstFrame.Timestamp, 1, (long)Math.Ceiling(GameConstants.MatchDurationSeconds / GameConstants.TickDelta));
                    Assert.Contains(packets, pack => pack.ActionCode == ActionCode.GameOver);
                }
            }
            finally
            {
                room.Stop();
            }
        }

        private static BattleRoom CreateRoom(float timeLimit)
        {
            var context = new BattleContext
            {
                BattleId = 9101,
                Mode = 0,
                TimeLimit = timeLimit,
                Players = new List<MatchUserInfo>
                {
                    new MatchUserInfo { UserId = 9101, Username = "A", TeamId = 1 },
                    new MatchUserInfo { UserId = 9102, Username = "B", TeamId = 2 }
                }
            };
            context.UserIdToBattlePlayerId[9101] = 0;
            context.UserIdToBattlePlayerId[9102] = 1;
            return new BattleRoom(context, null);
        }
    }
}
