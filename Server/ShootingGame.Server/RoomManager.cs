using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ShootingGame.Shared.Protocol;

namespace ShootingGame.Server
{
    /// <summary>
    /// Manages game rooms: create, join, leave, list, and auto-start battle when full.
    /// </summary>
    public class RoomManager
    {
        private readonly object _lock = new object();
        private readonly Dictionary<int, GameRoom> _rooms = new Dictionary<int, GameRoom>();
        private readonly Dictionary<int, int> _userToRoom = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _battleToRoom = new Dictionary<int, int>();
        private int _nextRoomId = 1;
        private readonly MatchMaker _matchMaker;
        private readonly LobbyServer _lobbyServer;

        public event Action<BattleContext> OnBattleStart;

        public RoomManager(MatchMaker matchMaker, LobbyServer lobbyServer)
        {
            _matchMaker = matchMaker;
            _lobbyServer = lobbyServer;
        }

        public List<RoomInfo> GetRoomList()
        {
            lock (_lock)
            {
                var list = new List<RoomInfo>();
                foreach (var room in _rooms.Values)
                {
                    list.Add(CloneRoomInfo(room.Info));
                }
                return list;
            }
        }

        public (bool Success, string Error, RoomInfo Room) CreateRoom(LobbyClient creator, string roomName, int maxPlayers)
        {
            if (string.IsNullOrWhiteSpace(roomName))
                roomName = $"{creator.Username}'s Room";

            if (maxPlayers < 2) maxPlayers = 2;
            if (maxPlayers > 8) maxPlayers = 8;

            lock (_lock)
            {
                if (_userToRoom.ContainsKey(creator.UserId))
                    return (false, "Already in a room", null);

                if (_matchMaker.GetBattleByUserId(creator.UserId) != null)
                    return (false, "Already in a battle", null);

                int roomId = _nextRoomId++;
                var room = new GameRoom
                {
                    Info = new RoomInfo
                    {
                        RoomId = roomId,
                        RoomName = roomName,
                        CreatorName = creator.Username,
                        PlayerCount = 1,
                        MaxPlayers = maxPlayers,
                        Status = 0 // Waiting
                    },
                    CreatorUserId = creator.UserId,
                    Players = new List<LobbyClient> { creator }
                };

                _rooms[roomId] = room;
                _userToRoom[creator.UserId] = roomId;

                Log($"Room {roomId} \"{roomName}\" created by {creator.Username} (max {maxPlayers})");
                BroadcastRoomList();
                return (true, null, CloneRoomInfo(room.Info));
            }
        }

        public (bool Success, string Error) JoinRoom(LobbyClient client, int roomId)
        {
            lock (_lock)
            {
                if (_userToRoom.ContainsKey(client.UserId))
                    return (false, "Already in a room");

                if (_matchMaker.GetBattleByUserId(client.UserId) != null)
                    return (false, "Already in a battle");

                if (!_rooms.TryGetValue(roomId, out var room))
                    return (false, "Room not found");

                if (room.Info.Status != 0)
                    return (false, "Room is already playing");

                if (room.Info.PlayerCount >= room.Info.MaxPlayers)
                    return (false, "Room is full");

                room.Players.Add(client);
                room.Info.PlayerCount = room.Players.Count;
                _userToRoom[client.UserId] = roomId;

                Log($"{client.Username} joined room {roomId} \"{room.Info.RoomName}\" ({room.Info.PlayerCount}/{room.Info.MaxPlayers})");
                BroadcastRoomList();

                // Auto-start when full
                if (room.Info.PlayerCount >= room.Info.MaxPlayers)
                {
                    StartBattleForRoom(room);
                }

                return (true, null);
            }
        }

        public bool LeaveRoom(LobbyClient client)
        {
            lock (_lock)
            {
                if (!_userToRoom.TryGetValue(client.UserId, out int roomId))
                    return false;

                if (!_rooms.TryGetValue(roomId, out var room))
                {
                    _userToRoom.Remove(client.UserId);
                    return false;
                }

                room.Players.RemoveAll(p => p.UserId == client.UserId);
                room.Info.PlayerCount = room.Players.Count;
                _userToRoom.Remove(client.UserId);

                Log($"{client.Username} left room {roomId}");

                if (room.Players.Count == 0)
                {
                    _rooms.Remove(roomId);
                    Log($"Room {roomId} removed (empty)");
                }

                BroadcastRoomList();
                return true;
            }
        }

        public void OnPlayerDisconnect(int userId)
        {
            LobbyClient client = _lobbyServer.GetClientByUserId(userId);
            if (client != null)
            {
                LeaveRoom(client);
            }
        }

        private void StartBattleForRoom(GameRoom room)
        {
            room.Info.Status = 1; // Playing

            // Convert room players to MatchUserInfo
            var players = new List<MatchUserInfo>();
            int teamId = 1;
            foreach (var client in room.Players)
            {
                players.Add(new MatchUserInfo
                {
                    UserId = client.UserId,
                    Username = client.Username,
                    TeamId = teamId,
                    HeroId = client.HeroId,
                    Client = client
                });
                teamId = teamId == 1 ? 2 : 1; // Alternate teams
            }

            Log($"Room {room.Info.RoomId} full, starting battle with {players.Count} players");

            var context = _matchMaker.StartBattle(players);
            if (context != null)
            {
                _battleToRoom[context.BattleId] = room.Info.RoomId;
                BroadcastRoomList();
                OnBattleStart?.Invoke(context);
            }
        }

        /// <summary>
        /// Called when a battle ends. Cleans up the associated room.
        /// </summary>
        public void OnBattleEnded(int battleId)
        {
            lock (_lock)
            {
                if (_battleToRoom.TryGetValue(battleId, out int roomId))
                {
                    _battleToRoom.Remove(battleId);

                    if (_rooms.TryGetValue(roomId, out var room))
                    {
                        // Remove all players from room tracking
                        foreach (var player in room.Players)
                        {
                            _userToRoom.Remove(player.UserId);
                        }

                        _rooms.Remove(roomId);
                        Log($"Room {roomId} removed (battle {battleId} ended)");
                    }
                }

                BroadcastRoomList();
            }
        }

        public void BroadcastRoomList()
        {
            var roomList = GetRoomList();
            var pack = new MainPack
            {
                RequestCode = RequestCode.Matching,
                ActionCode = ActionCode.RoomList
            };
            pack.RoomInfos.AddRange(roomList);

            foreach (var client in _lobbyServer.ConnectedClients.Values)
            {
                client.Send(pack);
            }
        }

        private RoomInfo CloneRoomInfo(RoomInfo info)
        {
            return new RoomInfo
            {
                RoomId = info.RoomId,
                RoomName = info.RoomName,
                CreatorName = info.CreatorName,
                PlayerCount = info.PlayerCount,
                MaxPlayers = info.MaxPlayers,
                Status = info.Status
            };
        }

        private void Log(string message)
        {
            Console.WriteLine($"[RoomManager] {DateTime.Now:HH:mm:ss.fff} {message}");
        }

        private class GameRoom
        {
            public RoomInfo Info;
            public int CreatorUserId;
            public List<LobbyClient> Players;
        }
    }
}
