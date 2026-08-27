using System;
using System.Collections.Generic;
using ShootingGame.Shared.Protocol;
using ShootingGame.Shared.Simulation;

namespace ShootingGame.Server
{
    internal static class ServerMatchClock
    {
        public static int GetRemainingTicks(int frameId, float configuredTimeLimit, float frameInterval)
        {
            float configuredSeconds = configuredTimeLimit > 0f
                ? configuredTimeLimit
                : GameConstants.MatchDurationSeconds;
            float durationSeconds = Math.Min(configuredSeconds, GameConstants.MatchDurationSeconds);
            int durationTicks = Math.Max(1, (int)Math.Ceiling(durationSeconds / frameInterval));
            int elapsedTicks = Math.Max(0, frameId - 1);
            return Math.Max(0, durationTicks - elapsedTicks);
        }

        public static void ResolveTeamTimeLimitWinner(
            IReadOnlyDictionary<int, AllPlayerOperation> frameHistory,
            int frameId,
            BattleContext context,
            out int winnerPlayerId,
            out int winnerTeamId)
        {
            winnerPlayerId = -1;
            winnerTeamId = 0;
            int bestPlayerKills = int.MinValue;
            var teamKills = new Dictionary<int, int>();

            if (frameHistory.TryGetValue(frameId - 1, out var latestFrame))
            {
                foreach (var state in latestFrame.PlayerStates)
                {
                    int teamId = GetContextTeamId(context, state.PlayerId);
                    if (teamId <= 0)
                        continue;

                    teamKills[teamId] = teamKills.GetValueOrDefault(teamId) + state.Kills;
                    if (state.Kills > bestPlayerKills
                        || (state.Kills == bestPlayerKills && (winnerPlayerId < 0 || state.PlayerId < winnerPlayerId)))
                    {
                        bestPlayerKills = state.Kills;
                        winnerPlayerId = state.PlayerId;
                    }
                }
            }

            int bestTeamKills = int.MinValue;
            foreach (var score in teamKills)
            {
                if (score.Value > bestTeamKills
                    || (score.Value == bestTeamKills && (winnerTeamId == 0 || score.Key < winnerTeamId)))
                {
                    bestTeamKills = score.Value;
                    winnerTeamId = score.Key;
                }
            }

            if (winnerTeamId != 0)
                return;

            foreach (var player in context.Players)
            {
                int bpId = context.GetBattlePlayerId(player.UserId);
                if (winnerPlayerId < 0 || bpId < winnerPlayerId)
                {
                    winnerPlayerId = bpId;
                    winnerTeamId = player.TeamId;
                }
            }
        }

        private static int GetContextTeamId(BattleContext context, int bpId)
        {
            foreach (var player in context.Players)
            {
                if (context.GetBattlePlayerId(player.UserId) == bpId)
                    return player.TeamId;
            }
            return 0;
        }
    }
}
