# Spec: Shared Library Foundation

## Purpose

Pure C# class library (`ShootingGame.Shared`) containing all simulation logic, physics, protocol, and data types shared between Unity client and standalone server. Zero Unity dependencies.

## Requirements

### Math Types

- `Vec3` struct: x, y, z (float). Operators: +, -, *, /, magnitude, sqrMagnitude, normalized, Dot, Cross, Lerp, Distance, ClampMagnitude, zero/one/up/forward/right constants.
- `Vec2` struct: x, y (float). Same operator set where applicable.
- `Quat` struct: x, y, z, w (float). Euler construction, LookRotation, Slerp, RotateTowards, identity constant, operator * (rotate vector).
- `GameMath` static utility: Sqrt, Abs, Clamp, Lerp, InverseLerp, Min, Max, Atan2, Sin, Cos, PI, Deg2Rad, Rad2Deg.
- Unity client provides implicit conversion: `Vec3 ↔ UnityEngine.Vector3`, `Quat ↔ UnityEngine.Quaternion` (defined client-side, not in shared lib).

### Core Data Types

- `InputFrame`: tick (int), movement (Vec2), jump (bool), run (bool), aim (bool), fire (bool), aimYaw (float), aimPitch (float).
- `PlayerSnapshot`: tick (int), position (Vec3), rotation (Quat), velocity (Vec3), verticalVelocity (float), isGrounded (bool), playerState (enum: Ground/Sky/Aim), fireCooldown (float), health (byte).
- `WorldSnapshot`: tick (int), players (PlayerSnapshot[]).
- `GameConstants`: static class with TickRate (60), TickDelta (1/60f), MoveSpeed (6f), RunMultiplier (1.5f), Gravity (-20f), JumpForce (8f), MaxHealth (100), FireRate, SlopeLimit (45f), etc.

### Project Structure

```
ShootingGame.Shared/
├── ShootingGame.Shared.csproj   (net8.0 or netstandard2.1)
├── Math/
│   ├── Vec3.cs
│   ├── Vec2.cs
│   ├── Quat.cs
│   └── GameMath.cs
├── Simulation/
│   ├── InputFrame.cs
│   ├── PlayerSnapshot.cs
│   ├── WorldSnapshot.cs
│   ├── PlayerSimulation.cs
│   ├── GameConstants.cs
│   └── PlayerStateLogic.cs
├── Physics/
│   ├── AABB.cs
│   ├── Capsule.cs
│   ├── Ray.cs
│   ├── HitResult.cs
│   ├── CollisionWorld.cs
│   ├── KinematicMover.cs
│   └── Raycast.cs
├── Protocol/
│   ├── MessageType.cs
│   ├── PacketWriter.cs
│   ├── PacketReader.cs
│   ├── InputMessage.cs
│   └── WorldStateMessage.cs
└── StateMachine/
    ├── IPlayerState.cs
    ├── PlayerStateMachine.cs
    ├── GroundState.cs
    ├── SkyState.cs
    └── AimState.cs
```

### Target Framework

- `netstandard2.1` preferred (compatible with Unity 2021+ and modern .NET server).
- If netstandard2.1 causes issues, fallback to `net8.0` for server and keep a Unity-compatible copy.
