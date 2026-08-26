# Reference UI and Scene Mapping

Source: `F:/解包文件夹/ShootersReady_Unpacked/ExportedProject`.

The reference combat scene `Assets/App/Scenes/Game00/Game00.unity` is used as a layout reference only. Its logical layers map to the current project as follows:

| Reference layer | Current implementation |
| --- | --- |
| `UIUpperBase` | `ReferenceHudLayout.UIUpperBase`; score, kill feed, network state |
| `UILowerBase` | `ReferenceHudLayout.UILowerBase`; health, ammo, ability bar |
| `CanvasOverFade` | `ReferenceHudLayout.CanvasOverFade`; scoreboard, matching, game-over overlays |
| `HowToPlay` / `ControlGuideText` | Existing contextual UI hooks; keep input acquisition in `ClientInputSystem` |
| `CharacterIcon` / status sliders | Existing `BattleUI` health and hero selection bindings |
| `AmmoRemain` graphics | Existing `BattleUI` ammo text; future per-round strip can reuse the lower anchor |
| `CompassTarget` | Existing objective/compass presentation hook; keep data authoritative from battle frames |

The reference project targets Unity 2022.3, built-in rendering, legacy Input, and UGUI `Text`. The current project targets Unity 6, URP, Input System, TMP, and ECS networking. Therefore only hierarchy, timing, and numeric feel parameters are migrated; reference prefabs and gameplay scripts are not copied directly.

The current HUD creates these anchors once under its screen-space canvas. Repeated widgets are refreshed on bounded intervals and ability slots are reused, so scene startup does not continuously rebuild the hierarchy.
