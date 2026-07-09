# Spec: Kinematic Character Controller

## Purpose

Replace Unity's `CharacterController.Move()` with a custom kinematic mover that runs in the shared library. Must produce identical results on client and server.

## Requirements

### Player Collider

- Capsule shape: radius 0.3, height 1.8 (match current CharacterController settings).
- Origin at capsule center (feet at position.y - height/2 + radius).

### Movement Algorithm (KinematicMover.Move)

Input: current position (Vec3), desired movement (Vec3), CollisionWorld.
Output: new position (Vec3), grounded (bool).

Steps:
1. **Sweep test**: Move capsule along desired movement vector. Find first AABB intersection.
2. **Slide**: If hit, move to contact point, then project remaining movement onto the surface plane (subtract normal component). Repeat up to 3 iterations to handle corners.
3. **Ground check**: After horizontal movement, cast a short sphere downward (distance 0.1) from capsule bottom. If hit and slope angle < 45 degrees, player is grounded.
4. **Snap to ground**: If grounded and not jumping, snap capsule to ground surface to prevent floating on slopes.

### Capsule vs AABB Sweep Test

- Use Minkowski sum approach: expand AABB by capsule radius, then sweep a line segment (capsule axis simplified to point for v1, or full capsule for v2).
- v1 simplification: treat player as sphere (bottom of capsule) for sweep. Acceptable for box-only worlds.
- Return: hit (bool), fraction (float 0-1), normal (Vec3), point (Vec3).

### Gravity & Jump

- Gravity applied in PlayerSimulation, not in KinematicMover.
- KinematicMover only handles collision resolution for a given movement vector.
- Jump: sets verticalVelocity = sqrt(-2 * gravity * jumpHeight). Gravity accumulates each tick.
- When grounded and not jumping: verticalVelocity = small downward value (-2f) to maintain ground contact.

### Collision World

- `CollisionWorld` holds a `List<AABB>` loaded from exported collision data.
- Provides: `SweepSphere(origin, direction, radius, maxDistance) → HitResult[]`
- Provides: `Raycast(origin, direction, maxDistance) → HitResult`
- Provides: `OverlapCapsule(position, height, radius) → bool` (for spawn validation)
- No spatial acceleration structure in v1 (brute force over all AABBs). Add BVH later if perf requires.

### Collision Data Format

Binary file (`collision.bin`):
```
[int32: count]
[AABB * count]:
    float32 minX, minY, minZ
    float32 maxX, maxY, maxZ
```

### Edge Cases

- Player stuck in geometry on spawn → push out using depenetration (find overlap, push along shortest axis).
- Falling through floor → ground check every tick, snap when velocity is downward and ground is detected.
- Sliding along wall corners → 3-iteration slide loop handles this.
