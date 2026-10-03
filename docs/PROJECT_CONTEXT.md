# Project context

Baseline inspected: 2026-10-02. Recheck this file against the project when making changes.

## Current state

Sword combat arena added 2026-10-03: `Assets/scenes/combat_test.scene` is a separate enclosed test scene, with a cover block, lighting, and `CombatArena`. It generates a Citizen swordsman for each player and one red stationary sparring NPC, all using `PlayerController` and visible primitive swords. Left click swings; WASD/mouse/Shift/Space/C use the existing movement bindings. The NPC turns toward nearby players and counterattacks in range, without pursuit. `SwordFighter` handles 100 health, 25-damage waist-height sword traces, a 0.65-second attack cooldown (double for NPC), hit feedback, three-second respawn and fall recovery. The host alone resolves damage and hit traces; movement remains owner simulated, without anti-cheat or lag compensation. Sword presentation uses right-hand IK. `CombatHud` shows controls, health and feedback. Sailing components and the manifest startup scene are unchanged; swords are currently arena-only.

Combat validation: S&box 26.10.02 compilation passed with zero errors/warnings. Local `naval_test_combat` passed range rejection, cooldown, NPC damage/defeat/respawn and NPC retaliation. Two live instances passed `naval_test_combat_network`: host damages client, client RPC damages host, client defeat blocks attacks, and client respawns at its own spawn. Camera screenshot verified arena, both sword visuals and HUD. Physical mouse/keyboard feel and host migration have not been systematically tested. API references: installed `Sandbox.Engine.xml`, [network events](https://sbox.game/dev/doc/networking/network-events), [RPC messages](https://sbox.game/dev/doc/networking/rpc-messages), and [PlayerController](https://sbox.game/dev/doc/scene/components/reference/player-controller).

Final combat checks also passed wall obstruction and facing-away rejection. Closing the client and joining a new instance produced exactly two players again, and the full network combat/respawn test passed after rejoining. Spawned test clients were closed; the editor was left with `combat_test.scene` open, outside Play mode.

| Setting | Observed value |
| --- | --- |
| Title | Naval Combat |
| Identity | `local.naval_combat` |
| Project type | `game` |
| Startup scene | `Assets/scenes/minimal.scene` |
| Network mode | Multiplayer |
| Player limits | 1 to 64 (configuration, not a tested capacity) |
| Manifest tick rate | 50 |
| Scene fixed update frequency | 50 |
| Scene network frequency | 60 |
| Map selection | Unrestricted; map list contains `facepunch.flatgrass` |
| Package references | Empty |
| Generated C# target | .NET 10, C# 14, root namespace `Sandbox` |

The original minimal scene contains a sun, skybox/environment probe, a plane, three physics cubes, and a camera with post-processing. It is preserved as the manifest startup scene.

The user chose arcade ship handling. A separate `Assets/scenes/naval_prototype.scene` now creates a local handling test:

- `Code/Prototype/NavalPrototype.cs`: runtime setup of the placeholder ship, ocean, Citizen player, bow helm, player camera, and HUD. Refuses startup in an active network session. Hull is now 520 by 240 units, with collidable deck/cabin/rails and a 10,000 mass override; player's body mass is 80.
- `Code/Ocean/ArcadeOcean.cs`: three intersecting wave trains plus a crest-sharpening harmonic, shared height queries, and a 128 by 128 cell CPU mesh with dense nearby sampling and stretched horizon geometry. Wave amplitude defaults to 55 (previously 18). Mesh buffers rebuild safely after layout changes during hotload.
- `Assets/shaders/naval_ocean.shader` and `Assets/materials/ocean/naval_ocean.vmat`: custom cel shading with four blue/teal bands, animated white crest ribbons and trailing foam strokes, antialiased boundaries, and distance haze. Shader wave phases and clock match the buoyancy calculation. `FoamStrength` is adjustable on the ocean component. This is opaque stylized water, without underwater rendering or physical foam simulation.
- `Code/Ships/ArcadeShip.cs`: four-point spring buoyancy, custom gravity, wind-powered acceleration, lateral drag, coasting resistance, and assisted yaw. Accepts steering only through its occupied helm. No global ship keyboard input remains.
- `Code/Ships/ShipHelm.cs`: standing `BaseChair` with proximity/ground/line-of-sight checks, hand IK, and a safe exit point. `SitMoveMode` handles attachment, animation, camera, and disabling/restoring the player's physics.
- `Code/Players/ShipPlayer.cs`: E interaction toggle, occupied helm input, release momentum inheritance, R recovery, and automatic overboard recovery.
- `Code/UI/SailingHud.razor` and `.scss`: mode indicator, controls, and a context-sensitive helm prompt.
- `Code/Diagnostics/HelmSmokeTest.cs`: editor-only `naval_test_helm` integration command.
- `Code/Ships/ShipChaseCamera.cs`: earlier chase-camera component remains available but is not used in the current scene. The built-in player camera now controls the view.

Controls on foot: WASD walk, mouse look, Shift run, Space jump, C first/third person, E take nearby bow helm. At the helm: A/D turn, E release. At the mast: A/D rotate sail, W raise/furl, S lower/deploy, E release. R returns the player to the deck, and falling below the water triggers recovery automatically. Ship, islands, ocean and wind are now saved in the naval scene; edit-mode changes persist when saved. Player and effects are generated in play mode. Citizen uses the local user's clothing through `Dresser`. Walking uses the engine's physical moving-ground support; only a player operating a station is parented to the ship. No swimming, swords, weapons, damage, match flow, or multiplayer authority/spawning is implemented.

Input settings contain the starter actions, including `Forward`, `Backward`, `Left`, `Right`, `Attack1`, `Attack2`, `Reload`, and `Use`. These are bindings only; they do not imply implemented controls. Collision and platform settings are present.

## Local environment

- Engine references resolve to `C:\Program Files (x86)\Steam\steamapps\common\sbox`; `bin/managed/Sandbox.Engine.dll` exists.
- `dotnet` is installed, but `dotnet --list-sdks` returned no SDKs at initialization. Command-line compilation has not been validated.
- Generated projects write outputs into the engine's `.vs/output` directory. Inspect those paths before invoking builds or use temporary output paths for isolated checks.
- `.editorconfig` and `.gitignore` already exist and were preserved.
- Git collaboration setup added on 2026-10-03: `main` is the default branch, `.gitattributes` normalizes text, and `CONTRIBUTING.md` documents the team workflow. The shared repository is https://github.com/brandon-daniels/naval-combat.
- Verified in S&box 26.10.02: game/editor compilation succeeds without errors or warnings; screenshots show Citizen clothing, the physical deck, brass helm, and HUD. The `naval_test_helm` integration check passed deck landing, walking to the bow without driving the ship, out-of-range rejection, mount/repeated-entry rejection, throttle/steering, moving-ship attachment, release with physics restored, walking after release, and overboard recovery. It drives movement and interaction methods in the live physics scene; physical keyboard/mouse feel and multiplayer have not been systematically tested.
- A live editor MCP endpoint was discovered in `sbox-dev.log` at `http://127.0.0.1:7269/mcp`. Recheck the current log before reuse; it is an environment detail, not a fixed project dependency. Initialize the connection, use `tools/list`, and call `editor_status` before acting. Discover actual tools with `search_tools`; inspect compiler results with `compile_status` and runtime errors with `read_console`. Protect unsaved scene changes.

## Platform working model

S&box uses C# with hotloading. Scenes contain GameObjects; Components supply behavior. Implement gameplay through this scene/component model and verify exact APIs against the current engine rather than importing assumptions from Unity or older S&box examples.

Component properties can expose references and tuning in the inspector. Keep source resources under `Assets/` and preserve their serialized references. The engine generates compiled asset files and development project files.

Before promoting the local movement prototype into multiplayer gameplay, decide simulation ownership for movement, projectiles, and damage. The current multiplayer project setting alone does not provide a working multiplayer game. Define synchronization and authority for each shared system, then test with multiple instances.

## Development loop

1. Inspect the relevant source, scene, and project configuration.
2. Verify needed APIs using official docs and installed engine references/examples.
3. Implement the smallest complete feature and configure its scene or prefab references.
4. Check editor compilation, hotloading, and play behavior.
5. For networked features, use the network status menu's **Join via new instance** and test host/client behavior.
6. Record actual validation results and update these notes when the baseline changes.

## Official references

These official pages were consulted during initialization:

- [Documentation overview](https://sbox.game/dev/doc/)
- [Your first project](https://sbox.game/dev/doc/getting-started/first-project)
- [Explore the engine](https://sbox.game/dev/doc/getting-started/explore-engine)
- [Game project startup](https://sbox.game/dev/doc/getting-started/project-types/game-project)
- [Components and inspector references](https://sbox.game/dev/doc/scene/components/)
- [C# and hotloading](https://sbox.game/dev/doc/code/)
- [Testing multiplayer](https://sbox.game/dev/doc/networking/testing-multiplayer)
- [Built-in player controller](https://sbox.game/dev/doc/scene/components/reference/player-controller)
- [Facepunch BaseChair implementation](https://github.com/Facepunch/sbox-public/blob/master/engine/Sandbox.Engine/Scene/Components/Game/BaseChair.cs)
- [Facepunch mounted movement mode](https://github.com/Facepunch/sbox-public/blob/master/engine/Sandbox.Engine/Scene/Components/Game/PlayerController/Modes/Sit.cs)

Use the documentation's API Reference and topic navigation for current physics, UI, input, ownership, RPC, and synchronization details when those systems are implemented.

## Wind sailing

`SailingWind` exposes wind heading (direction blowing toward), strength and gentle gusts. `SailRig` is a second standing `ShipStation` at the mast, with a rotating yard, deforming two-sided canvas, moving lower boom and orange world-aligned wind arrow. Deployment starts furled. Drive is deployment times squared positive sail/wind alignment times an arcade heading factor times wind strength. Headwinds, furled sails and calm wind give no drive; crosswinds are slower. The helm only steers, and deployed sails drive an unattended ship. `ShipStation` shares proximity, occupancy, line-of-sight, attachment and release behavior. HUD shows wind relative to the bow, deployment, alignment and power. `naval_test_sails` checks station interaction, trim/deployment, invalid remote input, calm/back-facing sails, unattended propulsion and release. This remains a local-only prototype.

Validation: installed editor compilation passed with zero errors/warnings. Both `naval_test_helm` and `naval_test_sails` passed in local play. Screenshots verified deployed canvas and wind HUD. The helm check waits for grounding during waves before boarding; its walking assertion checks the actual interaction radius. Physical keyboard/mouse feel and multiplayer are not covered.

Airborne wind: WindStreaks creates 48 tapered curved ribbons in one dynamic mesh around the ship. Ribbons drift in world space using SailingWind.Direction and CurrentStrength, recycle around the ship, taper in/out, and hide in calm wind. DriftSpeed and StreakLength are inspector settings. The unlit naval_wind shader/material keeps them pale against the sky. Verified editor compilation, material compilation, visible ribbons in play and disappearance at zero wind strength.

Practice islands: PracticeIslands, spawned under Prototype content, creates three fixed collidable islands at (2600,-1500), (5800,2000), and (9500,700). Each has faceted shoreline/beach/grass meshes with matching static ModelCollider geometry extending below the water, plus lighthouse, twin rocks, or palm landmarks. Positions form a downwind-friendly zigzag with approximately 3,900-4,750 units between centers. Verified zero compile errors/warnings, visibility of all three from the deck, physics ray hits on all three landmasses and the first submerged shore, and an unobstructed channel sample. No full sailing route or beach landing playthrough was performed.

## Authored scene conversion

The ship and all three islands are now serialized under Naval Prototype / Sailing world in naval_prototype.scene, preserving the original scene GUIDs. NavalPrototype references saved Ship, Ocean, Wind and SpawnPoint and creates only the player/camera/HUD/wind effects. Ship children, landmarks, station anchors and component references are editable in the scene. IslandSurface runs in editor and play, generating matching mesh/collision from serialized ring settings; generated components are NotSaved. Ocean and SailRig also run editor previews with generated visuals marked NotSaved. Editor/NavalSceneAuthoring and PracticeIslandAuthoring retain the one-time construction command naval_author_scene, which refuses to duplicate an already assigned ship. The scene was saved, closed and reopened successfully; edit-mode terrain collision and world preview verified.

Final conversion validation: persisted scene contains one ship, three island roots and 16 terrain sections, without runtime player objects. Both naval_test_helm and naval_test_sails passed after conversion; compilation passed with zero errors/warnings. Editor left outside Play mode with the authored scene open.

Ocean art update: Wind Waker-inspired saturated blue palette with three broad cel bands, animated warped cellular foam outlines broken into patches, and white crest accents. Distance filtering fades fine foam before the horizon. Reduced the crest harmonic from 0.22 to 0.12 and short-wave amplitude from 0.15 to 0.08 in both CPU buoyancy height and shader wave calculations, preserving their agreement. Existing WaveHeight/FoamStrength settings remain editable.

Cannons: four saved ShipCannon stations, two per broadside, authored by Editor/CannonAuthoring.cs. Shared station interaction now chooses the nearest valid ShipStation; cannon input uses WASD aim and Attack1 fire. Each has +/-35 degree traverse, 0-45 elevation, 1800 muzzle speed, two-second reload, visual recoil and flash. Cannonball uses swept sphere traces (radius 7), gravity 400, inherited ship velocity, water crossing and eight-second cleanup. CannonBurst visuals expire automatically. No damage model yet. Editor-only naval_test_cannons tests all four stations, aim, authorization, cooldown, projectile impact and release. Helm regression passed after cannon installation.
