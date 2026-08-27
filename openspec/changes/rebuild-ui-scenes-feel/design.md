# Design: Rebuild UI, scenes, and gameplay feel

## Reference mapping

Use `Game00.unity` as a layout reference only. Recreate the following logical layers in the current Fight scene or a reusable HUD prefab:

- `UIUpperBase`: score, stage/room, match timer, objective/compass.
- `UILowerBase`: health/status, ammo strip, reload state, ability slots.
- `CanvasOverFade`: scene transition and damage/ability overlays.
- `HowToPlay`/`ControlGuideText`: contextual input hints.
- `CharacterIcon` and status sliders: local hero identity and health/weapon state.

Existing `BattleUI` and `AbilityBar` remain the data owners. New presentation components should subscribe to `ClientPresentationEventBus`, `HitEventView`, and the latest ECS snapshot rather than polling every frame. Refresh intervals and object counts are bounded; repeated entries use pooling or reuse.

The Fight HUD hierarchy is generated and saved by the Unity editor menu `ShootingGame/UI/Generate Arcade Fight HUD`. Runtime startup must only bind and update serialized objects; it must not create HUD canvases, text, images, or ability slots. The match clock is authoritative on the host and is capped at 300 seconds. When the cap expires, the host sends the existing game-over packet and resolves the winner by kills with deterministic player-id tie breaking. Ammo presentation uses a fixed shell object pool: the rightmost visible shell is ejected on fire while the shells to its left slide into the open slot.

Every `BattleFrame` carries the host's remaining match ticks in its existing timestamp field. Clients display that received value directly and never decrement a local HUD timer. The four-shell strip is a fixed-slot animation: eject the rightmost shell, shift survivors from right to left in sequence, then reuse the ejected object in the leftmost slot only when at least four rounds remain.

## Scene composition

Keep `Fight.unity` as the network gameplay scene and `HeroSelectScene.unity` as the selection scene. Add only scene-local anchors, canvases, camera targets, and presentation components. Use current TMP, URP materials, and Cinemachine targets. Any reference sprite or animation is copied only after dependency and license checks, then rebound through current components.

## Feel parameters

Add shared, deterministic movement parameters for acceleration/deceleration and ADS speed multiplier. Movement systems apply the same parameters on server and client prediction. `CombatCameraDirector` applies smoothed ADS FOV and recoil decay; recoil is emitted only after a successful fire event. Aim direction may use a camera ray/ground-plane fallback on the client, while server hit validation remains authoritative.

Reference baseline values are starting points, not hard-coded legacy behavior: acceleration factor about `1 + movePower * 7`, ADS movement multiplier about `0.02 + movePower * 0.38`, and exponential recoil decay. Expose them through the existing shared simulation/config types so tuning does not require scene script edits.

## Performance and verification

Avoid per-frame hierarchy creation, per-shot material allocation, and unbounded UI instantiation. Validate Unity compilation, scene hierarchy, and play-mode interaction. Run the existing server test suite plus focused tests for movement smoothing, ADS scaling, and successful fire-to-recoil ordering.
