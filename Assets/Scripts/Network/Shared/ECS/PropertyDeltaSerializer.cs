using ShootingGame.Shared.ECS.Components;
using ShootingGame.Shared.Math;
using ShootingGame.Shared.Protocol;

namespace ShootingGame.Shared.ECS
{
    /// <summary>
    /// Serializes only dirty properties of an ECS component, rather than the entire component.
    /// This reduces P-Frame payload size significantly.
    ///
    /// Wire format for a component delta:
    ///   [componentTypeId:1B] [propertyCount:1B] [(propIndex:1B, propData:var)...]
    ///
    /// Where propData is the native binary representation of that property.
    /// </summary>
    public static class PropertyDeltaSerializer
    {
        /// <summary>
        /// Serialize only dirty properties of a component into the writer.
        /// Each component type needs its own serialize method.
        /// </summary>
        public static void SerializeMovementComponent(PacketWriter writer, ref MovementComponent comp)
        {
            if (!comp.Dirty.HasDirty) return;

            writer.WriteByte((byte)ComponentTypeId.Get<MovementComponent>());
            int dirtyCount = CountDirty(comp.Dirty.DirtyMask, MovementPropertyCount);
            writer.WriteByte((byte)dirtyCount);

            if (comp.Dirty.IsDirty(0)) { writer.WriteVec3(comp.Velocity); writer.WriteByte(0); }
            if (comp.Dirty.IsDirty(1)) { writer.WriteFloat(comp.VerticalVelocity); writer.WriteByte(1); }
            if (comp.Dirty.IsDirty(2)) { writer.WriteBool(comp.IsGrounded); writer.WriteByte(2); }
            if (comp.Dirty.IsDirty(3)) { writer.WriteFloat(comp.MaxMoveSpeed); writer.WriteByte(3); }

            comp.Dirty.ResetAll();
        }

        /// <summary>
        /// Deserialize dirty properties from the reader into the component.
        /// </summary>
        public static void DeserializeMovementComponent(PacketReader reader, ref MovementComponent comp)
        {
            int count = reader.ReadByte();
            for (int i = 0; i < count; i++)
            {
                byte propIndex = reader.ReadByte();
                switch (propIndex)
                {
                    case 0: comp.Velocity = reader.ReadVec3(); break;
                    case 1: comp.VerticalVelocity = reader.ReadFloat(); break;
                    case 2: comp.IsGrounded = reader.ReadBool(); break;
                    case 3: comp.MaxMoveSpeed = reader.ReadFloat(); break;
                }
            }
        }

        public static void SerializeHealthComponent(PacketWriter writer, ref HealthComponent comp)
        {
            if (!comp.Dirty.HasDirty) return;

            writer.WriteByte((byte)ComponentTypeId.Get<HealthComponent>());
            writer.WriteByte((byte)CountDirty(comp.Dirty.DirtyMask, HealthPropertyCount));

            if (comp.Dirty.IsDirty(0)) { writer.WriteByte(comp.Current); writer.WriteByte(0); }

            comp.Dirty.ResetAll();
        }

        public static void DeserializeHealthComponent(PacketReader reader, ref HealthComponent comp)
        {
            int count = reader.ReadByte();
            for (int i = 0; i < count; i++)
            {
                byte propIndex = reader.ReadByte();
                switch (propIndex)
                {
                    case 0: comp.Current = reader.ReadByte(); break;
                }
            }
        }

        public static void SerializeTransformComponent(PacketWriter writer, ref TransformComponent comp)
        {
            if (!comp.Dirty.HasDirty) return;

            writer.WriteByte((byte)ComponentTypeId.Get<TransformComponent>());
            writer.WriteByte((byte)CountDirty(comp.Dirty.DirtyMask, TransformPropertyCount));

            if (comp.Dirty.IsDirty(0)) { writer.WriteVec3(comp.Position); writer.WriteByte(0); }
            if (comp.Dirty.IsDirty(1)) { writer.WriteQuat(comp.Rotation); writer.WriteByte(1); }

            comp.Dirty.ResetAll();
        }

        public static void DeserializeTransformComponent(PacketReader reader, ref TransformComponent comp)
        {
            int count = reader.ReadByte();
            for (int i = 0; i < count; i++)
            {
                byte propIndex = reader.ReadByte();
                switch (propIndex)
                {
                    case 0: comp.Position = reader.ReadVec3(); break;
                    case 1: comp.Rotation = reader.ReadQuat(); break;
                }
            }
        }

        // Property allocation by component type
        private const int MovementPropertyCount = 4;
        private const int HealthPropertyCount = 2;
        private const int TransformPropertyCount = 2;

        private static int CountDirty(ulong mask, int maxProps)
        {
            int count = 0;
            ulong check = mask;
            for (int i = 0; i < maxProps; i++)
            {
                if ((check & 1) != 0) count++;
                check >>= 1;
            }
            return count;
        }
    }
}
