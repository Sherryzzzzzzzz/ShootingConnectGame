using System;
using ShootingGame.Shared.Math;
using ShootingGame.Shared.Protocol;

namespace ShootingGame.Network
{
    /// <summary>
    /// 泛型网络同步变量。自动追踪脏标记，支持增量同步。
    /// 使用时放在 NetworkBehaviour 子类的字段中。
    /// </summary>
    public class NetVar<T> where T : struct, IEquatable<T>
    {
        private T _value;
        private bool _dirty;

        public T Value
        {
            get => _value;
            set
            {
                if (!_value.Equals(value))
                {
                    _value = value;
                    _dirty = true;
                    OnChanged?.Invoke(_value);
                }
            }
        }

        public bool IsDirty => _dirty;
        public event Action<T> OnChanged;

        public NetVar(T initial = default)
        {
            _value = initial;
            _dirty = true; // 初始为脏，首次同步时会全量发送
        }

        public void ClearDirty() => _dirty = false;

        /// <summary>写入值到 PacketWriter。仅支持 int/float/bool/Vec3。</summary>
        public void WriteTo(PacketWriter writer)
        {
            if (typeof(T) == typeof(int)) writer.WriteInt32((int)(object)_value);
            else if (typeof(T) == typeof(float)) writer.WriteFloat((float)(object)_value);
            else if (typeof(T) == typeof(bool)) writer.WriteBool((bool)(object)_value);
            else if (typeof(T) == typeof(Vec3)) writer.WriteVec3((Vec3)(object)_value);
            else throw new NotSupportedException($"NetVar<{typeof(T).Name}> not supported");
        }

        /// <summary>从 PacketReader 读取值。</summary>
        public void ReadFrom(PacketReader reader)
        {
            object val;
            if (typeof(T) == typeof(int)) val = reader.ReadInt32();
            else if (typeof(T) == typeof(float)) val = reader.ReadFloat();
            else if (typeof(T) == typeof(bool)) val = reader.ReadBool();
            else if (typeof(T) == typeof(Vec3)) val = reader.ReadVec3();
            else throw new NotSupportedException($"NetVar<{typeof(T).Name}> not supported");

            var newVal = (T)val;
            if (!_value.Equals(newVal))
            {
                _value = newVal;
                OnChanged?.Invoke(_value);
            }
        }

        public static implicit operator T(NetVar<T> v) => v._value;
    }
}
