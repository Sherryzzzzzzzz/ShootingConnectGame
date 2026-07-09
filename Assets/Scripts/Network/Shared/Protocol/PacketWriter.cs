// 数据包写入器
using System;
using ShootingGame.Shared.Math;

namespace ShootingGame.Shared.Protocol
{
    /// <summary>
    /// 轻量级二进制写入器，用于网络消息
    /// </summary>
    public class PacketWriter
    {
        private byte[] _buffer;
        private int _pos;

        public PacketWriter(int capacity = 16384)
        {
            _buffer = new byte[capacity];
            _pos = 0;
        }

        public int Position => _pos;

        public void Reset() => _pos = 0;

        /// <summary>
        /// 返回写入字节的副本
        /// </summary>
        public byte[] ToArray()
        {
            var result = new byte[_pos];
            Buffer.BlockCopy(_buffer, 0, result, 0, _pos);
            return result;
        }

        /// <summary>
        /// 返回内部缓冲区和写入长度
        /// </summary>
        public (byte[] buffer, int length) GetBuffer() => (_buffer, _pos);

        private void EnsureCapacity(int additionalBytes)
        {
            int required = _pos + additionalBytes;
            if (required <= _buffer.Length) return;
            int newSize = _buffer.Length * 2;
            while (newSize < required) newSize *= 2;
            var newBuffer = new byte[newSize];
            Buffer.BlockCopy(_buffer, 0, newBuffer, 0, _pos);
            _buffer = newBuffer;
        }

        public void WriteByte(byte value)
        {
            EnsureCapacity(1);
            _buffer[_pos++] = value;
        }

        public void WriteBool(bool value)
        {
            EnsureCapacity(1);
            _buffer[_pos++] = value ? (byte)1 : (byte)0;
        }

        public void WriteInt16(short value)
        {
            EnsureCapacity(2);
            _buffer[_pos++] = (byte)(value & 0xFF);
            _buffer[_pos++] = (byte)((value >> 8) & 0xFF);
        }

        public void WriteUInt16(ushort value)
        {
            EnsureCapacity(2);
            _buffer[_pos++] = (byte)(value & 0xFF);
            _buffer[_pos++] = (byte)((value >> 8) & 0xFF);
        }

        public void WriteInt32(int value)
        {
            EnsureCapacity(4);
            _buffer[_pos++] = (byte)(value & 0xFF);
            _buffer[_pos++] = (byte)((value >> 8) & 0xFF);
            _buffer[_pos++] = (byte)((value >> 16) & 0xFF);
            _buffer[_pos++] = (byte)((value >> 24) & 0xFF);
        }

        public void WriteUInt32(uint value)
        {
            EnsureCapacity(4);
            _buffer[_pos++] = (byte)(value & 0xFF);
            _buffer[_pos++] = (byte)((value >> 8) & 0xFF);
            _buffer[_pos++] = (byte)((value >> 16) & 0xFF);
            _buffer[_pos++] = (byte)((value >> 24) & 0xFF);
        }

        public void WriteInt64(long value)
        {
            WriteUInt32((uint)(value & 0xFFFFFFFF));
            WriteUInt32((uint)((value >> 32) & 0xFFFFFFFF));
        }

        public void WriteUInt64(ulong value)
        {
            WriteUInt32((uint)(value & 0xFFFFFFFF));
            WriteUInt32((uint)((value >> 32) & 0xFFFFFFFF));
        }

        public void WriteString(string value)
        {
            if (value == null) value = "";
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(value);
            EnsureCapacity(4 + bytes.Length);
            WriteInt32(bytes.Length);
            Buffer.BlockCopy(bytes, 0, _buffer, _pos, bytes.Length);
            _pos += bytes.Length;
        }

        public void WriteFloat(float value)
        {
            EnsureCapacity(4);
            byte[] bytes = BitConverter.GetBytes(value);
            Buffer.BlockCopy(bytes, 0, _buffer, _pos, 4);
            _pos += 4;
        }

        public void WriteVec2(Vec2 v)
        {
            WriteFloat(v.x);
            WriteFloat(v.y);
        }

        public void WriteVec3(Vec3 v)
        {
            WriteFloat(v.x);
            WriteFloat(v.y);
            WriteFloat(v.z);
        }

        public void WriteQuat(Quat q)
        {
            WriteFloat(q.x);
            WriteFloat(q.y);
            WriteFloat(q.z);
            WriteFloat(q.w);
        }

        /// <summary>
        /// 写入原始字节数组。
        /// </summary>
        public void WriteBytes(byte[] data, int offset, int count)
        {
            if (data == null || count <= 0) return;
            EnsureCapacity(count);
            Buffer.BlockCopy(data, offset, _buffer, _pos, count);
            _pos += count;
        }
    }
}