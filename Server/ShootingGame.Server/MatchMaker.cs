using System;
using System.Collections.Generic;
using System.Threading;
using ShootingGame.Shared.Protocol;
using ShootingGame.Shared.Simulation;

namespace ShootingGame.Server
{
    /// <summary>
    /// Match user info for matchmaking.
    /// </summary>
    public class MatchUserInfo
    {
        public int UserId;
        public string Username;
        public int TeamId;
        public int HeroId;
        public int Rating;
        public DateTime JoinTime;
        public LobbyClient Client;
    }

    /// <summary>
    /// Battle context for a matched game.
    /// </summary>
    public class BattleContext
    {
        public int BattleId;
        public List<MatchUserInfo> Players = new List<MatchUserInfo>();
        public Dictionary<int, int> UserIdToBattlePlayerId = new Dictionary<int, int>();
        public int RandSeed;
        public List<SpawnPoint> SpawnPoints = new List<SpawnPoint>();
        public string CollisionDataPath;

        public int GetBattlePlayerId(int userId)
        {
            return UserIdToBattlePlayerId.TryGetValue(userId, out int bpId) ? bpId : -1;
        }
    }

    /// <summary>
    /// MatchMaker handles player matching and battle creation.
    /// </summary>
    public class MatchMaker
    {
        private readonly int _playersPerMatch;
        private readonly int _teamsPerMatch;
        private readonly object _lock = new object();
        private List<SpawnPoint> _spawnPoints;
        private string _collisionDataPath;

        // Matching queue
        private readonly List<MatchUserInfo> _queue = new List<MatchUserInfo>();

        // Active battles
        private readonly Dictionary<int, BattleRoom> _activeBattles = new Dictionary<int, BattleRoom>();
        private readonly Dictionary<int, int> _userToBattleId = new Dictionary<int, int>();
        private int _nextBattleId = 1;

        // Events
        public event Action<BattleContext> OnMatchFound;
        public event Action<int> OnBattleEnded; // battleId

        public IReadOnlyDictionary<int, BattleRoom> ActiveBattles => _activeBattles;

        public MatchMaker(int playersPerMatch = 2, int teamsPerMatch = 2)
        {
            _playersPerMatch = playersPerMatch;
            _teamsPerMatch = teamsPerMatch;
        }

        public void SetSpawnPoints(List<SpawnPoint> spawnPoints)
        {
            _spawnPoints = spawnPoints;
        }

        public void SetCollisionDataPath(string path)
        {
            _collisionDataPath = path;
        }

        /// <summary>
        /// Add a player to the matching queue.
        /// Automatically cleans up stale battle entries from previous failed matches.
        /// </summary>
        public bool JoinQueue(LobbyClient client, int teamId = 0, int heroId = 0)
        {
            lock (_lock)
            {
                if (_queue.Exists(p => p.UserId == client.UserId))
                {
                    Log($"Player {client.UserId} already in queue, ignoring duplicate");
                    return false;
                }

                if (_userToBattleId.ContainsKey(client.UserId))
                {
                    // 玩家不在队列但注册在战斗中 → 可能是残留的未启动战斗
                    int staleBattleId = _userToBattleId[client.UserId];
                    Log($"Player {client.UserId} has stale battle entry (battle {staleBattleId}), auto-cleaning for re-queue");
                    CleanupStaleBattleEntries(staleBattleId);
                }

                var info = new MatchUserInfo
                {
                    UserId = client.UserId,
                    Username = client.Username,
                    TeamId = teamId,
                    HeroId = heroId > 0 ? heroId : ShootingGame.Shared.Hero.HeroRegistry.DefaultHeroId,
                    JoinTime = DateTime.Now,
                    Client = client
                };

                _queue.Add(info);
                client.IsInQueue = true;

                Log($"Player {client.UserId} ({client.Username}) joined queue. Queue size: {_queue.Count}");

                // Try to match
                TryMatch();

                return true;
            }
        }

        /// <summary>
        /// Remove all _userToBattleId entries for a stale battle that never started.
        /// </summary>
        private void CleanupStaleBattleEntries(int battleId)
        {
            if (_activeBattles.TryGetValue(battleId, out var battle))
            {
                foreach (var kvp in battle.Context.UserIdToBattlePlayerId)
                {
                    _userToBattleId.Remove(kvp.Key);
                }
                _activeBattles.Remove(battleId);
                Log($"Cleaned up stale battle {battleId} ({battle.Context.Players.Count} players released)");
            }
            else
            {
                // Battle not in _activeBattles but entries in _userToBattleId exist
                // Find and remove all entries pointing to this battle
                var keysToRemove = new List<int>();
                foreach (var kvp in _userToBattleId)
                {
                    if (kvp.Value == battleId)
                        keysToRemove.Add(kvp.Key);
                }
                foreach (var key in keysToRemove)
                {
                    _userToBattleId.Remove(key);
                }
                Log($"Cleaned up orphan _userToBattleId entries for battle {battleId} ({keysToRemove.Count} entries)");
            }
        }

        /// <summary>
        /// Remove a player from the matching queue.
        /// Also cleans up stale battle entries if the player was matched
        /// but the battle never started.
        /// </summary>
        public bool LeaveQueue(int userId)
        {
            lock (_lock)
            {
                int index = _queue.FindIndex(p => p.UserId == userId);
                if (index >= 0)
                {
                    var info = _queue[index];
                    info.Client.IsInQueue = false;
                    _queue.RemoveAt(index);
                    Log($"Player {userId} left queue. Queue size: {_queue.Count}");
                    return true;
                }

                // Player not in queue, but might be stuck in a stale battle
                // (matched via queue but battle never started).
                if (_userToBattleId.TryGetValue(userId, out int battleId))
                {
                    Log($"Player {userId} not in queue but registered in battle {battleId}, cleaning up stale entry");
                    if (_activeBattles.TryGetValue(battleId, out var battle))
                    {
                        battle.HandlePlayerDisconnect(userId);
                    }
                    CleanupStaleBattleEntries(battleId);
                    return true;
                }

                return false;
            }
        }

        /// <summary>
        /// Handle player disconnect.
        /// </summary>
        public void OnPlayerDisconnect(int userId)
        {
            lock (_lock)
            {
                // Remove from queue
                LeaveQueue(userId);

                // Notify active battle if any
                if (_userToBattleId.TryGetValue(userId, out int battleId))
                {
                    if (_activeBattles.TryGetValue(battleId, out var battle))
                    {
                        battle.HandlePlayerDisconnect(userId);
                    }
                }
            }
        }

        private void TryMatch()
        {
            // Simple matchmaking: just take first N players
            // In production, you'd want rating-based matching, etc.
            while (_queue.Count >= _playersPerMatch)
            {
                var matchedPlayers = new List<MatchUserInfo>();
                for (int i = 0; i < _playersPerMatch; i++)
                {
                    matchedPlayers.Add(_queue[i]);
                }

                // Remove matched players from queue
                for (int i = 0; i < _playersPerMatch; i++)
                {
                    _queue[0].Client.IsInQueue = false;
                    _queue.RemoveAt(0);
                }

                // Create battle
                CreateBattle(matchedPlayers);
            }
        }

        /// <summary>
        /// Start a battle directly from a list of players (used by room system).
        /// Cleans up stale battle entries from queue matching that never started.
        /// </summary>
        public BattleContext StartBattle(List<MatchUserInfo> players)
        {
            lock (_lock)
            {
                foreach (var player in players)
                {
                    if (_userToBattleId.TryGetValue(player.UserId, out int existingBattleId))
                    {
                        Log($"Player {player.UserId} was in stale battle {existingBattleId}, cleaning up for new battle");
                        CleanupStaleBattleEntries(existingBattleId);
                    }
                }
            }

            return CreateBattle(players);
        }

        private BattleContext CreateBattle(List<MatchUserInfo> players)
        {
            int battleId = Interlocked.Increment(ref _nextBattleId);

            var context = new BattleContext
            {
                BattleId = battleId,
                RandSeed = new Random().Next(0, 10000),
                CollisionDataPath = _collisionDataPath
            };

            // Use configured spawn points or defaults
            if (_spawnPoints != null && _spawnPoints.Count > 0)
            {
                context.SpawnPoints.AddRange(_spawnPoints);
            }
            else
            {
                // Default MOBA-style spawn: team1 east, team2 west
                context.SpawnPoints.Add(new SpawnPoint(new ShootingGame.Shared.Math.Vec3(15, 0, 0), -90f, 1));
                context.SpawnPoints.Add(new SpawnPoint(new ShootingGame.Shared.Math.Vec3(15, 0, 5), -90f, 1));
                context.SpawnPoints.Add(new SpawnPoint(new ShootingGame.Shared.Math.Vec3(15, 0, -5), -90f, 1));
                context.SpawnPoints.Add(new SpawnPoint(new ShootingGame.Shared.Math.Vec3(15, 0, 10), -90f, 1));
                context.SpawnPoints.Add(new SpawnPoint(new ShootingGame.Shared.Math.Vec3(15, 0, -10), -90f, 1));
                context.SpawnPoints.Add(new SpawnPoint(new ShootingGame.Shared.Math.Vec3(-15, 0, 0), 90f, 2));
                context.SpawnPoints.Add(new SpawnPoint(new ShootingGame.Shared.Math.Vec3(-15, 0, 5), 90f, 2));
                context.SpawnPoints.Add(new SpawnPoint(new ShootingGame.Shared.Math.Vec3(-15, 0, -5), 90f, 2));
                context.SpawnPoints.Add(new SpawnPoint(new ShootingGame.Shared.Math.Vec3(-15, 0, 10), 90f, 2));
                context.SpawnPoints.Add(new SpawnPoint(new ShootingGame.Shared.Math.Vec3(-15, 0, -10), 90f, 2));
            }

            // Assign team IDs and battle player IDs
            int bpId = 0;
            for (int i = 0; i < players.Count; i++)
            {
                var player = players[i];

                // Assign team ID if not set (alternating teams)
                if (player.TeamId == 0)
                {
                    player.TeamId = (i % _teamsPerMatch) + 1;
                }

                context.Players.Add(player);
                context.UserIdToBattlePlayerId[player.UserId] = bpId;
                bpId++;
            }

            // Track user -> battle mapping
            lock (_lock)
            {
                foreach (var player in players)
                {
                    _userToBattleId[player.UserId] = battleId;
                }

                // Create battle room
                var battleRoom = new BattleRoom(context, this);
                _activeBattles[battleId] = battleRoom;
            }

            Log($"Match found! Battle {battleId} created with {players.Count} players");

            // 必须在发送 MatchFound 之前触发 OnMatchFound，
            // 这样 BattleUdpServer.RegisterBattle 会先于客户端收到 MatchFound 执行
            OnMatchFound?.Invoke(context);

            // 通知玩家
            foreach (var player in players)
            {
                player.Client.Send(new MainPack
                {
                    RequestCode = RequestCode.Matching,
                    ActionCode = ActionCode.MatchFound,
                    BattleInfo = CreateBattleInfo(context, player.UserId)
                });
            }

            return context;
        }

        private BattleInfo CreateBattleInfo(BattleContext context, int forUserId)
        {
            var bi = new BattleInfo
            {
                BattleId = context.BattleId,
                RandSeed = context.RandSeed
            };

            foreach (var player in context.Players)
            {
                bi.BattlePlayers.Add(new BattlePlayerInfo
                {
                    PlayerId = context.GetBattlePlayerId(player.UserId),
                    TeamId = player.TeamId,
                    UserId = player.UserId,
                    PlayerName = player.Username,
                    HeroId = player.HeroId
                });
            }

            // Include spawn points
            foreach (var sp in context.SpawnPoints)
            {
                bi.SpawnPoints.Add(new SpawnPointMsg
                {
                    Position = sp.Position,
                    Yaw = sp.Yaw,
                    TeamId = sp.TeamId
                });
            }

            // Include collision data (for small maps; clients can also load locally)
            if (!string.IsNullOrEmpty(context.CollisionDataPath) && System.IO.File.Exists(context.CollisionDataPath))
            {
                bi.CollisionData = System.IO.File.ReadAllBytes(context.CollisionDataPath);
            }

            return bi;
        }

        /// <summary>
        /// Called when a battle ends.
        /// </summary>
        public void EndBattle(int battleId)
        {
            lock (_lock)
            {
                if (_activeBattles.TryGetValue(battleId, out var battle))
                {
                    // Remove user -> battle mappings
                    foreach (var kvp in battle.Context.UserIdToBattlePlayerId)
                    {
                        _userToBattleId.Remove(kvp.Key);
                    }

                    _activeBattles.Remove(battleId);
                    Log($"Battle {battleId} ended");
                }
            }

            OnBattleEnded?.Invoke(battleId);
        }

        public BattleRoom GetBattle(int battleId)
        {
            lock (_lock)
            {
                return _activeBattles.TryGetValue(battleId, out var battle) ? battle : null;
            }
        }

        public BattleRoom GetBattleByUserId(int userId)
        {
            lock (_lock)
            {
                if (_userToBattleId.TryGetValue(userId, out int battleId))
                {
                    return _activeBattles.TryGetValue(battleId, out var battle) ? battle : null;
                }
                return null;
            }
        }

        public int GetQueueSize()
        {
            lock (_lock)
            {
                return _queue.Count;
            }
        }

        private void Log(string message)
        {
            Console.WriteLine($"[MatchMaker] {DateTime.Now:HH:mm:ss.fff} {message}");
        }
    }
}