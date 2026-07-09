using System;
using System.Collections.Generic;
using System.Net;

namespace ShootingGame.Shared.Protocol
{
    /// <summary>
    /// Reliable delivery layer on top of UdpTransport.
    /// Uses sequence numbers, ACK bitmask, and retransmission.
    ///
    /// Packet envelope format:
    /// [byte channel: 0=unreliable, 1=reliable]
    /// [ushort sequence]
    /// [ushort ack]            — last received seq from remote
    /// [uint   ackBits]        — bitmask for previous 32 sequences
    /// [byte[] payload]
    /// </summary>
    public class ReliableChannel
    {
        public const int HeaderSize = 1 + 2 + 2 + 4; // 9 bytes
        private const int MaxPendingPackets = 256;
        private const float RetransmitIntervalSec = 0.1f; // 100ms
        private const int MaxRetransmits = 10;

        // Sending
        private ushort _localSequence;
        private readonly Dictionary<ushort, PendingPacket> _pendingAcks = new Dictionary<ushort, PendingPacket>();

        // Receiving
        private ushort _remoteSequence;
        private uint _receivedBits; // bitmask of received sequences before _remoteSequence

        // RTT
        private float _smoothedRtt = 0.05f; // start at 50ms
        public float SmoothedRtt => _smoothedRtt;

        private struct PendingPacket
        {
            public byte[] Data;
            public int Length;
            public float SendTime;
            public int Retransmits;
        }

        /// <summary>
        /// Wrap a payload into an unreliable envelope.
        /// </summary>
        public static int WriteUnreliableHeader(byte[] buffer, ushort localSeq, ushort remoteAck, uint ackBits)
        {
            int pos = 0;
            buffer[pos++] = 0; // channel = unreliable
            buffer[pos++] = (byte)(localSeq & 0xFF);
            buffer[pos++] = (byte)((localSeq >> 8) & 0xFF);
            buffer[pos++] = (byte)(remoteAck & 0xFF);
            buffer[pos++] = (byte)((remoteAck >> 8) & 0xFF);
            buffer[pos++] = (byte)(ackBits & 0xFF);
            buffer[pos++] = (byte)((ackBits >> 8) & 0xFF);
            buffer[pos++] = (byte)((ackBits >> 16) & 0xFF);
            buffer[pos++] = (byte)((ackBits >> 24) & 0xFF);
            return pos;
        }

        /// <summary>
        /// Prepare a reliable packet. Returns the full packet (header + payload) to send.
        /// Stores it for potential retransmission.
        /// </summary>
        public byte[] WrapReliable(byte[] payload, int payloadLength, float currentTime)
        {
            ushort seq = _localSequence++;
            byte[] packet = new byte[HeaderSize + payloadLength];

            int pos = 0;
            packet[pos++] = 1; // channel = reliable
            packet[pos++] = (byte)(seq & 0xFF);
            packet[pos++] = (byte)((seq >> 8) & 0xFF);
            packet[pos++] = (byte)(_remoteSequence & 0xFF);
            packet[pos++] = (byte)((_remoteSequence >> 8) & 0xFF);
            packet[pos++] = (byte)(_receivedBits & 0xFF);
            packet[pos++] = (byte)((_receivedBits >> 8) & 0xFF);
            packet[pos++] = (byte)((_receivedBits >> 16) & 0xFF);
            packet[pos++] = (byte)((_receivedBits >> 24) & 0xFF);

            Buffer.BlockCopy(payload, 0, packet, pos, payloadLength);

            _pendingAcks[seq] = new PendingPacket
            {
                Data = packet,
                Length = packet.Length,
                SendTime = currentTime,
                Retransmits = 0
            };

            return packet;
        }

        /// <summary>
        /// Prepare an unreliable packet.
        /// </summary>
        public byte[] WrapUnreliable(byte[] payload, int payloadLength)
        {
            ushort seq = _localSequence++;
            byte[] packet = new byte[HeaderSize + payloadLength];

            int pos = 0;
            packet[pos++] = 0; // channel = unreliable
            packet[pos++] = (byte)(seq & 0xFF);
            packet[pos++] = (byte)((seq >> 8) & 0xFF);
            packet[pos++] = (byte)(_remoteSequence & 0xFF);
            packet[pos++] = (byte)((_remoteSequence >> 8) & 0xFF);
            packet[pos++] = (byte)(_receivedBits & 0xFF);
            packet[pos++] = (byte)((_receivedBits >> 8) & 0xFF);
            packet[pos++] = (byte)((_receivedBits >> 16) & 0xFF);
            packet[pos++] = (byte)((_receivedBits >> 24) & 0xFF);

            Buffer.BlockCopy(payload, 0, packet, pos, payloadLength);
            return packet;
        }

        /// <summary>
        /// Parse a received packet header. Returns channel, sequence, and the payload start offset.
        /// Also processes the ACK info from the remote side.
        /// </summary>
        public bool ProcessReceived(byte[] data, int length, float currentTime,
                                     out byte channel, out ushort sequence, out int payloadOffset)
        {
            channel = 0;
            sequence = 0;
            payloadOffset = 0;

            if (length < HeaderSize) return false;

            channel = data[0];
            sequence = (ushort)(data[1] | (data[2] << 8));
            ushort remoteAck = (ushort)(data[3] | (data[4] << 8));
            uint remoteBits = (uint)(data[5] | (data[6] << 8) | (data[7] << 16) | (data[8] << 24));
            payloadOffset = HeaderSize;

            // Process remote's ACKs of our packets
            ProcessAcks(remoteAck, remoteBits, currentTime);

            // Track this sequence in our receive history
            TrackReceivedSequence(sequence);

            return true;
        }

        /// <summary>
        /// Get packets that need retransmission.
        /// </summary>
        public List<byte[]> GetRetransmits(float currentTime)
        {
            var toRetransmit = new List<byte[]>();
            var toRemove = new List<ushort>();

            foreach (var kvp in _pendingAcks)
            {
                float elapsed = currentTime - kvp.Value.SendTime;
                if (elapsed > RetransmitIntervalSec)
                {
                    if (kvp.Value.Retransmits >= MaxRetransmits)
                    {
                        toRemove.Add(kvp.Key);
                        continue;
                    }

                    var updated = kvp.Value;
                    updated.SendTime = currentTime;
                    updated.Retransmits++;
                    _pendingAcks[kvp.Key] = updated;

                    toRetransmit.Add(kvp.Value.Data);
                }
            }

            foreach (var key in toRemove)
                _pendingAcks.Remove(key);

            return toRetransmit;
        }

        /// <summary>
        /// Check if a connection is considered lost (too many unacked reliable packets).
        /// </summary>
        public bool IsConnectionLost => _pendingAcks.Count > MaxPendingPackets;

        /// <summary>
        /// Get the current local ACK state (for embedding in unreliable packets).
        /// </summary>
        public (ushort remoteAck, uint ackBits) GetAckState() => (_remoteSequence, _receivedBits);

        private void ProcessAcks(ushort ack, uint ackBits, float currentTime)
        {
            // ACK the specific sequence
            if (_pendingAcks.TryGetValue(ack, out var pending))
            {
                float rtt = currentTime - pending.SendTime;
                _smoothedRtt = 0.875f * _smoothedRtt + 0.125f * rtt;
                _pendingAcks.Remove(ack);
            }

            // ACK the bitmask sequences
            for (int i = 0; i < 32; i++)
            {
                if ((ackBits & (1u << i)) != 0)
                {
                    ushort seq = (ushort)(ack - 1 - i);
                    _pendingAcks.Remove(seq);
                }
            }
        }

        private void TrackReceivedSequence(ushort sequence)
        {
            // If this is newer than what we've seen
            if (IsNewer(sequence, _remoteSequence))
            {
                int diff = SequenceDiff(sequence, _remoteSequence);
                if (diff <= 32)
                {
                    _receivedBits = (_receivedBits << diff) | 1u; // shift old bits and mark previous _remoteSequence
                }
                else
                {
                    _receivedBits = 1u; // too far ahead, reset
                }
                _remoteSequence = sequence;
            }
            else
            {
                // Old or duplicate packet — mark in bitmask if within range
                int diff = SequenceDiff(_remoteSequence, sequence);
                if (diff > 0 && diff <= 32)
                {
                    _receivedBits |= (1u << (diff - 1));
                }
            }
        }

        private static bool IsNewer(ushort s1, ushort s2)
        {
            return ((s1 > s2) && (s1 - s2 <= 32768)) ||
                   ((s1 < s2) && (s2 - s1 > 32768));
        }

        private static int SequenceDiff(ushort newer, ushort older)
        {
            if (newer >= older)
                return newer - older;
            return 65536 + newer - older;
        }
    }
}
