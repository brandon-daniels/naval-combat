# Naval Combat

A game project for S&box with a playable Citizen character, deck movement, and an arcade ship controlled from a bow helm. The planned game combines an ocean, ship physics, and sword combat.
<img width="1167" height="685" alt="image" src="https://github.com/user-attachments/assets/b8ac1933-23a1-4828-93a0-22119d7e9a60" />

<img width="990" height="661" alt="Screenshot 2026-10-03 105628" src="https://github.com/user-attachments/assets/2f2fe23d-9748-4127-a143-451dbadfd92e" />

## Try sword combat

Open `Assets/scenes/combat_test.scene` and press Play. A separate enclosed arena spawns a swordsman and a red sparring NPC. WASD moves, mouse looks, left click slashes, Shift runs, Space jumps, and C switches camera view. Approach the NPC to spar; it turns toward nearby players and attacks within sword range, but does not chase.

Each fighter has 100 health. Sword hits deal 25 damage, with a 0.65-second cooldown (the NPC attacks half as often). Defeated fighters cannot attack and respawn after three seconds. The HUD shows your health and hit/miss feedback. Swords and characters use placeholder geometry and Citizen models.

For PvP, use the editor network menu to start hosting, then **Join via new instance**. Each connection owns its movement; the host validates sword cooldown, health, range, obstruction and damage. Run `naval_test_combat` in local play or `naval_test_combat_network` with exactly two connected players for integration checks. The arena is separate from sailing; the project startup scene remains `minimal.scene`.

## Try the arcade prototype

Open `Assets/scenes/naval_prototype.scene` in the S&box editor and press Play locally.

| Control | Action |
| --- | --- |
| WASD | Walk on deck; A/D steer at the helm; A/D rotate, W raise and S lower at the mast |
| Mouse | Look around |
| E | Use the nearby mast or helm; press again to release |
| Space | Jump on foot |
| Shift | Run on foot |
| C | Toggle first/third-person view |
| R | Return the player to the deck |

Walk toward the brass wheel at the front of the ship until the **E · Take the helm** prompt appears. Taking the helm attaches the player to a standing control position with hands on the wheel. Releasing returns them to a clear spot on deck with the ship's velocity. Lower the sail at the mast to catch wind. Align its facing direction with the wind for more power; the HUD shows wind catch and sail power. The orange masthead arrow and HUD arrow point where the wind blows. The HUD arrow is relative to the bow (up means ahead). An unattended ship keeps sailing; raise the sail to remove propulsion and coast to a stop. Headwinds give no drive in this arcade square-sail model. Falling into the ocean automatically returns the player to the deck; swimming is not implemented.

The player uses the built-in S&box `PlayerController`, the default Citizen model, and the local user's avatar clothing. The widened deck, cabin, and rails have collision. The ship uses four-point spring buoyancy, assisted steering, and placeholder geometry. The ocean has dramatic intersecting swells, cel-shaded blue/teal bands, and animated white foam ribbons along wave crests. Its denser mesh follows the ship and fades into distant haze. Water is opaque, without reflections or underwater rendering. Adjust `WaveHeight` and `FoamStrength` on `ArcadeOcean` to tune it.

The sailing prototype remains local and refuses startup in an active network session. Multiplayer spawning and sword combat are available in the separate combat arena described above. The original minimal scene and project startup setting remain unchanged.

Handling values are inspector properties on `ArcadeShip`; initial defaults live in `Code/Ships/ArcadeShip.cs`. The ship and islands are saved scene objects. Edit them outside Play mode and save the scene to retain changes. Player, camera, HUD and wind streaks are created at runtime; play-mode edits are temporary.

Verified in the installed S&box editor: successful compilation with no errors or warnings, rendered Citizen/clothing and HUD, deck landing/walking, helm range restrictions, mount, acceleration/steering, attachment to the moving ship, release with restored physics, walking after release, and overboard recovery.

To repeat the physics integration check, play the scene locally and run `naval_test_helm` or `naval_test_sails` in the editor console. It temporarily controls the player, reports each assertion, and restores input afterward. It only runs in the editor with networking inactive. Physical keyboard/mouse feel and multiplayer behavior are not covered by this automated check.

## Open the project

1. Open `naval_combat.sbproj` with the S&box editor.
2. Open `Assets/scenes/minimal.scene` and enter play mode to inspect the starter scene.
3. Add gameplay components under `Code/` and attach them to GameObjects in the editor.

S&box compiles and hotloads C# changes during development. Inspect the editor console for errors and verify behavior in play mode.

## Project guide

- [Development instructions](AGENTS.md)
- [Project context and platform references](docs/PROJECT_CONTEXT.md)
- [Official S&box documentation](https://sbox.game/dev/doc/)

See [CONTRIBUTING.md](CONTRIBUTING.md) for cloning, branches, pull requests, and validation. Git ignores generated projects and local caches; source assets and their metadata are versioned. The existing compiled shader exception is preserved.

Pale airborne wind streaks drift above the water in the sailing wind direction. Their speed follows wind strength and gusts; they disappear in calm conditions. Adjust DriftSpeed and StreakLength on WindStreaks to tune the effect.

Three practice islands form a spaced zigzag sailing route: Beacon Island (lighthouse), Twin Rocks Island (stone spires), and Palm Island (palm grove). Sail from the starting area toward the lighthouse, then the rocks, then the palms. Island centers are roughly 4,000-4,700 units apart, with broad open-water channels. Shores and land have static collision; approach slowly by raising the sail. The current fixed wind favors this outbound route.

## Editing the sailing world

Open `Assets/scenes/naval_prototype.scene` with Play stopped. Expand **Naval Prototype > Sailing world** in the Hierarchy:

- **Arcade ship**: move/rotate the whole ship, or expand it to edit the deck, cabin, rails, helm, mast and spawn point. The ArcadeShip component exposes handling settings.
- **Practice islands**: move/rotate/scale Beacon Island, Twin Rocks Island or Palm Island as whole groups. Expand an island to edit landmarks and terrain sections. IslandSurface exposes terrain radius, ring proportions, heights and tint; generated collision follows the surface.
- **Ocean / Sailing wind**: edit wave and wind settings directly.

Save the scene after editing. Keep helm, mast, spawn and component references connected. Terrain and sail previews regenerate from their saved settings; they are not external model assets. The startup scene remains unchanged.

The ocean now uses a Wind Waker-inspired blue palette, drifting white foam outlines and broad rolling swells with softer small waves. WaveHeight and FoamStrength remain editable on the saved Ocean object.

Four editable cannon stations sit on the ship, two per side. Press E near one, use A/D to traverse and W/S to adjust elevation, then left-click to fire. E releases the station. Each cannon has a two-second reload, barrel recoil, ballistic iron shots, muzzle flash and water/solid impact effects. Cannonballs collide with the world; damage and destructible ships are not implemented.
