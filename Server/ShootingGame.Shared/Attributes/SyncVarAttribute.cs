using System;

namespace ShootingGame.Network
{
    /// <summary>
    /// 标记 struct 中的字段为同步变量。Source Generator 会为其生成脏检测和序列化代码。
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class SyncVarAttribute : Attribute
    {
        /// <summary>
        /// 当此字段变化时在 NetworkBehaviour 上调用的方法名。
        /// 方法签名为 void On{FieldName}Changed(T oldValue, T newValue)
        /// </summary>
        public string HookMethodName { get; set; }
    }
}
