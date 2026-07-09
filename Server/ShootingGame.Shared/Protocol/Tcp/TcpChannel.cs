using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;

namespace ShootingGame.Shared.Protocol
{
    /// <summary>
    /// TCP channel with length-prefixed frame protocol ([4-byte big-endian length][protobuf bytes]).
    /// Handles TCP framing (splitting/merging), connect, send, receive, and close.
    /// </summary>
    public class TcpChannel
    {
        private TcpClient _tcpClient;
        private NetworkStream _stream;
        private Thread _receiveThread;
        private volatile bool _running;

        private readonly byte[] _lengthBuffer = new byte[4];
        private readonly byte[] _receiveBuffer = new byte[65536];
        private readonly List<byte[]> _receivedFrames = new List<byte[]>();
        private readonly object _recvLock = new object();
        private readonly object _sendLock = new object();

        public bool IsConnected => _running && _tcpClient != null && _tcpClient.Connected;

        public event Action<byte[]> OnFrameReceived;
        public event Action OnDisconnected;

        /// <summary>Wrap an existing connected TcpClient.</summary>
        public void Wrap(TcpClient tcpClient)
        {
            _tcpClient = tcpClient;
            _stream = tcpClient.GetStream();
            _running = true;

            _receiveThread = new Thread(ReceiveLoop)
            {
                IsBackground = true,
                Name = "TcpChannel_Receive"
            };
            _receiveThread.Start();
        }

        /// <summary>Connect to a remote endpoint.</summary>
        public bool Connect(string host, int port, int timeoutMs = 5000)
        {
            try
            {
                _tcpClient = new TcpClient();
                var result = _tcpClient.BeginConnect(host, port, null, null);
                if (!result.AsyncWaitHandle.WaitOne(timeoutMs))
                {
                    _tcpClient.Close();
                    return false;
                }
                _tcpClient.EndConnect(result);

                _stream = _tcpClient.GetStream();
                _running = true;

                _receiveThread = new Thread(ReceiveLoop)
                {
                    IsBackground = true,
                    Name = "TcpChannel_Receive"
                };
                _receiveThread.Start();

                return true;
            }
            catch
            {
                return false;
            }
        }

        public void Close()
        {
            if (!_running) return;
            _running = false;

            try { _stream?.Close(); } catch { }
            try { _tcpClient?.Close(); } catch { }
        }

        /// <summary>Send a frame (length-prefixed).</summary>
        public void Send(byte[] data)
        {
            if (!IsConnected) return;

            try
            {
                int length = data.Length;
                var lenBytes = new byte[]
                {
                    (byte)(length >> 24),
                    (byte)(length >> 16),
                    (byte)(length >> 8),
                    (byte)(length)
                };

                lock (_sendLock)
                {
                    _stream.Write(lenBytes, 0, 4);
                    _stream.Write(data, 0, length);
                }
            }
            catch
            {
                Close();
            }
        }

        /// <summary>Try to get the next received frame. Returns null if empty.</summary>
        public bool TryRecv(out byte[] frame)
        {
            lock (_recvLock)
            {
                if (_receivedFrames.Count > 0)
                {
                    frame = _receivedFrames[0];
                    _receivedFrames.RemoveAt(0);
                    return true;
                }
            }
            frame = null;
            return false;
        }

        /// <summary>Drain all received frames.</summary>
        public List<byte[]> DrainRecv()
        {
            lock (_recvLock)
            {
                var result = new List<byte[]>(_receivedFrames);
                _receivedFrames.Clear();
                return result;
            }
        }

        private void ReceiveLoop()
        {
            while (_running)
            {
                try
                {
                    // Read 4-byte big-endian length prefix
                    int bytesRead = ReadExact(_lengthBuffer, 0, 4);
                    if (bytesRead < 4) break;

                    int length = (_lengthBuffer[0] << 24) | (_lengthBuffer[1] << 16) |
                                 (_lengthBuffer[2] << 8) | _lengthBuffer[3];

                    if (length <= 0 || length > _receiveBuffer.Length) break;

                    // Read payload
                    bytesRead = ReadExact(_receiveBuffer, 0, length);
                    if (bytesRead < length) break;

                    // Emit frame
                    var frame = new byte[length];
                    Buffer.BlockCopy(_receiveBuffer, 0, frame, 0, length);

                    lock (_recvLock)
                    {
                        _receivedFrames.Add(frame);
                    }

                    OnFrameReceived?.Invoke(frame);
                }
                catch when (!_running)
                {
                    break;
                }
                catch
                {
                    break;
                }
            }

            Close();
            OnDisconnected?.Invoke();
        }

        private int ReadExact(byte[] buffer, int offset, int count)
        {
            int totalRead = 0;
            while (totalRead < count)
            {
                int read = _stream.Read(buffer, offset + totalRead, count - totalRead);
                if (read == 0) break;
                totalRead += read;
            }
            return totalRead;
        }
    }
}
