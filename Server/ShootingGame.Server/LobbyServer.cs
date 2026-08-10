using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using ShootingGame.Shared.Protocol;

namespace ShootingGame.Server
{
    /// <summary>
    /// TCP Lobby Server handling login, matching, and player management.
    /// Listens on port 7778 for TCP connections.
    /// </summary>
    public class LobbyServer
    {
        private readonly int _port;
        private TcpListener _listener;
        private volatile bool _running;
        private Thread _listenThread;

        // Connected clients (TCP)
        private readonly ConcurrentDictionary<int, LobbyClient> _clients = new ConcurrentDictionary<int, LobbyClient>();
        private int _nextClientId = 1;

        // MatchMaker reference
        private readonly MatchMaker _matchMaker;

        // Events
        public event Action<LobbyClient> OnClientConnected;
        public event Action<LobbyClient> OnClientDisconnected;
        public event Action<LobbyClient, MainPack> OnMessageReceived;

        public IReadOnlyDictionary<int, LobbyClient> ConnectedClients => _clients;

        /// <summary>
        /// Number of logged-in users (not just TCP connections).
        /// </summary>
        public int OnlineCount
        {
            get
            {
                int count = 0;
                foreach (var client in _clients.Values)
                {
                    if (client.IsLoggedIn)
                        count++;
                }
                return count;
            }
        }

        public LobbyServer(int port = 7778, MatchMaker matchMaker = null)
        {
            _port = port;
            _matchMaker = matchMaker;
        }

        public void Start()
        {
            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start();
            _running = true;

            _listenThread = new Thread(ListenForClients) { IsBackground = true };
            _listenThread.Start();

            Log($"LobbyServer started on port {_port}");
        }

        public void Stop()
        {
            _running = false;
            _listener?.Stop();

            // Disconnect all clients
            foreach (var client in _clients.Values)
            {
                client.Disconnect();
            }
            _clients.Clear();

            Log("LobbyServer stopped");
        }

        private void ListenForClients()
        {
            while (_running)
            {
                try
                {
                    var tcpClient = _listener.AcceptTcpClient();
                    var clientId = Interlocked.Increment(ref _nextClientId);
                    var lobbyClient = new LobbyClient(clientId, tcpClient, this);

                    _clients[clientId] = lobbyClient;
                    lobbyClient.Start();

                    OnClientConnected?.Invoke(lobbyClient);
                    Log($"Client {clientId} connected from {tcpClient.Client.RemoteEndPoint}");
                }
                catch (Exception ex) when (_running)
                {
                    Log($"Error accepting client: {ex.Message}");
                }
            }
        }

        public void RemoveClient(int clientId)
        {
            if (_clients.TryRemove(clientId, out var client))
            {
                OnClientDisconnected?.Invoke(client);
                Log($"Client {clientId} disconnected");
                BroadcastOnlinePlayers();
            }
        }

        public void Broadcast(MainPack pack, int excludeClientId = -1)
        {
            foreach (var client in _clients.Values)
            {
                if (client.ClientId != excludeClientId)
                {
                    client.Send(pack);
                }
            }
        }

        /// <summary>
        /// Broadcast online player count to all connected clients.
        /// </summary>
        public void BroadcastOnlinePlayers()
        {
            int count = OnlineCount;
            var pack = new MainPack
            {
                RequestCode = RequestCode.Matching,
                ActionCode = ActionCode.OnlinePlayers,
                Str = count.ToString()
            };

            foreach (var client in _clients.Values)
            {
                client.Send(pack);
            }
        }

        public LobbyClient GetClient(int clientId)
        {
            _clients.TryGetValue(clientId, out var client);
            return client;
        }

        public LobbyClient GetClientByUserId(int userId)
        {
            foreach (var client in _clients.Values)
            {
                if (client.UserId == userId)
                    return client;
            }
            return null;
        }

        internal void HandleMessage(LobbyClient client, MainPack pack)
        {
            OnMessageReceived?.Invoke(client, pack);
        }

        private void Log(string message)
        {
            Console.WriteLine($"[LobbyServer] {DateTime.Now:HH:mm:ss.fff} {message}");
        }
    }

    /// <summary>
    /// Represents a connected TCP client in the lobby.
    /// </summary>
    public class LobbyClient
    {
        public int ClientId { get; }
        public int UserId { get; set; }
        public string Username { get; set; }
        public bool IsLoggedIn => UserId > 0;
        public bool IsInQueue { get; set; }
        public int TeamId { get; set; }
        public int HeroId { get; set; } = ShootingGame.Shared.Hero.HeroRegistry.DefaultHeroId;
        public bool HeroConfirmed { get; set; }

        private readonly TcpClient _tcpClient;
        private readonly LobbyServer _server;
        private readonly TcpChannel _channel = new TcpChannel();
        private volatile bool _connected;

        public event Action<MainPack> OnPacketReceived;

        public LobbyClient(int clientId, TcpClient tcpClient, LobbyServer server)
        {
            ClientId = clientId;
            _tcpClient = tcpClient;
            _server = server;
        }

        public void Start()
        {
            _connected = true;
            _channel.OnFrameReceived += OnFrameReceived;
            _channel.OnDisconnected += OnChannelDisconnected;
            _channel.Wrap(_tcpClient);
        }

        public void Disconnect()
        {
            if (!_connected) return;
            _connected = false;

            try { _channel.Close(); }
            catch { }

            _server.RemoveClient(ClientId);
        }

        private void OnFrameReceived(byte[] data)
        {
            try
            {
                var pack = ProtobufSerializer.DeserializeMainPack(data);

                if (pack.ActionCode == ActionCode.Login && pack.UserInfo != null)
                {
                    Username = pack.UserInfo.Username;
                }

                _server.HandleMessage(this, pack);
                OnPacketReceived?.Invoke(pack);
            }
            catch (Exception ex) when (_connected)
            {
                Console.WriteLine($"[LobbyClient {ClientId}] Deserialize error: {ex.Message}");
                Console.WriteLine($"[LobbyClient {ClientId}] Stack: {ex.StackTrace}");
                if (ex.InnerException != null)
                    Console.WriteLine($"[LobbyClient {ClientId}] Inner: {ex.InnerException.Message}");
            }
        }

        private void OnChannelDisconnected()
        {
            Disconnect();
        }

        public void Send(MainPack pack)
        {
            if (!_connected) return;

            try
            {
                byte[] body = ProtobufSerializer.SerializeMainPack(pack);
                _channel.Send(body);
            }
            catch (Exception ex) when (_connected)
            {
                Console.WriteLine($"[LobbyClient {ClientId}] Send error: {ex.Message}");
                Disconnect();
            }
        }

        public void SendLoginResult(bool success, string message = "", int assignedUserId = 0)
        {
            var pack = new MainPack
            {
                RequestCode = RequestCode.User,
                ActionCode = ActionCode.LoginResult,
                ReturnCode = success ? ReturnCode.Success : ReturnCode.Fail,
                Str = message,
                IntVal = assignedUserId
            };
            Send(pack);
        }
    }
}