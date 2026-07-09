namespace ShootingGame.Shared.Protocol
{
    public enum MessageType : byte
    {
        ConnectionRequest = 1,
        ConnectionAccepted = 2,
        PlayerJoined = 3,
        PlayerLeft = 4,
        InputMessage = 10,
        WorldStateMessage = 11,
        DamageEvent = 20,
        Disconnect = 30,
        Heartbeat = 40,
        AbilityEvent = 21
    }
}
