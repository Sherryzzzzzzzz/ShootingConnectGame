# Tasks: Rebuild UI, scenes, and gameplay feel

## 1. Baseline and contracts

- [ ] 1.1 Record current Fight/HeroSelect hierarchy and identify reusable `BattleUI`/`AbilityBar` bindings.
- [x] 1.2 Add a reference-to-current mapping document for HUD anchors, events, and assets.
- [x] 1.3 Define bounded refresh/pooling rules for repeated HUD elements.

## 2. Fight HUD

- [x] 2.1 Create explicit upper/lower HUD anchors and transition overlay in `Fight.unity` or a reusable prefab.
- [ ] 2.2 Rework ammo, score/time, health, ability, compass, and contextual hint presentation around authoritative/predicted events.
- [ ] 2.3 Add hit/kill/ability feedback animations without per-frame GameObject creation.

## 3. Hero select and scene presentation

- [ ] 3.1 Stabilize HeroSelect camera targets, background anchors, and character preview composition.
- [ ] 3.2 Selectively migrate compatible reference sprites/materials or recreate them with current URP/TMP components.
- [ ] 3.3 Verify scene load transitions and editor responsiveness.

## 4. Gameplay feel

- [x] 4.1 Add deterministic acceleration/deceleration and ADS movement scaling to shared simulation.
- [ ] 4.2 Tune `CombatCameraDirector` ADS FOV, recoil kick/decay, and fire feedback ordering.
- [ ] 4.3 Add camera-ray/ground-plane aim fallback and verify muzzle/crosshair alignment.

## 5. Verification and delivery

- [ ] 5.1 Run focused and full server tests; add regression coverage for new shared parameters.
- [ ] 5.2 Compile and play-test Fight/HeroSelect in Unity; capture console/screenshot evidence.
- [ ] 5.3 Commit the scoped change on a non-main branch and report the exact push target before uploading.
