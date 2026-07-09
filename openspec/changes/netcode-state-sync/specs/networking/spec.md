# Spec: Networking Layer

## Purpose

UDP-based transport layer supporting reliable and unreliable message delivery between Unity client and standalone C# server.

## Requirements

### Transport

- Uses `System.Net.Sockets.UdpClient` on both client and server.
- Server listens on a configurable port (default 7777).
- Client connects by sending a connection request to server IP:port.
- MTU limit: 1200 bytes per packet (safe for all networks). Messages exceeding this must be fragmented (unlikely for our message sizes).

### Packet Header

Every UDP packet starts with:
```
[byte:   channel]        // 0 = unreliable, 1 = reliable
[uint16: sequenceNumber] // per-channel, wrapping
[uint16: ack]            // last received sequence from other side
[uint32: ackBitfield]    // bitmask for the 32 sequences before ack
[byte:   messageType]
[bytes:  payload]
```

### Unreliable Channel

- Fire and forget. No retransmission.
- Used for: InputMessage (client→server), WorldStateMessage (server→client).
- Sequence number used for ordering only — receiver discards packets older than the latest received.

### Reliable Channel

- Sequence number + ACK + retransmit.
- Sender stores unacked packets in a send buffer.
- On receiving an ACK, mark corresponding packets as delivered.
- Retransmit unacked packets after 100ms (or 2x smoothed RTT, whichever is larger).
- Max retransmits: 10, then consider connection lost.
- Used for: ConnectionRequest, ConnectionAccepted, PlayerJoined, PlayerLeft, DamageEvent, Disconnect.

### Connection Lifecycle

```
Client                              Server
  │  ConnectionRequest (reliable)      │
  │ ──────────────────────────────────▶│
  │                                    │ Assign playerId (0 or 1)
  │  ConnectionAccepted { playerId }   │
  │◀────────────────────────────────── │
  │                                    │
  │  InputMessage (unreliable, 60/s)   │
  │ ──────────────────────────────────▶│
  │  WorldState (unreliable, 60/s)     │
  │◀────────────────────────────────── │
  │                                    │
  │  ... gameplay ...                  │
  │                                    │
  │  Disconnect (reliable)             │
  │ ──────────────────────────────────▶│
```

### Heartbeat

- Client sends heartbeat every 1 second if no other packets sent.
- Server considers client disconnected after 5 seconds of silence.
- Server sends heartbeat every 1 second if no world state sent (shouldn't happen during gameplay).

### RTT Estimation

- Each side tracks smoothed RTT using the ACK system.
- `smoothedRTT = 0.875 * smoothedRTT + 0.125 * sampleRTT` (TCP-style EWMA).
- RTT is included in WorldStateMessage so client knows its latency to server.

### Threading Model

- Server: dedicated receive thread, game loop on main thread, ConcurrentQueue to pass received packets to game loop.
- Client (Unity): receive on background thread, ConcurrentQueue, process in Update().

### Message Types

| Type | Direction | Channel | Content |
|------|-----------|---------|---------|
| ConnectionRequest | C→S | Reliable | protocol version |
| ConnectionAccepted | S→C | Reliable | playerId, tickRate, serverTick |
| PlayerJoined | S→C | Reliable | playerId |
| PlayerLeft | S→C | Reliable | playerId |
| InputMessage | C→S | Unreliable | InputFrame * N (redundant) |
| WorldStateMessage | S→C | Unreliable | WorldSnapshot + lastProcessedInputTick per player |
| DamageEvent | S→C | Reliable | targetId, damage, newHealth, hitPosition |
| Disconnect | C→S | Reliable | reason |
| Heartbeat | Both | Unreliable | timestamp |
