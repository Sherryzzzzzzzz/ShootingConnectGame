using System;
using System.Collections.Generic;
using ShootingGame.Shared.Protocol.Kcp;

namespace ShootingGame.Shared.Protocol
{
    /// <summary>
    /// Wraps KCP for game networking. Provides reliable ordered delivery
    /// via KCP, while unreliable data bypasses KCP and is sent directly.
    ///
    /// Wire format:
    ///   Reliable: [KCP segment bytes] — handled entirely by KCP
    ///   Unreliable: [byte 0xFE][uint conv][payload bytes] — bypasses KCP
    /// </summary>
    public class KcpChannel
    {
        public const byte UnreliableMarker = 0xFE;
        public const int UnreliableHeaderSize = 1 + 4; // marker + conv
        public const int KcpMinHeaderSize = 4; // conv ID is first 4 bytes

        private readonly KCP _kcp;
        private readonly uint _conv;
        private readonly Action<byte[], int> _output; // raw UDP send callback
        private readonly List<byte[]> _receivedMessages = new List<byte[]>();
        private readonly object _recvLock = new object();

        private readonly byte[] _recvBuffer;

        public uint Conv => _conv;
        public float SmoothedRtt { get; private set; }

        /// <summary>Extract KCP conversation ID from raw data (little-endian, first 4 bytes).</summary>
        public static uint ExtractConv(byte[] data, int offset)
        {
            if (data.Length - offset < 4) return 0;
            return (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));
        }

        /// <param name="conv">Conversation ID (must match between peers)</param>
        /// <param name="output">Callback for raw UDP send</param>
        public KcpChannel(uint conv, Action<byte[], int> output)
        {
            _conv = conv;
            _output = output;
            _kcp = new KCP(conv, (buf, len) =>
            {
                output(buf, len);
            });
            _recvBuffer = new byte[65536];

            // Configure for low-latency game use
            // nodelay=1, interval=10ms, resend=2 (fast retransmit after 2 ACK jumps), nc=1 (no congestion control)
            _kcp.NoDelay(1, 10, 2, 1);
            // Small window for low latency (shooter game)
            _kcp.WndSize(128, 128);
            // MTU 512 to avoid IP fragmentation
            _kcp.SetMtu(512);
        }

        /// <summary>Feed raw received UDP data to KCP (for KCP data) or extract unreliable payload.</summary>
        /// <returns>True if an unreliable message was extracted.</returns>
        public bool Input(byte[] data, int offset, int length, out byte[] unreliablePayload)
        {
            unreliablePayload = null;

            if (length < 1) return false;

            // Check for unreliable marker
            if (data[offset] == UnreliableMarker)
            {
                if (length < UnreliableHeaderSize) return false;

                // Verify conv ID
                uint msgConv = BitConverter.ToUInt32(data, offset + 1);
                if (msgConv != _conv) return false;

                int payloadLen = length - UnreliableHeaderSize;
                unreliablePayload = new byte[payloadLen];
                Buffer.BlockCopy(data, offset + UnreliableHeaderSize, unreliablePayload, 0, payloadLen);
                return true;
            }

            // Otherwise, feed to KCP
            _kcp.Input(data, offset, length);
            return false;
        }

        /// <summary>Input convenience overload (entire buffer).</summary>
        public bool Input(byte[] data, int length, out byte[] unreliablePayload)
        {
            return Input(data, 0, length, out unreliablePayload);
        }

        /// <summary>Send data reliably via KCP.</summary>
        public void SendReliable(byte[] data, int length)
        {
            _kcp.Send(data);
        }

        /// <summary>Send data reliably (entire buffer).</summary>
        public void SendReliable(byte[] data)
        {
            _kcp.Send(data);
        }

        /// <summary>Wrap unreliable data with marker + conv prefix.</summary>
        public static byte[] WrapUnreliable(uint conv, byte[] data, int offset, int length)
        {
            byte[] packet = new byte[UnreliableHeaderSize + length];
            packet[0] = UnreliableMarker;
            byte[] convBytes = BitConverter.GetBytes(conv);
            Buffer.BlockCopy(convBytes, 0, packet, 1, 4);
            Buffer.BlockCopy(data, offset, packet, UnreliableHeaderSize, length);
            return packet;
        }

        /// <summary>Wrap unreliable data (entire buffer).</summary>
        public byte[] WrapUnreliable(byte[] data)
        {
            return WrapUnreliable(_conv, data, 0, data.Length);
        }

        /// <summary>Drive KCP state machine. Call this regularly (e.g., every frame).</summary>
        public void Update(uint currentTimeMs)
        {
            _kcp.Update(currentTimeMs);

            // Collect reliable messages from KCP
            while (true)
            {
                int peekSize = _kcp.PeekSize();
                if (peekSize <= 0) break;

                if (peekSize > _recvBuffer.Length) break;
                int received = _kcp.Recv(_recvBuffer);
                if (received <= 0) break;

                var msg = new byte[received];
                Buffer.BlockCopy(_recvBuffer, 0, msg, 0, received);

                lock (_recvLock)
                {
                    _receivedMessages.Add(msg);
                }
            }
        }

        /// <summary>Get next available received reliable message.</summary>
        public bool TryRecv(out byte[] message)
        {
            lock (_recvLock)
            {
                if (_receivedMessages.Count > 0)
                {
                    message = _receivedMessages[0];
                    _receivedMessages.RemoveAt(0);
                    return true;
                }
            }
            message = null;
            return false;
        }

        /// <summary>Drain all received reliable messages into a list.</summary>
        public List<byte[]> DrainRecv()
        {
            lock (_recvLock)
            {
                var result = new List<byte[]>(_receivedMessages);
                _receivedMessages.Clear();
                return result;
            }
        }

        /// <summary>How many ms until KCP needs Update next. Used for scheduling.</summary>
        public int Check(uint currentTimeMs)
        {
            return _kcp.Check(currentTimeMs);
        }
    }
}
