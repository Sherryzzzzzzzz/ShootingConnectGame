# Proposal: Netcode State Sync

## Problem

The project is currently a single-player TPS. We want to turn it into a 2-player online TPS with authoritative server, client-side prediction, and rollback reconciliation.

## Approach

**Architecture**: Authoritative dedicated C# server + Unity client with prediction/rollback.

**Key decisions made during exploration**:

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Sync model | State synchronization (server broadcasts world state) | Standard for FPS/TPS, scales to many players |
| Character controller | Self-written kinematic (replace CharacterController) | Deterministic, rollback-friendly, runs on server without Unity |
| Server runtime | Standalone C# console app (no Unity dependency) | Lightweight deployment, better performance, shared simulation DLL |
| Weapon type | Hitscan (instant raycast) | Simpler to synchronize, covers primary weapon |
| Hit detection | Server-side hitscan with lag compensation | Fair for all latency levels, anti-cheat friendly |
| Scene collision | Simple boxes (AABB) | Keeps custom physics tractable, matches current scene |
| Transport | UDP with reliable/unreliable channels | Required for real-time shooter |
| Initial scope | 2 players | Prove the full loop before scaling |

**Core idea**: Extract a pure C# shared library containing simulation, physics, and protocol code. Both client and server reference it. The same `Simulate()` function runs on both sides, ensuring prediction matches authority.

## Scope

### In scope
- Shared simulation library (pure C#, no Unity dependency)
- Custom kinematic character controller with AABB collision
- UDP networking layer (reliable + unreliable channels)
- Binary message protocol (input messages, world state messages)
- Standalone C# authoritative server
- Client-side prediction (local player executes input immediately)
- Rollback reconciliation (compare server state, re-simulate on mismatch)
- Remote player interpolation (render other players smoothly)
- Server-side hitscan with lag compensation (historical world snapshot rollback)
- Collision data export tool (Unity editor → shared format)
- 2-player session (connect, play, disconnect)

### Out of scope (for now)
- More than 2 players
- Projectile weapons (rockets, grenades)
- Matchmaking / lobby system
- Player authentication
- Anti-cheat beyond basic server validation
- Voice chat
- Character selection / loadouts
- Fixed-point math (floating point is acceptable with authoritative server)
- Complex terrain (mesh colliders, terrain collider)

## Risks

| Risk | Impact | Mitigation |
|------|--------|------------|
| Custom kinematic controller is complex | High | Start with flat ground + boxes only, add slopes later |
| Rollback with many re-simulation frames feels bad | Medium | Cap rollback window, smooth visual correction |
| Collision data export mismatches Unity visuals | Medium | Build editor validation tool to visualize exported colliders |
| UDP reliability layer bugs cause desyncs | High | Consider LiteNetLib as transport if self-written UDP proves too costly |
| Shared library math types diverge from Unity types | Low | Provide implicit conversion operators |
