# Spec: Standalone C# Server

## Purpose

Authoritative game server as a standalone C# console application. Runs the same shared simulation as the client. No Unity dependency.

## Requirements

### Runtime

- .NET 8 console application.
- References `ShootingGame.Shared.dll`.
- Single-threaded game loop with dedicated network receive thread.

### Game Loop

```
Initialize:
    Load collision data (collision.bin) → CollisionWorld
    Start UDP listener on port 7777
    Start receive thread → ConcurrentQueue<ReceivedPacket>

Main loop (60 ticks/sec):
    1. DrainReceiveQueue() → process connection requests, store inputs
    2. For each connected player:
         input = GetLatestInput(playerId)  // or last input if none received
         player.snapshot = PlayerSimulation.Simulate(player.snapshot, input, tickDelta)
    3. ProcessFireRequests(lagCompensation)
    4. worldHistory.Store(currentTick, TakeWorldSnapshot())
    5. For each connected player:
         SendWorldState(player, worldSnapshot, lastProcessedInput[playerId])
    6. ProcessReliableRetransmits()
    7. CheckTimeouts() → disconnect silent clients
    8. SleepUntilNextTick() // precise timing using Stopwatch
```

### Tick Timing

- Use `System.Diagnostics.Stopwatch` for high-resolution timing.
- Target: 60 ticks/sec (16.67ms per tick).
- If a tick runs over, execute next tick immediately (catch up). Cap catch-up to 3 ticks to prevent spiral.
- Use `Thread.Sleep(1)` + spin-wait for sub-millisecond precision.

### Player Management

- Max 2 players for now.
- Player 0 and Player 1 assigned on connection.
- If a player disconnects, their slot opens for reconnection.
- Spawn positions: predefined per player index.

### Input Buffer

- Per-player ring buffer of InputFrame (capacity 128).
- Server stores inputs indexed by client tick number.
- If input is missing for current server tick (packet loss), reuse last received input.
- Track `lastProcessedInputTick` per player — included in WorldStateMessage for client reconciliation.

### World History (for Lag Compensation)

- Ring buffer of `WorldSnapshot` (capacity 64 = ~1 second).
- Each snapshot stores all player positions and collider data at that tick.
- Indexed by server tick number.

### Startup Configuration

Command-line arguments or config file:
- `--port 7777` (listen port)
- `--tickrate 60` (tick rate)
- `--collision collision.bin` (collision data path)
- `--max-players 2`

### Logging

- Console output: connection/disconnection, damage events, tick warnings (slow tick).
- No external logging dependency. Simple timestamped console writes.

### Graceful Shutdown

- Handle Ctrl+C (Console.CancelKeyPress).
- Send Disconnect to all connected clients.
- Close UDP socket.
