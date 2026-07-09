namespace ShootingGame.Shared.Ability
{
    /// <summary>
    /// 能力事件类型。
    /// </summary>
    public enum AbilityEventType : byte
    {
        /// <summary>请求激活（客户端→服务端）</summary>
        RequestActivate = 0,
        /// <summary>服务端确认激活（服务端→客户端）</summary>
        ConfirmActivate = 1,
        /// <summary>服务端拒绝激活（服务端→客户端）</summary>
        RejectActivate = 2,
        /// <summary>正常结束</summary>
        Deactivate = 3,
        /// <summary>强制取消</summary>
        Cancel = 4,
    }

    /// <summary>
    /// 能力事件消息：用于客户端和服务端之间的实时能力同步。
    /// </summary>
    public struct AbilityEventData
    {
        public byte PlayerId;
        public ushort InstanceId;
        public byte AssetId;
        public AbilityEventType EventType;

        public AbilityEventData(byte playerId, ushort instanceId, byte assetId, AbilityEventType eventType)
        {
            PlayerId = playerId;
            InstanceId = instanceId;
            AssetId = assetId;
            EventType = eventType;
        }
    }
}
