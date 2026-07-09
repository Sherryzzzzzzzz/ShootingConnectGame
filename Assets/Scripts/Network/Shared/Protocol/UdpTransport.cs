// UDP传输层
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
    /// UDP传输层，发送和接收原始字节数据包
    /// 后台接收线程将数据包放入并发队列供主线程处理
    /// </summary>
    public class UdpTransport : IDisposable
    {
        private UdpClient _udp;
        private Thread _receiveThread;
        private volatile bool _running;
        private readonly ConcurrentQueue<ReceivedPacket> _receiveQueue = new ConcurrentQueue<ReceivedPacket>();

        /// <summary>
        /// 绑定到本地端口
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
        /// 发送原始字节到远程端点
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
        /// 尝试从队列中取出接收的数据包
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