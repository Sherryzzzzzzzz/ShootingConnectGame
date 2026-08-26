# Proposal: Rebuild combat UI and scenes from the reference project

## Problem

The current project has a functional runtime-generated HUD, but its hierarchy and feedback rhythm are inconsistent with the reference shooter. The Fight and HeroSelect scenes also lack a single, explicit presentation contract. Directly importing the reference scenes is unsafe because the projects use different Unity versions, render pipelines, input systems, and gameplay authority models.

## Goal

Rebuild the current UI and scene presentation using the reference project's layout and feedback ideas while keeping the current Unity 6, URP, TMP, Cinemachine, and ECS/network architecture. Improve movement, ADS, camera recoil, and fire feedback without moving authoritative gameplay into MonoBehaviours.

## Non-goals

- Copying the reference `.unity`, `.prefab`, or legacy MonoBehaviour gameplay wholesale.
- Importing the complete reference asset tree or unverified third-party dependencies.
- Replacing the authoritative server or shared deterministic simulation.
- Reworking spawn-point authority already defined by `fix-scene-spawn-ability-feedback`.

## Success criteria

- Fight HUD has explicit upper/lower layers for score, stage/time, health, ammo, abilities, crosshair, hit feedback, and network status.
- Hero select has a stable camera/UI composition and does not rebuild the same hierarchy every frame.
- Ammo, score, cooldown, hit, and ability activation feedback update from authoritative/predicted events with bounded refresh work.
- Fight HUD objects are generated in the editor and persisted in the scene; runtime does not rebuild the hierarchy. Matches auto-settle at 300 seconds, and the fixed ammo strip animates the rightmost shell ejecting with left shells filling the slot.
- Movement and ADS transitions use deterministic shared parameters; camera recoil/FOV remains presentation-only and smooth.
- Reference-derived assets are selectively imported or recreated with current shaders/TMP bindings, with no missing scripts or pink materials.
- A Unity editor/play-mode verification record and focused server regression tests are included.
