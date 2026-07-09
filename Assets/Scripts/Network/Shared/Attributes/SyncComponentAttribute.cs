using System;

namespace ShootingGame.Network
{
    /// <summary>
    /// 标记一个 struct 为网络同步组件，Source Generator 会为其生成序列化代码。
    /// 仅在 partial struct 上使用。
    /// </summary>
    [AttributeUsage(AttributeTargets.Struct)]
    public class SyncComponentAttribute : Attribute
    {
        /// <summary>
        /// 显式指定 ComponentTypeId（不指定则由 Source Generator 自动分配）。
        /// </summary>
        public byte ComponentTypeId { get; set; }
    }
}
