# Spec: Lag Compensation (Hitscan)

## Purpose

Server-side hit detection that rewinds the world to the shooting player's perceived time, so hits feel fair regardless of latency.

## Requirements

### Fire Detection

- When server processes an InputFrame with `fire = true` and `fireCooldown <= 0`:
  1. Record a `FireRequest { playerId, tick, origin, direction }`.
  2. `origin` = player's fire point position at current server tick.
  3. `direction` = computed from player's aimYaw and aimPitch (from input).

### Rewind Process

1. Estimate the client's perceived tick: `compensatedTick = serverTick - (clientRTT / 2 / tickDelta)`.
2. Clamp: `compensatedTick = max(compensatedTick, serverTick - maxCompensationTicks)`.
3. `maxCompensationTicks = 12` (~200ms at 60 tick). Clients with higher latency get partial compensation.
4. Retrieve `worldHistory[compensatedTick]`.
5. If history entry missing (too old), use oldest available snapshot.

### Hitscan Execution

1. From the historical snapshot, build a list of target capsules (all players except the shooter).
2. Player capsule: center at `snapshot.position + (0, height/2, 0)`, height 1.8, radius 0.3.
3. Execute `Raycast.CapsuleIntersection(ray, capsule)` for each target.
4. If multiple hits, take the closest one.
5. Return: `HitscanResult { hit, targetId, hitPoint, distance }`.

### Ray vs Capsule Intersection

- Treat capsule as: cylinder (for the shaft) + 2 hemispheres (top and bottom caps).
- Simplification for v1: treat capsule as an expanded AABB (min/max + radius padding). Less accurate but simpler.
- Upgrade path: true ray-capsule intersection using closest point on line segment.

### Damage Application

- On hit: `target.health -= damage` (damage = weapon damage constant, e.g., 25).
- If health <= 0: player dies. Broadcast death event (reliable). Respawn after delay.
- Broadcast `DamageEvent { targetId, damage, newHealth, hitPoint }` via reliable channel.
- Client receiving DamageEvent plays hit marker VFX/SFX.

### Anti-Abuse Safeguards

- Maximum fire rate enforced server-side (regardless of client input).
- Validate aim direction: if angle between player facing and fire direction > 90 degrees, reject.
- Rate limit fire requests: max 1 per fireCooldown period.
- Log suspicious patterns (firing faster than possible, impossible aim angles).

### Hitscan Range

- Maximum hitscan distance: 200 units (configurable in GameConstants).
- Beyond this range, ray does not hit players.
