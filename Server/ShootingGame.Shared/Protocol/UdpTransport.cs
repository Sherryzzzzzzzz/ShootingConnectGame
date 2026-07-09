using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace ShootingGame.Shared.Protocol
{
    public struct ReceivedPacket
    {
        public byte[] Data;
        public int Length;
        public IPEndPoint RemoteEndPoint;
    }

    /// <summary>
    /// UDP transport layer. Sends and receives raw byte packets.
    /// Background receive thread puts packets into a ConcurrentQueue for main-thread processing.
    /// </summary>
    public class UdpTransport : IDisposable
    {
        private UdpClient _udp;
        private Thread _receiveThread;
        private volatile bool _running;
        private readonly ConcurrentQueue<ReceivedPacket> _receiveQueue = new ConcurrentQueue<ReceivedPacket>();

        /// <summary>
        /// Bind to a local port (server mode) or any available port (client mode, pass 0).
        /// </summary>
        public void Start(int port)
        {
            _udp = new UdpClient(port);
            _running = true;
            _receiveThread = new Thread(ReceiveLoop) { IsBackground = true, Name = "UdpReceive" };
            _receiveThread.Start();
        }

        public void Stop()
        {
            _running = false;
            try { _udp?.Close(); } catch { }
            _receiveThread?.Join(500);
            _udp = null;
        }

        public void Dispose() => Stop();

        public int LocalPort => ((IPEndPoint)_udp?.Client?.LocalEndPoint)?.Port ?? 0;

        /// <summary>
        /// Send raw bytes to a remote endpoint.
        /// </summary>
        public void Send(byte[] data, int length, IPEndPoint remote)
        {
            if (_udp == null) return;
            try
            {
                _udp.Send(data, length, remote);
            }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
        }

        /// <summary>
        /// Try to dequeue a received packet. Returns false if queue is empty.
        /// Call this from the main thread / game loop.
        /// </summary>
        public bool TryReceive(out ReceivedPacket packet)
        {
            return _receiveQueue.TryDequeue(out packet);
        }

        private void ReceiveLoop()
        {
            while (_running)
            {
                try
                {
                    IPEndPoint remote = null;
                    byte[] data = _udp.Receive(ref remote);
                    _receiveQueue.Enqueue(new ReceivedPacket
                    {
                        Data = data,
                        Length = data.Length,
                        RemoteEndPoint = remote
                    });
                }
                catch (SocketException)
                {
                    if (!_running) break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
            }
        }
    }
}
