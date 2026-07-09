# Tasks: Netcode State Sync

## Phase 0: Shared Library Foundation

- [x] **0.1** Create `ShootingGame.Shared` C# class library project (netstandard2.1), set up solution structure with `ShootingGame.Server` console project referencing it
- [x] **0.2** Implement math types: `Vec2`, `Vec3`, `Quat`, `GameMath` with full operator overloads and utility methods
- [x] **0.3** Write unit tests for math types (dot, cross, normalize, Slerp, LookRotation, basic edge cases)
- [x] **0.4** Define core data types: `InputFrame`, `PlayerSnapshot`, `WorldSnapshot`, `GameConstants`
- [x] **0.5** Port `PlayerSimulation.Simulate()` from `PlayerController.Simulate()` — pure function taking snapshot + input → snapshot (no Unity dependency, no CharacterController)
- [x] **0.6** Port state machine to shared: `IPlayerState`, `PlayerStateMachine`, `GroundState`, `SkyState`, `AimState` (logic only, no animation)
- [x] **0.7** Write unit tests for `PlayerSimulation.Simulate()` — basic movement, gravity, jump, state transitions

## Phase 1: Kinematic Character Controller

- [x] **1.1** Implement `AABB` struct and `Ray` struct with `Ray vs AABB` intersection test
- [x] **1.2** Implement `Capsule` struct (or use sphere simplification for v1) and sweep test against AABB
- [x] **1.3** Implement `CollisionWorld` — holds list of AABBs, provides `SweepSphere()` and `Raycast()` queries
- [x] **1.4** Implement `KinematicMover.Move()` — sweep + 3-iteration slide + ground check
- [x] **1.5** Write unit tests: move into wall (slide), move into corner (double slide), ground detection, falling, jump arc
- [x] **1.6** Integrate `KinematicMover` into `PlayerSimulation.Simulate()` — replace direct position += velocity with mover.Move()
- [x] **1.7** Build Unity Editor tool: export all BoxColliders in scene to `collision.bin` (binary format)
- [x] **1.8** Export Fight scene collision data, verify by loading in test and checking AABB count/positions

## Phase 2: Protocol & Networking

- [x] **2.1** Implement binary serialization: `PacketWriter` and `PacketReader` (write/read int, float, byte, bool, Vec3, Quat)
- [x] **2.2** Define `MessageType` enum, implement `InputMessage` serialize/deserialize (with N redundant frames)
- [x] **2.3** Implement `WorldStateMessage` serialize/deserialize (player snapshots + lastProcessedInputTick per player)
- [x] **2.4** Implement `DamageEvent`, `ConnectionRequest`, `ConnectionAccepted`, `Disconnect` message types
- [x] **2.5** Write unit tests: round-trip serialize/deserialize for all message types
- [x] **2.6** Implement UDP transport: `UdpTransport` class wrapping UdpClient, send/receive raw bytes, background receive thread + ConcurrentQueue
- [x] **2.7** Implement reliable channel on top of UDP: sequence numbers, ACK bitmask, retransmit queue, RTT estimation
- [x] **2.8** Implement connection lifecycle: handshake (request → accepted), heartbeat, timeout detection
- [x] **2.9** Integration test: two processes exchange reliable + unreliable messages over localhost

## Phase 3: Standalone Server

- [x] **3.1** Create `ShootingGame.Server` console app entry point with tick loop (Stopwatch-based timing, 60 tick/sec)
- [x] **3.2** Integrate networking: listen for connections, assign player IDs, manage connected player list
- [x] **3.3** Implement server input buffer: per-player ring buffer, input retrieval with fallback to last input on miss
- [x] **3.4** Integrate `PlayerSimulation.Simulate()` into server tick — authoritative simulation for all connected players
- [x] **3.5** Load `collision.bin` into `CollisionWorld`, pass to simulation
- [x] **3.6** Broadcast `WorldStateMessage` to all clients each tick
- [x] **3.7** End-to-end test: client sends input → server simulates → client receives state (no prediction yet, just overwrite)

## Phase 4: Client Prediction & Rollback

- [x] **4.1** Add `ShootingGame.Shared.dll` reference to Unity project (copy DLL or project reference)
- [x] **4.2** Implement Vec3/Quat conversion extensions (Unity ↔ Shared)
- [x] **4.3** Load `collision.bin` in Unity client, build client-side `CollisionWorld`
- [x] **4.4** Refactor `PlayerController`: replace CharacterController.Move with `PlayerSimulation.Simulate()`, remove CharacterController component
- [x] **4.5** Implement `inputHistory` and `snapshotHistory` ring buffers on client
- [x] **4.6** Implement prediction loop: simulate locally, store snapshots, send input to server
- [x] **4.7** Implement reconciliation: compare server state vs local snapshot, rollback + re-simulate on mismatch
- [x] **4.8** Implement visual smoothing: Lerp visual position toward simulation position after rollback
- [ ] **4.9** Test: add artificial latency (50ms, 100ms, 200ms), verify prediction is smooth and rollback corrections are invisible under normal play

## Phase 5: Remote Player & Interpolation

- [x] **5.1** Create `RemotePlayerController` component — spawns/manages remote player GameObjects
- [x] **5.2** Implement interpolation buffer: ring buffer of timestamped snapshots, retrieve pair for interpolation
- [x] **5.3** Implement interpolation rendering: Lerp position, Slerp rotation at renderTime = currentTime - interpolationDelay
- [x] **5.4** Bridge animation system to snapshots: drive Idle/Move/Jump/Fall/Aim states from snapshot fields (works for both local and remote)
- [ ] **5.5** Test with 2 clients: verify remote player moves smoothly, no teleporting

## Phase 6: Hitscan & Lag Compensation

- [x] **6.1** Implement `Ray vs Capsule` intersection in shared physics (or expanded-AABB approximation for v1)
- [x] **6.2** Implement `WorldHistory` ring buffer on server (stores WorldSnapshot per tick, capacity 64)
- [x] **6.3** Implement `HitscanResolver`: given fire request + historical snapshot, raycast against historical player capsules
- [x] **6.4** Implement fire request processing in server: detect fire input, compute compensated tick, resolve hitscan, apply damage
- [x] **6.5** Implement `DamageEvent` broadcast (reliable) and client-side hit feedback (screen flash, hit marker, health update)
- [x] **6.6** Implement server-side fire rate enforcement and aim direction validation
- [x] **6.7** Replace client-side Bullet/BulletManager with VFX-only tracer (cosmetic, no physics)
- [x] **6.8** End-to-end test: Player A shoots Player B, B takes damage, both clients see correct feedback

## Phase 7: Polish & Edge Cases

- [x] **7.1** Handle player disconnect/reconnect gracefully (remove from world, free slot, notify other player)
- [x] **7.2** Implement spawn system: predefined spawn points, respawn after death with delay
- [x] **7.3** Add ping/RTT display to client UI
- [x] **7.4** Add connection status UI (connecting, connected, disconnected)
- [ ] **7.5** Stress test: simulate packet loss (10%, 20%), high latency (200ms), verify game remains playable
- [ ] **7.6** Profile server tick time, ensure < 5ms per tick with 2 players
