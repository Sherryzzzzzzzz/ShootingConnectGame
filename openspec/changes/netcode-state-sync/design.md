# Design: Netcode State Sync

## System Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                                                                 │
│                    ShootingGame.Shared.dll                       │
│                    (Pure C# Class Library)                       │
│  ┌────────────┐ ┌────────────┐ ┌───────────┐ ┌──────────────┐  │
│  │ Simulation │ │  Physics   │ │ Protocol  │ │ StateMachine │  │
│  │            │ │            │ │           │ │              │  │
│  │ Simulate() │ │ Kinematic  │ │ Serialize │ │ Ground/Sky/  │  │
│  │ Snapshot   │ │ Colliders  │ │ Messages  │ │ Aim (logic)  │  │
│  │ InputFrame │ │ Raycast    │ │ InputMsg  │ │              │  │
│  └──────┬─────┘ │ AABB/Cap   │ │ StateMsg  │ └──────────────┘  │
│         │       └────────────┘ └───────────┘                    │
│         │                                                       │
└─────────┼───────────────────────────────────────────────────────┘
          │ referenced by both:
    ┌─────┴──────┐          ┌──────────┐
    │   Unity    │   UDP    │  C# CLI  │
    │   Client   │◀────────▶│  Server  │
    └────────────┘          └──────────┘
```

## Data Flow

### Per-Tick Flow (Server)

```
1. Network.ReceiveAll()
       │
2. For each player:
   │   input = inputBuffer.Get(playerId, currentTick)
   │   snapshot = PlayerSim.Simulate(snapshot, input, dt)
       │
3. BulletSim.TickAll(world)
       │
4. Process fire requests with lag compensation:
   │   historicalSnapshot = worldHistory[fireRequest.clientTick]
   │   hit = Raycast(origin, dir, historicalSnapshot.colliders)
   │   if hit → ApplyDamage()
       │
5. worldHistory.Store(currentTick, world.Snapshot())
       │
6. For each client:
       Network.SendWorldState(client, world, lastProcessedInput[client])
```

### Per-Tick Flow (Client - Local Player)

```
1. input = GatherInput() → InputFrame
       │
2. inputHistory.Store(currentTick, input)
       │
3. Network.SendInput(input + redundant previous N frames)
       │
4. prediction = PlayerSim.Simulate(currentState, input, dt)
       │
5. snapshotHistory.Store(currentTick, prediction)
       │
6. ApplyToPresentation(prediction)  // immediate response
       │
7. [When server state arrives for tick S]:
   │   localPrediction = snapshotHistory[S]
   │   if Distance(localPrediction.position, serverState.position) > threshold:
   │       // ROLLBACK
   │       state = serverState
   │       for tick = S+1 to currentTick:
   │           state = PlayerSim.Simulate(state, inputHistory[tick], dt)
   │           snapshotHistory[tick] = state
   │       ApplyToPresentation(state)
```

### Per-Tick Flow (Client - Remote Player)

```
1. Receive server state → remoteBuffer.Add(serverTick, state)
       │
2. renderTime = currentTime - interpolationDelay (2-3 ticks)
       │
3. Find two snapshots bracketing renderTime:
   │   before = remoteBuffer.GetBefore(renderTime)
   │   after  = remoteBuffer.GetAfter(renderTime)
       │
4. t = InverseLerp(before.time, after.time, renderTime)
       │
5. ApplyToPresentation(Lerp(before, after, t))
```

## Key Data Structures

### PlayerSnapshot (Shared)

```
struct PlayerSnapshot
    int         tick
    Vec3        position
    Quat        rotation
    Vec3        velocity
    float       verticalVelocity
    bool        isGrounded
    PlayerState playerState      // Ground, Sky, Aim
    float       fireCooldown
    byte        health
```

### InputFrame (Shared)

```
struct InputFrame
    int     tick
    Vec2    movement
    bool    jump
    bool    run
    bool    aim
    bool    fire
    float   aimYaw          // camera/aim direction (needed for server rotation)
    float   aimPitch
```

### WorldSnapshot (Shared)

```
struct WorldSnapshot
    int                      tick
    PlayerSnapshot[]         players
    BulletSnapshot[]         bullets      // active projectiles (if any in future)
```

## Custom Kinematic Character Controller

### Collision Primitives

Server-side physics uses simple primitives only:

```
AABB    { Vec3 min, Vec3 max }              // boxes, walls, floors
Capsule { Vec3 base, float height, radius } // player collider
Sphere  { Vec3 center, float radius }       // optional triggers
```

### KinematicMover Algorithm

```
Move(capsule, desiredMovement, collisionWorld):
    remaining = desiredMovement
    for iteration = 0 to 2:          // max 3 slides
        sweep = SweepCapsule(capsule, remaining, collisionWorld)
        if no hit:
            capsule.position += remaining
            break
        capsule.position += remaining * sweep.fraction
        remaining = SlideAlongSurface(remaining, sweep.normal)
        if remaining.magnitude < epsilon:
            break

GroundCheck(capsule, collisionWorld):
    cast sphere downward from capsule bottom by small distance (0.1)
    if hit and slope angle < slopeLimit:
        return { grounded: true, groundNormal: hit.normal }
    return { grounded: false }
```

### Collision Data Export (Editor Tool)

Unity Editor tool that:
1. Iterates all BoxColliders in the scene
2. Converts each to AABB in world space (position + scale + rotation → min/max)
3. Serializes to binary file (`fight_collision.bin`)
4. Server and client both load this file into `CollisionWorld`

## Networking Layer

### Transport

- UDP socket (System.Net.Sockets.UdpClient)
- Two virtual channels:
  - **Unreliable**: input messages, world state messages (sent every tick)
  - **Reliable**: connection handshake, player join/leave, damage events, health updates
- Reliable channel: sequence number + ACK bitmask + retransmit on timeout
- Fallback plan: swap to LiteNetLib if self-written reliable layer proves buggy

### Message Format

```
[1 byte: message type]
[2 bytes: sequence number]
[N bytes: payload (hand-written binary, no reflection)]
```

### Input Redundancy

Client sends the last 3 input frames per packet to handle packet loss:

```
InputMessage:
    [currentTick: uint32]
    [count: byte]           // typically 3
    [InputFrame * count]    // current + 2 previous
```

Server uses the latest unprocessed frame, ignoring duplicates.

## Lag Compensation (Hitscan)

### Server World History

```
Ring buffer: WorldSnapshot[HISTORY_SIZE]   // ~60 entries = 1 second at 60 tick
```

### Fire Request Processing

```
When server receives input with fire=true:
    1. Compute clientRTT / 2 → estimate client's perceived tick
    2. Clamp to maxCompensationTicks (e.g., 12 = 200ms)
    3. Retrieve worldHistory[compensatedTick]
    4. Build temporary collider set from historical player positions
       (exclude the shooter's own collider)
    5. Raycast from shooter's fire point in aim direction
    6. If hit → apply damage, broadcast hit event (reliable)
    7. If miss → broadcast miss event (unreliable, for impact VFX)
```

## Client-Server Separation in Unity

### What stays in Unity (client only)

- Input gathering (Input System → InputFrame)
- Animation (Animancer, Animator) — driven by snapshot state
- Camera (Cinemachine) — driven by snapshot position/rotation
- VFX / Audio (muzzle flash, bullet impact, shells)
- UI (crosshair, health bar)
- Remote player interpolation rendering

### What moves to Shared

- PlayerSimulation.Simulate()
- State machine logic (transitions, state behavior — no animation code)
- KinematicMover + CollisionWorld
- Raycast / hitscan logic
- InputFrame, PlayerSnapshot, WorldSnapshot
- Protocol serialization

### Existing code mapping

| Current file | Destination | Notes |
|---|---|---|
| `PlayerController.Simulate()` | Shared `PlayerSimulation.Simulate()` | Remove CharacterController dependency |
| `PlayerController.HandleGravity()` | Shared `PlayerSimulation` | Pure math |
| `PlayerController.HandleRotation()` | Shared `PlayerSimulation` | Pure math |
| `PlayerController.BuildLocalInput()` | Client only (stays in Unity) | Hardware input |
| `PlayerModel.StateMachineTick()` | Split: logic→Shared, animation→Client | |
| `StateBase / StateMachine` | Shared (rewrite without Unity deps) | |
| `PlayerGroundState/SkyState/AimState` | Shared (logic only, no animation/camera) | |
| `AnimationStates (Idle/Move/Jump...)` | Client only (presentation) | Driven by snapshot |
| `Bullet.cs` | Hitscan on server; VFX only on client | Replace projectile with instant raycast |
| `SimpleClient.cs` | Replace with UDP transport | |
| `InputFrame` | Shared (add aimYaw/aimPitch) | |
