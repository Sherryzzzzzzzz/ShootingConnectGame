# Spec: Client-Side Prediction & Rollback

## Purpose

Make local player movement feel instant despite network latency, and correct prediction errors seamlessly when server state diverges.

## Requirements

### Prediction

- Client executes `PlayerSimulation.Simulate()` immediately upon gathering input — does NOT wait for server confirmation.
- The predicted state is applied to presentation (transform, animation) instantly.
- Client maintains two ring buffers:
  - `inputHistory[tick]` — InputFrame for each tick (capacity: 128 ticks = ~2 seconds)
  - `snapshotHistory[tick]` — PlayerSnapshot after simulating each tick (capacity: 128 ticks)

### Input Redundancy

- Each InputMessage sent to server contains the current frame plus the previous 2 frames (3 total).
- This handles up to 2 consecutive packet drops without the server missing input.
- Server ignores input frames it has already processed (by checking tick number).

### Reconciliation (Rollback)

When client receives a WorldStateMessage from server:

1. Extract `lastProcessedInputTick` for this client (call it `S`).
2. Retrieve `snapshotHistory[S]` — the local prediction at tick S.
3. Compare `snapshotHistory[S].position` with `serverState.position`.
4. If `Distance(predicted, server) < 0.01f` AND all other fields match within tolerance → prediction correct, no action needed.
5. If mismatch exceeds tolerance → **rollback**:
   a. Set `currentState = serverState` (overwrite with authority).
   b. Re-simulate from tick `S+1` to `currentTick`:
      ```
      for t = S+1 to currentTick:
          currentState = PlayerSimulation.Simulate(currentState, inputHistory[t], tickDelta)
          snapshotHistory[t] = currentState
      ```
   c. Apply final `currentState` to presentation.
6. Discard `inputHistory` and `snapshotHistory` entries older than `S - 10` (keep small buffer for safety).

### Comparison Tolerances

| Field | Tolerance |
|-------|-----------|
| position | 0.01 units (Vec3 distance) |
| rotation | 0.5 degrees |
| verticalVelocity | 0.1 |
| velocity | 0.01 (Vec3 distance) |
| playerState enum | exact match |
| isGrounded | exact match |

If any field exceeds tolerance, trigger full rollback.

### Visual Smoothing

- After rollback, don't teleport the visual representation instantly.
- Maintain a `visualPosition` that Lerps toward `simulationPosition` over 100ms (~6 ticks).
- `visualPosition = Lerp(visualPosition, simulationPosition, 0.2f)` per tick.
- This hides small corrections. Large corrections (>2 units) snap instantly.

### Edge Cases

- Server state arrives out of order → discard if older than last processed server tick.
- Server state arrives for a tick we've already cleared from history → skip reconciliation (too old to correct).
- Multiple server states arrive in one frame → process only the most recent.
- Client is ahead of server by too many ticks → slow down tick rate slightly to let server catch up (tick drift correction).
