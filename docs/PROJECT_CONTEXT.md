# Project context

## Current priority: NPC gameplay (2026-10-09)

The user explicitly prioritized NPC development going forward. Continue with the pirate NPC loop rather than treating completion of the multiplayer/economy backlog as a prerequisite. Address networking, resource delivery, combat or scene setup when it directly blocks the NPC slice; keep other unfinished checks tracked without promoting them ahead of NPC work.

The first integration slice is complete: the main gameplay session now spawns one host-controlled pirate captain and ship. Next implement patrol, disengagement and stuck recovery, then damage/defeat and cargo theft/delivery through shared gameplay rules. Do not describe firing as working damage or theft before those systems exist. Existing multiplayer/economy milestones remain partial.

## Main-scene NPC integration (2026-10-09)

NavalMultiplayerSession clones its authored NPC template by default (SpawnNpcShip can disable spawning). The pirate ship and sailor are unowned network objects simulated by the host. Joining clients receive the captain, target, activity and shot state but run no NPC decisions. Host-only station access and cannon acceptance support this unowned sailor while retaining human ownership checks. Disconnect cleanup excludes the pirate.

The captain chooses an active human sailor's ship, physically walks between mast, helm and cannons, sails toward its target and fires broadsides. Invalid/disconnected targets are replaced; without a target it attempts to furl the sail and wait. The captain centers the persistent rudder before leaving the helm. Spawn placement uses authored deck markers. Cannon aim now allows configurable downward elevation (10 degrees by default) and aims at the rebuilt ship's deck height; target-hierarchy obstruction checks use the installed engine's parent.IsDescendant(child) convention.

Live S&box 26.10.02 validation: local naval_test_npc passed mast/helm/cannon use and firing with 5,504 units of ship displacement. The hosted pirate also sailed and fired. With a second client, naval_test_npc_network passed one-pirate replication, unowned host authority, replicated target/activity/shot state, and zero client decision ticks. naval_test_network_shell passed with both players and the pirate. For target loss, the captain was assigned the remote ship during Play; closing that client caused it to select the remaining host ship and resume station use, with the shell check again passing and no orphaned ships. Game and editor compilation reported zero errors and warnings.

Commands: naval_npc_status reports current behavior; naval_test_npc observes a new shot after mast/helm/cannon use locally or on the host; naval_test_npc_network checks both peers with a host and joining client. The station flags persist for the session, so rerunning naval_test_npc does not independently prove a fresh journey. Host migration, local-preview promotion while seated, no-human furling, and the separate prototype scene were not replayed in this pass. Client goods/upgrade resource-delivery warnings remain an unrelated open issue. Cannon damage, loot, cargo scoring, patrol and disengagement are still absent; current selection may repeatedly attack an empty/newly spawned ship. The test client was closed and the editor left outside Play after validation.

## Physical cargo multiplayer regression (2026-10-09)

The two-owner voyage diagnostic now follows physical barrels through the existing owner RPC boundary: invest, spawn, carry, secure, deliver and upgrade. It checks in-range foreign-owner pickup rejection, repeated secured-barrel interaction, and duplicate sale without extra payment. Rejection checks require acknowledgements from both owners so missing replicated barrels cannot silently pass. Positive checks wait for authoritative state with bounded timeouts. Original production durations and owner input settings are restored. Only host-owned barrels are repositioned; the producer radius and sale quote are temporarily changed and restored. This is not a physical sailing-route test.

Run `naval_test_voyage_network false` with two fresh voyages for cargo. The optional `true` variant also checks cannons and refuses to start unless both ships have them. The current player template has no cannons, so cannon coverage is explicitly reported as not tested. The local `naval_test_voyage` already covered barrels; only the network version needed migration.

The cleanup fixture now waits for deferred object destruction before asserting removal. New `naval_test_disconnect_cargo` stages one real production batch per player, charges the configured investment, then waits up to 60 seconds for the remote client to leave. It verifies the departing ship/sailor/barrel disappear while the remaining owner's batch, money and hold stay unchanged. Restart Play after these destructive diagnostics for a fresh session.

Live validation in S&box 26.10.02: `naval_test_voyage_cleanup`, two-owner `naval_test_voyage_network false`, and `naval_test_disconnect_cargo` passed. The shell check passed after the real client disconnect with no orphaned ships or barrels. Game/editor compilation reported zero errors and warnings. A join-time `SailingHud` null-reference error was fixed by waiting for player/ship/economy references before rendering or hashing the HUD. A fresh client subsequently rejoined successfully, with no recurrence of that HUD exception in its log. The editor was closed before the post-rejoin shell and fresh-wallet assertions were run; do not treat reconnect state or host migration as fully verified.

Editor launch and its localhost MCP interface require execution outside the restricted environment. Current endpoint was `http://127.0.0.1:7269/mcp`; rediscover from the log after reopening. Both test client and editor are now closed. The client still logged missing compiled goods/upgrade resources and unused scene resources despite the existing manifest include patterns. Authoritative cargo tests passed, but resource delivery, full rejoin state, physical remote sailing, cannon setup, and host migration remain follow-ups. Milestones 1/2 remain partial.

## Multiplayer disconnect cargo cleanup (2026-10-09)

Continued Milestone 1: disconnect now removes the departing voyage's independently networked goods barrels before destroying its sailor and ship. The slot caches the voyage identity (including local-preview promotion and host slot rebuilding), so cleanup does not depend on the sailor still being valid when the callback arrives. Duplicate activation of an existing sailor now restores its cleanup slot. Only the authoritative peer performs cleanup; other voyage identities are untouched. This preserves the existing fresh-voyage-on-reconnect behavior; reconnect progression retention is not implemented.

`naval_test_network_shell` now checks for orphaned network ships, missing/duplicate voyage identities, and barrels belonging to departed voyages. New local-editor command `naval_test_voyage_cleanup` exercises loose, carried and secured barrel removal, missing-identity rejection, isolation from another voyage, and repeated cleanup using temporary fixtures.

Validation: .NET 10.0.401 command-line build against the installed S&box references passed with zero errors. Existing host-migration analyzer warnings in `CombatNetworkSmokeTest` and `VoyageNetworkSmokeTest` remain. Outputs were redirected into a temporary workspace directory rather than the engine output directory. S&box launch attempts did not produce a running editor or reachable MCP endpoint, so hotload, the new runtime diagnostic, and actual host/client disconnect/reconnect were NOT verified. Milestone 1 remains partial.

Next live checks: run `naval_test_voyage_cleanup` in local gameplay, then join a second instance, produce cargo for each player, disconnect one while carrying/holding/staging cargo, and rerun `naval_test_network_shell`. Rejoin and verify one fresh sailor/ship and no old barrels; repeat after host migration. At this point the network voyage diagnostic still assumed direct production claiming (`HasOutput`); the local voyage diagnostic already covered physical barrels. See the later update above for current network diagnostic behavior. The integrated player template also currently lacks cannons, as recorded below.

## Original reference ship (2026-10-05)

The playable ship is now an original mesh built from scratch from the user's cream-colored, low-poly single-mast boat reference. `Assets/models/reference_ship/reference_ship.vmdl` replaces the imported pirate hull; the editable source is `reference_ship.blend`, rebuilt by `Tools/build_reference_ship.py` using Blender 5.2. The generator creates its own hull, stern house, windows, wheel, rails, mast, yards, bowsprit, rigging, materials, separate square sail, and collision FBX. It does not import vendor geometry. The original vendor library remains available, but neither sailing scene references its old ship mesh.

Both `naval_gameplay.scene` and `naval_prototype.scene` now serialize the new render model and ModelCollider on each ship, so it is visible before Play. Removed the obsolete hull/deck/cabin/rail objects, root hull box, and primitive ship renderers; retained station/rig transform GUIDs and functional references. The existing player ship in gameplay has no cannon stations (pre-existing scene state); NPC/prototype cannon logic is preserved.

Model coordinates are +Y bow / +Z up. The FBX export pre-rotates by -90 degrees to compensate Source import conversion; runtime keeps yaw -90, scale 48.8, and offset Z -90. Deck top is 3.34, hold floor 0.92, clear headroom 2.26 model units (110 game units), stair width 1.88 (92 game units). Twelve visible steps share a smooth 26.7-degree convex ramp. `PhysicsHullFile` MUST use `import_mode = "HullPerElement"`; `hull_mode` is not the file-import property and silently creates an enclosing convex hull. The dedicated collision source has 85 convex elements and one material. Hierarchy checks in ArcadeShip now use the installed engine's parent.IsDescendant(child) convention.

The aft main-deck helm faces the stern wheel, the mast control aligns with the new mast, and spawn points sit beside the hatch. NPC deck routing uses the side aisle to avoid walking into the opening. Ocean swimming ignores points within the enclosed hold. The ocean shader masks the eight nearest holds only while water intersects their vertical bounds; this is a yaw-aligned visual mask, not a flooding simulation. Full multiplayer/reconnect and extreme roll masking have not been retested.

Validation: new hull/sail assets and both scenes compile; S&box reports zero C# errors. Live physical cargo traversal passed deck-to-hold-to-deck with the normal PlayerController, no intermediate teleport, and no swimming. Sail station/deployment/trim/propulsion diagnostic passed. Blender overview and hatch renders are in `docs/reference_ship_preview.png` and `docs/reference_ship_hold.png`. Editor reopening verified the new saved ship objects and absence of the old Hull objects. Final `naval_test_helm` passed range rejection, mount, propulsion, steering, release, walking, swimming, and deck recovery. The expanded `naval_test_cargo_walk` passed again, including walking forward under the intact deck beside the mast, and returning through the stair. Both game and editor compilers report zero errors and warnings. The editor was left outside Play, focused on the saved new ship. Multiplayer was not rerun.


## Pirate Ship 02 arcade-ship visual (2026-10-04)

The authored `Arcade ship` template in `naval_gameplay.scene` now has a `Pirate ship 02 visual` child using `lowpoly pirates/models/ships/pirate_ship_02.vmdl`. ModelDoc preserves the derivative FBX's roughly 21.32-unit length, so the visual uses a 24.4 uniform scale to match the existing 520-unit gameplay hull. A negative 90-degree S&box yaw maps the model's bow axis onto the ArcadeShip's positive-X forward axis, and a -45 Z offset aligns the scaled deck with the established deck height. The seven placeholder box renderers for the hull, deck, cabin, and rails are disabled, while their colliders remain enabled so existing walking, buoyancy, stations, cannons, and multiplayer cloning keep their proven gameplay setup. Live editor compilation and visual alignment remain to be checked.

## Pirate ship 02 cargo interior (2026-10-04)

`Assets/Lowpoly Pirates/models/ships/pirate_ship_02.vmdl` now imports a dedicated derivative of `ship.002` instead of filtering the shared `boats.fbx` directly. The welded cabin-front door was opened, a framed entrance and eight-step stair flight descend into a below-deck cargo room, and the room has a floor, side lining, and fore/aft bulkheads. Its `PhysicsHullFile` points to a 23-element compound collision source so the room, extended walking deck, and individual steps remain valid convex shapes on a moving ship.

The original sail cloth is exported independently as `pirate_ship_02_sails.vmdl`, aligned to the hull pivot and intentionally without collision. Gameplay can omit it, toggle its renderer, swap it, or use it as the source for a future deforming sail component without changing the hull. The editable source is `pirate_ship_02_cargo.blend`; `Tools/build_pirate_ship_02_cargo.py` reproducibly rebuilds the hull, sails, collision FBXs, and Blender file from the untouched shared `boats.fbx`.

`ArcadeShip` now installs the authored model and its `ModelCollider` on every playable or cloned ship, disables the old interior-blocking prototype boxes, and lays out the stations for a ship model scale of 48.8 (about 1,040 units long). Its four-point buoyancy footprint scales with the hull. The helm is forward of the cargo entrance, the two cannons sit on opposite gunwales, the mast station remains on the walking deck, and the saved player/NPC spawns were raised for the larger deck. `SailRig` scales its procedural deforming canvas with the hull, preserving raise/lower and trim controls. Ship-station visibility checks ignore the owning hull's detailed collision while still rejecting external obstructions.

Blender 5.2 headless import/export and geometry inspection passed: the derivative hull is centered at X/Y zero with minimum Z zero, contains the eight descending stair elements, and the closed door polygons are absent in the rendered preview. The stale absolute source-palette image path prevents a correctly textured standalone Blender render, but S&box continues to override the imported material with `pirate_palette.vmat`. S&box 26.10.02 compiled the game and model with zero code errors; `naval_test_ship_model`, `naval_test_helm`, `naval_test_sails`, and `naval_test_cannons` passed against the enlarged live ship. These checks covered cargo-floor collision, moving-deck walking, helm steering, sail deployment/raising/propulsion, both broadside cannon stations, firing, reload, and release. A physical keyboard walk down the stairs remains useful visual/feel validation.

## Editor-authored trade buoys (2026-10-04)

The Beacon, Twin Rocks, and Palm trade buoy visuals are serialized as editable child objects in `naval_gameplay.scene`. Their model, transform, and tint are now visible in the scene hierarchy and inspector; `VoyagePort` no longer creates a duplicate visual when play starts. Beacon remains gold and the two sale ports remain green. Scene JSON and authored references were validated outside the editor; a live S&box visual/compile check remains required because the editor was not exposed to the available UI-control bridge.

## Weighty helm handling (2026-10-04)

`ArcadeShip` now uses a persistent wheel/rudder position: holding A/D winds the wheel over time and releasing the key leaves it set until the player steers it back. Rudder authority scales with forward water speed, retaining only a small low-speed response for close maneuvering. Yaw response develops progressively instead of snapping to the requested rate, lateral resistance is softer so the hull carries a controlled sideslip through turns, and raised or depowered sails preserve more coasting momentum. These handling values are inspector-exposed under the component's Handling group. The sailing HUD reports the rudder percentage and side and explains that the wheel holds position. `ShipNpc` sets an authorized direct rudder target so its heading controller does not accumulate human-style wheel input.

Follow-up speed tuning reduces effective sail thrust to 38% of the legacy acceleration cap and lowers unpowered forward resistance from 0.18 to 0.045. The ship now takes longer to build speed and retains momentum roughly four times as strongly after furling or losing wind. `DriveResponse` and `CoastingResistance` remain inspector-exposed for further feel tuning.

S&box 26.10.02 live compilation passed with zero errors after the follow-up speed tuning. The extended `naval_test_helm` diagnostic passed propulsion, persistent-rudder winding, gradual steering, moving-ship attachment, release, deck walking, swimming and recovery. Physical keyboard feel remains to be checked. The pre-existing modified `naval_gameplay.scene` was not edited by this handling pass.

## Physical produced cargo (2026-10-04)

Completed production now creates a networked, collidable `lowpoly pirates/models/barrel.vmdl` batch at the producing Beacon buoy instead of placing goods directly in `CargoHold`. Only the investing player can carry that barrel with E. Dropping it while back aboard the player's ship reserves its ten units of hold capacity and secures a visible barrel to the deck; dropping with a full hold leaves it loose in the world. Key 2 at a nearby trade buoy sells only barrels secured to that player's ship, credits the existing market value, removes the matching hold quantity, and destroys the delivered barrel. Carry, capacity, sale, and payment mutations remain host-authoritative. The HUD now describes the invest/carry/drop/deliver flow.

Baseline inspected: 2026-10-02. Recheck this file against the project when making changes.

## Lowpoly Pirates placeable models (2026-10-04)

Seven filtered ModelDoc resources under `Assets/Lowpoly Pirates/models` expose a barrel, crate, chest, cannon, palm tree, rock and watchtower as individual editor-placeable assets. Together with the six dirt-island resources, all placeable Lowpoly Pirates models use explicit compiled-space origin corrections: horizontally centered and vertically aligned to the model base. Live S&box bounds inspection verified every center within 0.001 units of X/Y zero and every minimum Z at zero. They share `materials/pirate_palette.vmat`. Drag these `.vmdl` assets from the S&box asset browser into a scene and position them manually. No component automatically distributes them or changes gameplay geometry, collision, navigation or networking.

Six filtered ship resources under `Assets/Lowpoly Pirates/models/ships` expose the two rowboats (including their matching paddles) and four larger ships from `boats.fbx` as separate collidable models. Their explicit import translations were calculated from each complete vessel's source-space vertex bounds so the origin is centered in X/Y and sits at the lowest point in Z. The original combined FBX remains unchanged. Engine compilation and live bounds validation are still required.

Six additional filtered resources under `Assets/Lowpoly Pirates/models/islands` expose the two large and four small meshes from `dirt_islands.fbx` as independent, recentered, collidable models. Each dragged instance has its own scene transform, including independent position, rotation and scale. All six compiled successfully in S&box 26.10.02. These models are separate from the generated `IslandSurface` practice terrain; placing one does not automatically add it to NPC shoreline routing.

The source FBX files contain stale absolute Blender material paths and dotted object names, so their first on-demand import logs warnings even though the filtered models compile and the global palette material is applied. S&box 26.10.02 compiled all seven resources successfully.

## Playable trading slice (2026-10-04)

The default `naval_gameplay.scene` now provides a local playable invest/produce/load/sail/sell/upgrade loop. The missing `NavalPrototype.CreateSailor` factory was restored as shared player setup, and the obsolete serialized `NpcShipCrew` component was removed from the integrated scene. Goods and upgrade asset extensions were shortened to `.ngoods` and `.nupgrade` to meet S&box's eight-character limit; resource metadata GUIDs were preserved. A clean editor restart was needed after changing registered asset types, and authored resource values were verified at runtime afterward.

Three saved `VoyagePort` trade buoys expose commodity, price, radius, and production availability in the editor. Beacon's gold buoy is at (1500,-1500), Twin Rocks at (4700,2000), and Palm at (8400,700). `PlayerVoyage` attaches to each spawned sailor with a wallet and reserved `ProductionSite`. Number keys 1/2 invest-or-load/sell; 3/4/5 buy cargo/hull/sailing upgrades. Requests validate authority, RPC ownership, ship distance, sailor proximity, and low ship speed. Cargo and production mutations remain on the host. HUD displays money, used capacity, production countdown, relative port bearings, sale/upgrade prices, and results. The three synchronized upgrade levels drive base-relative capacity, hull health, and sailing-speed modifiers. Production and upgrades reset with the session; reconnect persistence and cargo-stack migration remain future work.

Live local validation on 2026-10-03: `naval_test_voyage` passed authored resource loading, 500 starting coins, range rejection, 100-coin investment, early/full-hold/duplicate claim rejection, completed production loading ten goods, Palm sale paying 350, all three upgrade purchases and effects, and insufficient-funds rejection. This diagnostic accelerates its production timer and moves the ship between ports; it is not a manual full-route playthrough. `naval_test_helm`, `naval_test_sails`, and `naval_test_cannons` passed in the integrated scene. Helm recovery assertions now match swimming plus manual R recovery. A camera screenshot verified the ship, islands, ocean, and voyage HUD. Game/editor compilation succeeded; unrelated engine-content warnings may still appear. Pirate AI, cannon ship damage, defeat, cargo theft, and a full two-client voyage remain outside this playable local slice.

Two-instance investigation on 2026-10-04: `naval_test_network_shell` passed with two connected players and distinct owned ships. Fixed the integration scene's `Sailing world` parent from Never to Snapshot so ocean/wind/template references reach joining clients; created client camera/HUD before host-only bootstrap checks, retried presentation reference binding each frame, and excluded generated ocean/terrain/sail geometry from snapshots. Cannon reload time now synchronizes from the host. Production uses a serialized per-voyage identity that survives promoting a local session to hosting. The new `naval_test_voyage_network` exercises real owner requests, but FAILED on remote investment: the remote sailor/ship moved violently after staging, and the host correctly rejected its out-of-range request. The client also reported missing compiled custom-resource files despite the resource inclusion setting. Remote physics and custom-resource delivery are known blockers; do not describe multiplayer as playable or Milestones 1/2 as complete. Test clients were closed after the investigation. Continue from these failures rather than adding piracy before the network slice is stable.

Follow-up on 2026-10-04: the violent motion was isolated to the diagnostic directly teleporting owner-simulated network rigidbodies. Reworked `naval_test_voyage_network` to leave both ships under normal owner simulation and temporarily widen the producer validation radius; it now passes both real-owner investment, production loading, 350-coin sales, cargo upgrades, own-cannon station authorization, host fire acceptance/reload, and release. Stable sampled ship positions remained near the two normal spawns. The local voyage diagnostic remains responsible for authored Beacon/Palm placement and out-of-range checks. After a clean editor restart, source-extension resource patterns still omitted the four compiled definitions from client delivery. The project resource include now targets `/definitions/goods/*.ngoods_c` and `/definitions/upgrades/*.nupgrade_c`. A fresh client downloaded the expanded network file set without definition `ERROR_FILEOPEN` messages, and the two-owner voyage diagnostic passed again. Physical remote sailing/interpolation, reconnect, and host migration remain open; Milestones 1/2 are still incomplete.

Final local recheck on 2026-10-04: after a clean editor restart, the expanded `naval_test_voyage` passed every assertion again, including all three upgrade effects and the full-hold rejection. The HUD now includes current hull health, ship speed, and a trading-available indicator. Reset Play after the diagnostic to restore starting money, empty cargo, and no upgrades.

## Gameplay framework (2026-10-03)

The first integrated-loop code foundation now lives under `Code/Economy`, `Code/Gameplay`, and `Code/Upgrades`, with `Code/Combat/ShipHealth.cs`. It defines stable-ID goods and upgrades, authoritative wallet and cargo mutations, atomic market purchases/sales, timed invest-and-claim production, port proximity, upgrade purchases/modifiers, ship health/repair, shared transaction results, and a scene-level `NavalGameSession` registry. Cargo stack and upgrade-level replication is not implemented, RPC request boundaries and identity binding are not implemented, cannons do not apply ship damage, and UI/persistence/loot remain future work. Tests and editor compilation were intentionally not run for the initial framework setup pass.

Milestone 0 now has authored definition assets for `goods.trade_goods`, `upgrade.expanded_hold`, `upgrade.reinforced_hull`, and `upgrade.improved_rigging`. The first-slice contract uses a 30-minute target session, 500 starting money, 75% cargo drop, free respawn, and match-scoped upgrades. `Assets/scenes/naval_gameplay.scene` is an independent GUID-remapped copy of the proven sailing world for integration work; `naval_prototype.scene` and `combat_test.scene` remain separate test scenes. The full balance table is tracked in `README.md`.

Milestone 0 validation: S&box 26.10.02 game and editor compilation passed with zero errors or warnings. The integrated scene compiled, opened and reopened without unsaved changes; `NavalGameSession` resolved on its root, and `CargoHold`, `ShipHealth`, and `ShipUpgradeManager` resolved on its player ship with the expected goods and upgrade resource references. File hashes confirmed the source prototype and combat scenes were unchanged by scene creation. Existing live prototype diagnostics were not rerun because the editor MCP play command launched the manifest startup scene (`minimal.scene`) rather than the selected scene, and the prototype tab contained pre-existing unsaved editor changes that were preserved. The last verified prototype diagnostic results remain recorded below. The editor was left outside Play mode with `naval_gameplay.scene` active and the unsaved prototype tab still open.

## Multiplayer sailing shell (Milestone 1, in progress)

`NavalMultiplayerSession` replaces the disabled local `NavalPrototype` bootstrap in `naval_gameplay.scene`. It keeps the authored player ship and NPC ship disabled as templates, creates local-only camera/HUD/wind presentation, and spawns one cloned ship plus sailor for every active connection. Player ships and sailors are owned network objects. Hull simulation stops on proxies; movement, sail trim, and cannon aim are owner-simulated. Station take/release requests are validated by the host and occupancy is synchronized from the host. Cannon fire is requested from the owning client, revalidated against player and ship ownership by the host, and creates a host-spawned network cannonball. Disconnect cleanup and host slot reconstruction are implemented.

`naval_test_network_shell` is a host-side structural diagnostic for one sailor and distinct owned ship per connection, active networking, ship references, and station authority bindings. Current validation is limited to a successful S&box 26.10.02 game/editor compile with zero errors and warnings. A live two-instance sailing pass, authorization-denial checks, cannon RPC check, reconnect, host migration, remote interpolation tuning, and authoritative NPC activation remain before Milestone 1 can be marked complete. The manifest now launches `naval_gameplay.scene`; an already-running editor may need to reload the project before it observes that manifest change.

## Current state

Sword combat arena added 2026-10-03: `Assets/scenes/combat_test.scene` is a separate enclosed test scene, with a cover block, lighting, and `CombatArena`. It generates a Citizen swordsman for each player and one red stationary sparring NPC, all using `PlayerController` and visible primitive swords. Left click swings; WASD/mouse/Shift/Space/C use the existing movement bindings. The NPC turns toward nearby players and counterattacks in range, without pursuit. `SwordFighter` handles 100 health, 25-damage waist-height sword traces, a 0.65-second attack cooldown (double for NPC), hit feedback, three-second respawn and fall recovery. The host alone resolves damage and hit traces; movement remains owner simulated, without anti-cheat or lag compensation. Sword presentation uses right-hand IK. `CombatHud` shows controls, health and feedback. Sword combat remains arena-only and the test scene must be opened explicitly.

Combat validation: S&box 26.10.02 compilation passed with zero errors/warnings. Local `naval_test_combat` passed range rejection, cooldown, NPC damage/defeat/respawn and NPC retaliation. Two live instances passed `naval_test_combat_network`: host damages client, client RPC damages host, client defeat blocks attacks, and client respawns at its own spawn. Camera screenshot verified arena, both sword visuals and HUD. Physical mouse/keyboard feel and host migration have not been systematically tested. API references: installed `Sandbox.Engine.xml`, [network events](https://sbox.game/dev/doc/networking/network-events), [RPC messages](https://sbox.game/dev/doc/networking/rpc-messages), and [PlayerController](https://sbox.game/dev/doc/scene/components/reference/player-controller).

Final combat checks also passed wall obstruction and facing-away rejection. Closing the client and joining a new instance produced exactly two players again, and the full network combat/respawn test passed after rejoining. Spawned test clients were closed; the editor was left with `combat_test.scene` open, outside Play mode.

| Setting | Observed value |
| --- | --- |
| Title | Naval Combat |
| Identity | `local.naval_combat` |
| Project type | `game` |
| Startup scene | `Assets/scenes/naval_gameplay.scene` |
| Network mode | Multiplayer |
| Player limits | 1 to 64 (configuration, not a tested capacity) |
| Manifest tick rate | 50 |
| Scene fixed update frequency | 50 |
| Scene network frequency | 60 |
| Map selection | Unrestricted; map list contains `facepunch.flatgrass` |
| Package references | Empty |
| Generated C# target | .NET 10, C# 14, root namespace `Sandbox` |

The original minimal scene contains a sun, skybox/environment probe, a plane, three physics cubes, and a camera with post-processing. It remains preserved as a reference scene. The manifest now starts `naval_gameplay.scene` so the normal Play action launches the integrated game instead of the three-box starter scene.

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

Cannons: two enabled saved ShipCannon stations, one centered on each broadside, authored by Editor/CannonAuthoring.cs. The former aft stations remain disabled in the existing scenes so their serialized object references are preserved. Shared station interaction chooses the nearest valid ShipStation; cannon input uses WASD aim and Attack1 fire. Each has +/-35 degree traverse, 0-45 elevation, 1800 muzzle speed, two-second reload, visual recoil and flash. Cannonball uses swept sphere traces (radius 7), gravity 400, inherited ship velocity, water crossing and eight-second cleanup. CannonBurst visuals expire automatically. No damage model yet. Editor-only naval_test_cannons tests both enabled stations, aim, authorization, cooldown, projectile impact and release.

## NPC captain (2026-10-03)

`NavalPrototype.SpawnNpcShip` defaults to true and creates a runtime clone of the authored ship at `NpcShipOffset` (-2400, 1100, 0), with a red-tinted Citizen captain. The saved ship and scene GUIDs remain unchanged. Disable this property for solo handling tests. The player keeps their original ship; the NPC targets it, or selects the nearest other enabled ship when its target disappears.

`ShipNpc` drives a normal PlayerController with input/look/camera controls disabled. `ShipPlayer.IsNpc` suppresses keyboard station controls while retaining deck recovery and release momentum. The captain physically walks along the prototype deck's center aisle, takes stations through the same range/ground/visibility/occupancy checks as the player, and calls SailRig.Adjust, ArcadeShip.SetHelmTarget and ShipCannon.Aim/Fire only while occupying the station. It deploys/trims sails, closes range, furls, turns the nearest broadside toward its target, and fires with motion/drop compensation and an obstruction check. Station hysteresis prevents wave-induced switching. All NPC runtime objects are cleaned up when the prototype ends.

This remains a local prototype, with the existing networking guard and no ship damage/sinking. Deck navigation is tailored to the current layout. Ocean routes now avoid IslandSurface shorelines and include upwind tacks (see navigation update below). Aim predicts constant target velocity; hits are not guaranteed during maneuvers or heavy waves.

Validation: S&box 26.10.02 game/editor compilation passed with zero errors/warnings. Live `naval_test_npc` completed autonomous station traversal, approximately 2,900 units of ship travel, and cannon firing; extended observation showed repeated fire with stable station occupancy. `naval_test_cannons` passed all four human ship cannons (now scoped to their owning ship), and `naval_test_helm` passed. `naval_npc_status` reports activity, station visits, shots, range and positions. No multiplayer support or testing was added.


## Island routes and upwind sailing (2026-10-03)

`SailingRoute` builds a wind-aware visibility graph from enabled `IslandSurface` components. Conservative circles include the outer submerged shore, its 16% shape variation, world scale, and `ShipNpc.IslandClearance` (700 units by default). Contained rings are merged. Every candidate leg, including both sides of a tack, is checked against the shoreline envelopes. Route cost accounts for sailing speed and the extra station work of tacking. Targets inside an unsafe envelope produce a hold rather than a direct course into land.

The NPC retains waypoints between updates and replans when a target moves 450 units, wind changes 10 degrees, or a leg becomes obstructed. It scans shoreline changes every two seconds. Large course changes trigger furling, helm work, and retrimming for the new heading before redeploying. CruisingDeployment defaults to 0.65 to reduce overshoot. Reaching cannon range only ends pursuit when the island route to the target is clear. Navigation status, remaining waypoints and tack counts are available through `naval_npc_status`.

`SailRig.HeadingPower` is now shared by sailing physics and route cost estimation. It permits slower close-hauled sailing while retaining zero drive within 45 degrees of the wind source. Planned tacks sail 60 degrees off the source, leaving a steering margin. This handling change applies to human and NPC ships; deployment, sail alignment, wind strength and station authorization still govern actual drive. Calm wind still gives no propulsion.

Scope: island avoidance covers the authored IslandSurface terrain, not arbitrary moving obstacles or ship-to-ship collision avoidance. Shoreline envelopes are deliberately conservative; narrow channels and targets too close to shore may be rejected. The NPC remains local-only and uses the existing physical station interactions.

Navigation validation: S&box 26.10.02 compilation passed with zero errors/warnings. `naval_test_routes` passed direct travel, two-leg upwind routing, island detours, combined island/upwind routing, wind reversal, unsafe destination rejection, the shared no-go polar, and clearance/sailability of every planned leg. Live `naval_test_navigation false` passed Beacon Island avoidance and cannon engagement in 50 seconds; `naval_test_navigation true` passed an upwind tack change and cannon engagement in 52 seconds. Both sampled actual hull clearance throughout, with human input isolated and scene positions/wind restored afterward. `naval_test_sails` passed afterward; no runtime errors occurred in these successful runs. Arbitrary obstacle layouts, moving-ship avoidance, and multiplayer were not tested.

## Pirate ship layout correction (2026-10-04)

The enlarged `pirate_ship_02` derivative now places the helm interaction beneath the modeled wheel by the aft cabin, centers the sail rig on the main mast, and places one working cannon securely inboard on each broadside. The obsolete gold placeholder helm renderers, prototype box mast/yard, world-space wind-arrow geometry, and box-built cannon renderers are disabled while their gameplay station, muzzle, recoil, and collision transforms remain active. SailRig renders `pirate_ship_02_sails.vmdl`, containing all three canvas islands separated from the matching ship source; raised sails are hidden and lowering restores/scales the authored canvas. The original ship mast and yards remain on the hull. ShipCannon renders `lowpoly pirates/models/cannon.vmdl` at the ship's model scale and follows yaw, elevation, and recoil. The below-deck cargo room occupies the main hold forward of the cabin door, with the stairs descending toward the bow. Its floor, tapered-width walls, split doorway bulkhead, stairs, and collision are rebuilt from the same Blender source.

Validation: the cargo FBX and 24-part compound collision rebuilt successfully, the hull, authored sail, and authored cannon assets compiled, and S&box 26.10.02 reports zero C# compile errors. A dedicated cutaway render verified that the nine-step stair descends through the opened cabin door into the unobstructed forward hold. Live screenshots verified both modeled cannons resting symmetrically on deck, no fixed canvas when raised, and all authored canvas sections visible when lowered. `naval_test_ship_model` passed the moving compound collision, cargo-floor ray, broadside orientation, helm, sail-control, and player-on-ship checks. `naval_test_sails` passed all deployment/rotation/propulsion checks; `naval_test_cannons` passed deck access, mount, aim, fire, impact, and release for both broadsides; `naval_test_helm` passed quarterdeck access, mounting, steering, moving-ship attachment, release, resumed walking, swimming, and recovery. The imported vendor sources still emit non-blocking stale material-path warnings; project material overrides remain in use.
