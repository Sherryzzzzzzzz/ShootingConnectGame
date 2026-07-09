// 数据包读取器
using System;
using ShootingGame.Shared.Math;

namespace ShootingGame.Shared.Protocol
{
    /// <summary>
    /// 轻量级二进制读取器，用于网络消息
    /// </summary>
    public class PacketReader
    {
        private readonly byte[] _buffer;
        private int _pos;
        private readonly int _length;

        public PacketReader(byte[] data) : this(data, 0, data.Length) { }

        public PacketReader(byte[] data, int offset, int length)
        {
            _buffer = data;
            _pos = offset;
            _length = offset + length;
        }

        public int Position => _pos;
        public int Remaining => _length - _pos;

        private void CheckRemaining(int count)
        {
            if (_pos + count > _length)
                throw new System.ArgumentOutOfRangeException($"PacketReader: not enough data (need {count}, remaining {Remaining})");
        }

        public byte ReadByte()
        {
            CheckRemaining(1);
            return _buffer[_pos++];
        }

        public bool ReadBool()
        {
            CheckRemaining(1);
            return _buffer[_pos++] != 0;
        }

        public short ReadInt16()
        {
            CheckRemaining(2);
            short value = (short)(_buffer[_pos] | (_buffer[_pos + 1] << 8));
            _pos += 2;
            return value;
        }

        public ushort ReadUInt16()
        {
            CheckRemaining(2);
            ushort value = (ushort)(_buffer[_pos] | (_buffer[_pos + 1] << 8));
            _pos += 2;
            return value;
        }

        public int ReadInt32()
        {
            CheckRemaining(4);
            int value = _buffer[_pos]
                | (_buffer[_pos + 1] << 8)
                | (_buffer[_pos + 2] << 16)
                | (_buffer[_pos + 3] << 24);
            _pos += 4;
            return value;
        }

        public uint ReadUInt32()
        {
            CheckRemaining(4);
            uint value = (uint)(_buffer[_pos]
                | (_buffer[_pos + 1] << 8)
                | (_buffer[_pos + 2] << 16)
                | (_buffer[_pos + 3] << 24));
            _pos += 4;
            return value;
        }

        public long ReadInt64()
        {
            uint low = ReadUInt32();
            uint high = ReadUInt32();
            return (long)(((ulong)high << 32) | low);
        }

        public ulong ReadUInt64()
        {
            uint low = ReadUInt32();
            uint high = ReadUInt32();
            return ((ulong)high << 32) | low;
        }

        public string ReadString()
        {
            int length = ReadInt32();
            if (length == 0) return "";
            if (length < 0 || length > Remaining)
                throw new System.ArgumentOutOfRangeException($"PacketReader.ReadString: invalid string length {length} (remaining {Remaining})");
            string value = System.Text.Encoding.UTF8.GetString(_buffer, _pos, length);
            _pos += length;
            return value;
        }

        public float ReadFloat()
        {
            CheckRemaining(4);
            float value = BitConverter.ToSingle(_buffer, _pos);
            _pos += 4;
            return value;
        }

        public Vec2 ReadVec2() => new Vec2(ReadFloat(), ReadFloat());
        public Vec3 ReadVec3() => new Vec3(ReadFloat(), ReadFloat(), ReadFloat());
        public Quat ReadQuat() => new Quat(ReadFloat(), ReadFloat(), ReadFloat(), ReadFloat());

        /// <summary>
        /// 读取指定数量的原始字节。
        /// </summary>
        public byte[] ReadBytes(int count)
        {
            if (count <= 0) return Array.Empty<byte>();
            CheckRemaining(count);
            var result = new byte[count];
            Buffer.BlockCopy(_buffer, _pos, result, 0, count);
            _pos += count;
            return result;
        }
    }
}