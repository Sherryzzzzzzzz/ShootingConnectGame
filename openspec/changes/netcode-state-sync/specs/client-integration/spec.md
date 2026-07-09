# Spec: Client Integration (Unity)

## Purpose

Refactor the Unity client to use the shared simulation library, implement prediction/rollback, render remote players via interpolation, and connect to the standalone server.

## Requirements

### Replaced Components

| Old | New | Notes |
|-----|-----|-------|
| `CharacterController` | Removed | Replaced by shared KinematicMover |
| `PlayerController.Simulate()` | Calls `PlayerSimulation.Simulate()` from shared lib | Thin wrapper |
| `PlayerController.BuildLocalInput()` | Stays, but adds aimYaw/aimPitch | Still reads from Unity Input |
| `PlayerModel.StateMachineTick()` | Split: logic in shared, animation driven by snapshot | |
| `SimpleClient` (TCP) | `NetworkClient` (UDP) using shared protocol | |
| `Bullet.cs` | Client-side VFX only | No physics bullet; hitscan is server-side |
| `BulletManager` | Removed or repurposed for VFX-only tracers | |

### NetworkClient

- Connects to server via UDP.
- Sends InputMessage every tick (unreliable).
- Receives WorldStateMessage (unreliable) and DamageEvent (reliable).
- Background receive thread + ConcurrentQueue + drain in Update().
- Exposes: `void SendInput(InputFrame)`, `WorldStateMessage GetLatestWorldState()`, `event Action<DamageEvent> OnDamage`.

### Local Player Controller (Refactored)

```
Update() tick loop (same accumulator pattern as now):
    input = BuildLocalInput(currentTick)
    inputHistory.Store(currentTick, input)
    networkClient.SendInput(input)

    // Predict
    currentSnapshot = PlayerSimulation.Simulate(currentSnapshot, input, tickDelta, collisionWorld)
    snapshotHistory.Store(currentTick, currentSnapshot)

    // Apply to presentation
    ApplySnapshot(currentSnapshot)

    // Check for server reconciliation
    if (networkClient.HasNewWorldState()):
        Reconcile(networkClient.GetLatestWorldState())
```

### Remote Player Controller

- Separate component: `RemotePlayerController`.
- Receives snapshots from WorldStateMessage.
- Stores in interpolation buffer (ring buffer of timestamped snapshots).
- Renders at `currentTime - interpolationDelay` (default 100ms = 6 ticks).
- Lerps position, Slerps rotation between two bracketing snapshots.
- Animation state driven by snapshot's `playerState` and `velocity.magnitude`.

### Animation Bridging

- Current animation states (IdleState, MoveState, etc.) remain in Unity.
- They no longer read input directly — instead they read the PlayerSnapshot.
- Mapping:
  - `snapshot.velocity.magnitude < 0.1 && snapshot.isGrounded` → Idle
  - `snapshot.velocity.magnitude >= 0.1 && snapshot.isGrounded` → Move (blend by speed)
  - `snapshot.playerState == Aim` → Aim
  - `!snapshot.isGrounded && snapshot.verticalVelocity > 0` → Jump
  - `!snapshot.isGrounded && snapshot.verticalVelocity <= 0` → Fall
- This decouples animation from input, making it work for both local and remote players.

### Vec3/Quat Conversion

Defined in client code (not shared lib):
```csharp
// Extension methods or implicit operators
public static Vec3 ToShared(this Vector3 v) => new Vec3(v.x, v.y, v.z);
public static Vector3 ToUnity(this Vec3 v) => new Vector3(v.x, v.y, v.z);
// Same for Quat ↔ Quaternion
```

### Collision World Loading

- Client loads the same `collision.bin` file as the server.
- Populates a `CollisionWorld` instance used by local prediction.
- This ensures prediction matches server simulation exactly.

### Hit Feedback

- On receiving `DamageEvent` from server:
  - If `targetId == localPlayerId`: flash screen red, play hurt sound, update health UI.
  - If `shooterId == localPlayerId`: play hit marker VFX/SFX at hitPoint.
- Muzzle flash / fire SFX remain client-side, triggered by local input (no server confirmation needed for VFX).

### Camera

- Cinemachine stays as-is.
- Camera follows the visual position (smoothed after rollback), not the raw simulation position.
- Aim camera activation driven by `snapshot.playerState == Aim`.

### UI

- Health bar driven by `localSnapshot.health` (predicted) and corrected on server DamageEvent.
- Crosshair/aim image driven by `snapshot.playerState == Aim`.
- Connection status indicator (connected / disconnected / reconnecting).
- Ping display (from RTT estimation).
