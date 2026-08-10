using System;
using System.IO;
using ShootingGame.Shared.GameplayTags;
using ShootingGame.Shared.Protocol;

namespace ShootingGame.Server
{
    class Program
    {
        /// <summary>
        /// 自动探测客户端导出的碰撞数据 collision.bin。
        /// 从上到下尝试常见路径：显式参数 > 项目 StreamingAssets > 当前目录 > 上级目录。
        /// </summary>
        static string AutoFindCollisionFile()
        {
            string[] candidates = {
                Path.Combine(Directory.GetCurrentDirectory(), "Assets", "StreamingAssets", "collision.bin"),
                Path.Combine(Directory.GetCurrentDirectory(), "StreamingAssets", "collision.bin"),
                Path.Combine(Directory.GetCurrentDirectory(), "collision.bin"),
                // 从 exe 位置往上找项目根
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "Assets", "StreamingAssets", "collision.bin"),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Assets", "StreamingAssets", "collision.bin"),
            };
            foreach (var p in candidates)
            {
                var full = Path.GetFullPath(p);
                if (File.Exists(full))
                    return full;
            }
            return null;
        }

        static void Main(string[] args)
        {
            int lobbyPort = 7778;
            int battlePort = 7777;
            int playersPerMatch = 2;
            int matchMode = 0;        // 0=团队歼灭 1=死斗(FFA)
            int killTarget = 10;
            float timeLimit = 300f;
            string collisionPath = null;
            string spawnConfigPath = null;
            string configDir = ".";

            // Parse arguments
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--lobby-port" && i + 1 < args.Length)
                    int.TryParse(args[++i], out lobbyPort);
                else if (args[i] == "--battle-port" && i + 1 < args.Length)
                    int.TryParse(args[++i], out battlePort);
                else if (args[i] == "--collision" && i + 1 < args.Length)
                    collisionPath = args[++i];
                else if (args[i] == "--spawn-config" && i + 1 < args.Length)
                    spawnConfigPath = args[++i];
                else if (args[i] == "--config-dir" && i + 1 < args.Length)
                    configDir = args[++i];
                else if (args[i] == "--players" && i + 1 < args.Length)
                    int.TryParse(args[++i], out playersPerMatch);
                else if (args[i] == "--mode" && i + 1 < args.Length)
                    matchMode = args[++i].Equals("deathmatch", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                else if (args[i] == "--kill-target" && i + 1 < args.Length)
                    int.TryParse(args[++i], out killTarget);
                else if (args[i] == "--time-limit" && i + 1 < args.Length)
                    float.TryParse(args[++i], out timeLimit);
            }

            // 从 JSON 加载英雄/枪械/技能配置（Unity 编辑器 GameConfigExporter 导出，双端单一数据源）
            var heroConfigs = GameConfigLoader.LoadAll(configDir);

            // 初始化 GameplayTag 系统
            GameplayTagConfig.Initialize();
            // 初始化英雄注册表（JSON 优先，缺失时回退硬编码默认）
            ShootingGame.Shared.Hero.HeroRegistry.Initialize(heroConfigs);

            Console.WriteLine("========================================");
            Console.WriteLine("  ShootingGame Server");
            Console.WriteLine("========================================");
            Console.WriteLine($"Lobby Port (TCP): {lobbyPort}");
            Console.WriteLine($"Battle Port (UDP): {battlePort}");
            Console.WriteLine("========================================");

            // 未指定 --collision 时自动探测客户端的 collision.bin（AABB 碰撞数据）
            if (string.IsNullOrEmpty(collisionPath))
            {
                collisionPath = AutoFindCollisionFile();
                if (collisionPath != null)
                    Console.WriteLine($"[Program] Auto-found collision file: {collisionPath}");
                else
                    Console.WriteLine("[Program] WARNING: collision.bin not found, server will use default floor only (client has walls!)");
            }

            // Create components
            var matchMaker = new MatchMaker(playersPerMatch: playersPerMatch, teamsPerMatch: 2);
            matchMaker.SetMatchMode(matchMode, killTarget, timeLimit);
            Console.WriteLine($"Mode: {(matchMode == 1 ? $"Deathmatch (kill target {killTarget}, time limit {timeLimit}s)" : "Team Elimination")}, Players per match: {playersPerMatch}");
            if (!string.IsNullOrEmpty(collisionPath))
                matchMaker.SetCollisionDataPath(collisionPath);

            // Load spawn point configuration (aligns with client scene SpawnPoints)
            if (!string.IsNullOrEmpty(spawnConfigPath))
            {
                var spawnPoints = SpawnPointConfig.Load(spawnConfigPath);
                if (spawnPoints != null)
                    matchMaker.SetSpawnPoints(spawnPoints);
            }
            var lobbyServer = new LobbyServer(lobbyPort, matchMaker);
            var battleUdpServer = new BattleUdpServer(battlePort);
            var roomManager = new RoomManager(matchMaker, lobbyServer);

            // Wire up events
            lobbyServer.OnMessageReceived += (client, pack) =>
            {
                HandleLobbyMessage(client, pack, matchMaker, lobbyServer, roomManager);
            };

            matchMaker.OnMatchFound += (context) =>
            {
                Console.WriteLine($"[MatchMaker] Match found! Battle {context.BattleId}");
                // 关键：把 BattleRoom 注册到 UDP 服务器，否则 BattleReady 找不到战斗房间
                var battleRoom = matchMaker.GetBattle(context.BattleId);
                if (battleRoom != null)
                {
                    battleUdpServer.RegisterBattle(battleRoom);
                    battleRoom.SetRttProvider((bpId) => battleUdpServer.GetPlayerRttSeconds(bpId));
                }
                else
                {
                    Console.WriteLine($"[MatchMaker] ERROR: BattleRoom {context.BattleId} not found!");
                }
            };

            roomManager.OnBattleStart += (context) =>
            {
                Console.WriteLine($"[RoomManager] Battle started! Battle {context.BattleId}");
                var battleRoom = matchMaker.GetBattle(context.BattleId);
                if (battleRoom != null)
                {
                    battleUdpServer.RegisterBattle(battleRoom);
                    battleRoom.SetRttProvider((bpId) => battleUdpServer.GetPlayerRttSeconds(bpId));
                }
                else
                {
                    Console.WriteLine($"[RoomManager] ERROR: BattleRoom {context.BattleId} not found!");
                }
            };

            matchMaker.OnBattleEnded += (battleId) =>
            {
                battleUdpServer.UnregisterBattle(battleId);
                roomManager.OnBattleEnded(battleId);
            };

            lobbyServer.OnClientDisconnected += (client) =>
            {
                if (client.UserId > 0)
                {
                    matchMaker.OnPlayerDisconnect(client.UserId);
                    roomManager.OnPlayerDisconnect(client.UserId);
                }
            };

            // Start servers
            lobbyServer.Start();
            battleUdpServer.Start();

            Console.WriteLine("Server running. Press Ctrl+C to stop.");

            // Wait for shutdown
            var shutdownEvent = new System.Threading.ManualResetEvent(false);
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                shutdownEvent.Set();
            };

            shutdownEvent.WaitOne();

            // Shutdown
            Console.WriteLine("Shutting down...");
            lobbyServer.Stop();
            battleUdpServer.Stop();
        }

        static void HandleLobbyMessage(LobbyClient client, MainPack pack, MatchMaker matchMaker, LobbyServer lobbyServer, RoomManager roomManager)
        {
            switch (pack.ActionCode)
            {
                case ActionCode.Login:
                    HandleLogin(client, pack, lobbyServer);
                    break;

                case ActionCode.JoinQueue:
                    HandleJoinQueue(client, pack, matchMaker);
                    break;

                case ActionCode.LeaveQueue:
                    HandleLeaveQueue(client, pack, matchMaker);
                    break;

                case ActionCode.StartEnterBattle:
                    // Client acknowledges match found and is ready for battle
                    break;

                case ActionCode.RoomList:
                    HandleRoomList(client, roomManager);
                    break;

                case ActionCode.CreateRoom:
                    HandleCreateRoom(client, pack, roomManager);
                    break;

                case ActionCode.JoinRoom:
                    HandleJoinRoom(client, pack, roomManager);
                    break;

                case ActionCode.LeaveRoom:
                    HandleLeaveRoom(client, pack, roomManager);
                    break;

                case ActionCode.HeroSelected:
                    // 转发给其他客户端（按 BattlePlayerId 组播）
                    BroadcastToBattlePlayers(client, pack, matchMaker);
                    // 不回显——发送方自己更新UI
                    client.Send(new MainPack
                    {
                        RequestCode = RequestCode.Battle,
                        ActionCode = ActionCode.Ping,
                        ReturnCode = ReturnCode.Success
                    });
                    break;

                case ActionCode.HeroConfirmed:
                    // 转发给其他客户端
                    BroadcastToBattlePlayers(client, pack, matchMaker);
                    client.HeroConfirmed = true;
                    client.Send(new MainPack
                    {
                        RequestCode = RequestCode.Battle,
                        ActionCode = ActionCode.Ping,
                        ReturnCode = ReturnCode.Success
                    });
                    // 检查是否所有玩家都已确认
                    CheckAllHeroesConfirmed(matchMaker, lobbyServer);
                    break;
            }
        }

        static void BroadcastToBattlePlayers(LobbyClient sender, MainPack pack, MatchMaker matchMaker)
        {
            // 找到发送者所在的战斗
            BattleRoom battle = null;
            foreach (var b in matchMaker.ActiveBattles.Values)
            {
                foreach (var p in b.Context.Players)
                {
                    if (p.UserId == sender.UserId) { battle = b; break; }
                }
                if (battle != null) break;
            }
            if (battle == null) return;

            // 广播给其他玩家
            foreach (var player in battle.Context.Players)
            {
                if (player.UserId == sender.UserId) continue;
                if (player.Client == null) continue;
                player.Client.Send(new MainPack
                {
                    RequestCode = pack.RequestCode,
                    ActionCode = pack.ActionCode,
                    IntVal = pack.IntVal,
                    Str = pack.Str
                });
            }
        }

        static void CheckAllHeroesConfirmed(MatchMaker matchMaker, LobbyServer lobbyServer)
        {
            foreach (var battle in matchMaker.ActiveBattles.Values)
            {
                if (battle.IsStarted) continue;
                bool allConfirmed = true;
                foreach (var player in battle.Context.Players)
                {
                    if (player.Client == null || !player.Client.HeroConfirmed)
                    {
                        allConfirmed = false;
                        break;
                    }
                }
                if (allConfirmed)
                {
                    Console.WriteLine($"[HeroSelect] All players confirmed for Battle {battle.Context.BattleId}");
                    lobbyServer.Broadcast(new MainPack
                    {
                        RequestCode = RequestCode.Battle,
                        ActionCode = ActionCode.StartEnterBattle
                    });
                    // 通知 BattleUdpServer 开始接受 BattleReady
                    battle.ForceStart();
                }
            }
        }

        static void HandleLogin(LobbyClient client, MainPack pack, LobbyServer lobbyServer)
        {
            // 使用服务端唯一的 ClientId 作为 UserId，确保每个客户端身份唯一
            client.UserId = client.ClientId;
            if (pack.UserInfo != null && !string.IsNullOrWhiteSpace(pack.UserInfo.Username))
            {
                client.Username = pack.UserInfo.Username;
            }
            else
            {
                client.Username = $"Player_{client.ClientId}";
            }

            client.SendLoginResult(true, $"Welcome, {client.Username}!", client.UserId);
            Console.WriteLine($"[Login] User {client.UserId} ({client.Username}) logged in. Online: {lobbyServer.OnlineCount}");

            // Broadcast updated online player count to all clients
            lobbyServer.BroadcastOnlinePlayers();
        }

        static void HandleJoinQueue(LobbyClient client, MainPack pack, MatchMaker matchMaker)
        {
            if (!client.IsLoggedIn)
            {
                Console.WriteLine($"[MatchMaker] JoinQueue rejected: User {client.ClientId} not logged in");
                client.Send(new MainPack
                {
                    RequestCode = RequestCode.Matching,
                    ActionCode = ActionCode.JoinQueue,
                    ReturnCode = ReturnCode.Fail,
                    Str = "未登录，请先登录"
                });
                return;
            }

            int heroId = pack.IntVal;
            if (heroId <= 0) heroId = ShootingGame.Shared.Hero.HeroRegistry.DefaultHeroId;
            client.HeroId = heroId;

            int queueSizeBefore = matchMaker.GetQueueSize();
            int activeBattleCount = matchMaker.ActiveBattles.Count;
            Console.WriteLine($"[MatchMaker] JoinQueue request: User={client.UserId}, heroId={heroId}, queueSize={queueSizeBefore}, activeBattles={activeBattleCount}");

            bool success = matchMaker.JoinQueue(client, heroId: heroId);

            if (success)
            {
                Console.WriteLine($"[MatchMaker] User {client.UserId} joined queue. Queue size: {matchMaker.GetQueueSize()}");
                client.Send(new MainPack
                {
                    RequestCode = RequestCode.Matching,
                    ActionCode = ActionCode.JoinQueue,
                    ReturnCode = ReturnCode.Success,
                    Str = "已加入匹配队列"
                });
            }
            else
            {
                // 诊断：为什么失败？
                string reason;
                if (client.IsInQueue)
                    reason = "你已在匹配队列中";
                else
                    reason = "你已在战斗中，请等待战斗结束或重启客户端";
                Console.WriteLine($"[MatchMaker] JoinQueue FAILED for User {client.UserId}: {reason}");
                client.Send(new MainPack
                {
                    RequestCode = RequestCode.Matching,
                    ActionCode = ActionCode.JoinQueue,
                    ReturnCode = ReturnCode.Fail,
                    Str = reason
                });
            }
        }

        static void HandleLeaveQueue(LobbyClient client, MainPack pack, MatchMaker matchMaker)
        {
            bool success = matchMaker.LeaveQueue(client.UserId);

            client.Send(new MainPack
            {
                RequestCode = RequestCode.Matching,
                ActionCode = ActionCode.LeaveQueue,
                ReturnCode = success ? ReturnCode.Success : ReturnCode.Fail
            });
        }

        static void HandleRoomList(LobbyClient client, RoomManager roomManager)
        {
            var rooms = roomManager.GetRoomList();
            var pack = new MainPack
            {
                RequestCode = RequestCode.Matching,
                ActionCode = ActionCode.RoomList
            };
            pack.RoomInfos.AddRange(rooms);
            client.Send(pack);
        }

        static void HandleCreateRoom(LobbyClient client, MainPack pack, RoomManager roomManager)
        {
            if (!client.IsLoggedIn)
            {
                client.Send(new MainPack
                {
                    RequestCode = RequestCode.Matching,
                    ActionCode = ActionCode.CreateRoom,
                    ReturnCode = ReturnCode.Fail,
                    Str = "Not logged in"
                });
                return;
            }

            string roomName = pack.RoomInfo?.RoomName ?? $"{client.Username}'s Room";
            int maxPlayers = pack.RoomInfo?.MaxPlayers ?? 2;
            if (maxPlayers < 2) maxPlayers = 2;

            var (success, error, room) = roomManager.CreateRoom(client, roomName, maxPlayers);

            var response = new MainPack
            {
                RequestCode = RequestCode.Matching,
                ActionCode = ActionCode.CreateRoom,
                ReturnCode = success ? ReturnCode.Success : ReturnCode.Fail,
                Str = error ?? "",
                RoomInfo = room
            };

            if (success)
            {
                response.IntVal = room.RoomId;
            }

            client.Send(response);
        }

        static void HandleJoinRoom(LobbyClient client, MainPack pack, RoomManager roomManager)
        {
            if (!client.IsLoggedIn)
            {
                client.Send(new MainPack
                {
                    RequestCode = RequestCode.Matching,
                    ActionCode = ActionCode.JoinRoom,
                    ReturnCode = ReturnCode.Fail,
                    Str = "Not logged in"
                });
                return;
            }

            int roomId = pack.IntVal;
            var (success, error) = roomManager.JoinRoom(client, roomId);

            client.Send(new MainPack
            {
                RequestCode = RequestCode.Matching,
                ActionCode = ActionCode.JoinRoom,
                ReturnCode = success ? ReturnCode.Success : ReturnCode.Fail,
                Str = error ?? "",
                IntVal = roomId
            });
        }

        static void HandleLeaveRoom(LobbyClient client, MainPack pack, RoomManager roomManager)
        {
            bool success = roomManager.LeaveRoom(client);

            client.Send(new MainPack
            {
                RequestCode = RequestCode.Matching,
                ActionCode = ActionCode.LeaveRoom,
                ReturnCode = success ? ReturnCode.Success : ReturnCode.Fail
            });
        }
    }
}