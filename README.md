# Naval Combat

A game project for S&box. The planned game combines ship physics, PvPvE combat, player economy, ship upgrades, and piracy.

## Play the first voyage

The startup scene is now `Assets/scenes/naval_gameplay.scene`. Press Play to start aboard a ship with 500 coins and an empty cargo hold. This is a playable local trading slice; multiplayer is still being validated, and ship damage, defeat, loot, and pirate attacks are later milestones.

1. Walk to the mast, press **E**, then hold **S** to lower the sail. Use **A/D** to align it with the wind and press **E** to release.
2. Take the bow helm with **E** and steer with **A/D**. The voyage HUD lists every port's distance and direction relative to your bow.
3. Approach Beacon Island's gold trade buoy. Raise the sail at the mast to slow down; trading requires the ship within 650 units of the buoy, speed below 100, and the sailor aboard.
4. Press **1** to invest 100 coins. After 60 seconds, press **1** at Beacon again to load ten Trade Goods. Production is reserved to your sailor and continues while you sail elsewhere.
5. Sail to a green buoy and press **2** to sell your cargo. Beacon pays 18 per unit, Twin Rocks 24, and Palm Island 35, giving a 250-coin profit on a Palm delivery.
6. At any port, use **3 / 4 / 5** to purchase cargo, hull, or sailing upgrades. The HUD shows current levels and the next price. Upgrades last for the current play session.

Four cannons remain usable with **E**, **WASD** aiming, and left-click firing. **C** changes camera view; **R** returns you to the deck. Falling into the sea enters swimming mode.

Editor configuration: the three **trade buoy** objects in `naval_gameplay.scene` expose location, trade radius, production availability, commodity, and sale price. Goods use `.ngoods` resources and upgrades use `.nupgrade` resources (S&box limits custom asset extensions to eight characters). Preserve their `.meta` files when moving resources. The prototype and combat scenes remain available separately.

<img width="1167" height="685" alt="image" src="https://github.com/user-attachments/assets/b8ac1933-23a1-4828-93a0-22119d7e9a60" />

<img width="990" height="661" alt="Screenshot 2026-10-03 105628" src="https://github.com/user-attachments/assets/2f2fe23d-9748-4127-a143-451dbadfd92e" />

## Development plan

### Product loop

The first playable version should prove one complete risk-and-reward voyage:

1. A player spawns with a ship and starting money.
2. They dock at an island and invest in a production site.
3. Production advances over time and creates a finite quantity of goods.
4. The player loads those goods into ship cargo, consuming capacity.
5. They sail to an island where the goods have a better sale price.
6. Cargo makes the voyage valuable and exposes the player to NPC and player piracy.
7. Successful sales fund permanent or match-long ship upgrades.
8. Defeat drops some cargo for recovery or theft, then returns the player to the loop without eliminating them from the session.

The initial target is a small multiplayer vertical slice with three islands, one commodity, one production investment, one cargo hold, one NPC pirate, cannon damage, and three meaningful ship upgrades. Additional content should be data-driven after that slice is fun and reliable.

### Development principles

- Keep gameplay rules in focused C# components and use the editor to compose scenes, connect references, place docks, and tune values.
- Make money, inventory, production, purchases, damage, and loot authoritative. Clients may request actions and present feedback, but do not decide valuable state.
- Separate definitions from runtime state. Goods, upgrades, and island market settings should be reusable resources or serializable definitions; balances, stock, cargo, health, and timers are runtime state.
- Build one end-to-end commodity route before adding breadth. A complete simple loop is more useful than several disconnected systems.
- Keep local prototypes as test beds until their multiplayer replacement passes host/client checks. Do not silently treat the current sailing prototype as network-ready.
- Add editor-visible configuration and diagnostics with each system so designers can tune and verify it without changing code.

### Code and editor foundation

The following component boundaries are the intended groundwork. Names may change during implementation, but ownership should remain clear.

| Area | Code responsibility | Editor responsibility |
| --- | --- | --- |
| Session | `NavalGameSession` owns match phase, joining, spawning, reconnect policy, and authoritative service references | Session root, spawn points, startup scene, network test setup |
| Players | `PlayerWallet` stores authoritative money; player identity links owned character and ship | Starting funds and player spawn placement |
| Goods | `GoodsDefinition` describes a commodity; `CargoHold` stores typed stacks and capacity | Goods assets, ship cargo capacity, cargo interaction points |
| Islands | `IslandPort` identifies dock/load zones; `IslandMarket` quotes and executes trades | Port triggers, interaction markers, island/market assignments |
| Production | `ProductionSite` accepts investment and advances server time into claimable output | Build/claim points, costs, duration, output, capacity |
| Trading | `TradeService` validates distance, stock, cargo room, ownership, prices, and transfers atomically | Base prices and per-island demand multipliers |
| Ships | `ShipOwnership` and a network-ready ship controller define who may operate each station | Ship prefab, helm/sail/cannon stations, spawn/respawn points |
| Combat | `ShipHealth`, damage receivers, cannon authority, sinking/disable flow | Hit volumes, armor sections, effects, wreck/respawn anchors |
| Loot | `CargoCrate` or wreck inventory represents server-spawned recoverable cargo | Drop presentation and pickup radius |
| Upgrades | `ShipUpgradeDefinition` and `ShipUpgradeManager` validate purchases and apply modifiers | Upgrade assets, shop location, prices, stat tuning |
| NPCs | Pirate perception and goals choose pursuit, attack, loot, retreat, and delivery; existing station AI executes ship tasks | NPC routes/spawns, difficulty profile, patrol areas |
| UI | Read-only view models expose wallet, cargo, production, market, health, and upgrade state | HUD/panel composition and presentation tuning |
| Persistence | A versioned profile boundary saves only the progression deliberately selected for release | Development reset/migration controls |

Gameplay transfers should be atomic: validate the complete request first, then update money, stock, cargo, or upgrades as one authoritative operation. Definitions should use stable IDs so saved state and network messages never depend on scene object names.

### Milestones and feature tracking

Status legend: ✅ verified prototype, 🟡 partial/local prototype, ⬜ not started. A prototype is not considered complete for the main game until it is integrated into the vertical slice and tested with a host and client.

#### 0. Baseline and design contracts

- ✅ Arcade ship handling, physical deck, helm, sail, ocean, and three authored islands
- ✅ Four cannon stations with ballistic projectiles and impact effects
- ✅ Local NPC captain that sails, avoids islands, operates stations, and fires cannons
- ✅ Separate multiplayer sword arena with host-authoritative damage
- ✅ First-slice session rules and balance contract are documented below
- ✅ Stable-ID trade-goods and three ship-upgrade assets exist alongside the gameplay code foundation
- ✅ `Assets/scenes/naval_gameplay.scene` is the independent integration scene; prototype and combat test scenes remain preserved

Exit criteria: the rules for the first voyage are explicit, the integrated scene opens cleanly, and existing prototype tests still pass.

Milestone 0 is complete. Its first-slice contract is:

| Rule | Initial value |
| --- | --- |
| Session target | 30 minutes; session flow is implemented later |
| Starting money | 500 per player |
| Defeated cargo | 75% drops as loot; rounding and stack selection are implemented with loot |
| Respawn cost | Free for the vertical slice to prevent elimination and deadlocks |
| Upgrade lifetime | Match-scoped; all upgrade levels reset when the session ends |
| Starting cargo capacity | 20 units |
| Commodity | `goods.trade_goods` / Trade Goods, one capacity unit each |
| Production investment | 100 money, 60 seconds, 10 Trade Goods |
| Beacon Island role | Producer: claim production here; market buys Trade Goods for 18 |
| Twin Rocks role | Mid-market: buys for 24 and sells for 28 |
| Palm Island role | Destination: buys Trade Goods for 35 |
| Cargo upgrade | `upgrade.expanded_hold`: +5 capacity per level |
| Hull upgrade | `upgrade.reinforced_hull`: +150 maximum health per level |
| Sailing upgrade | `upgrade.improved_rigging`: +10% sailing performance per level |
| Upgrade pricing | Three levels; base prices 300/350/325 with a 1.75× level multiplier |

These are tuning baselines, not promises of final balance. Economy values remain authoritative and integer-valued. The integrated scene initially inherits the proven local sailing world; subsequent milestones replace its local-only bootstrap with multiplayer session spawning and wire the authored economy components into its islands and ship.

#### 1. Authoritative multiplayer sailing shell

- 🟡 Spawn one owned ship and character per connected player (session spawning and ownership are implemented; live two-instance verification remains)
- 🟡 Define authority for hull physics, stations, sails, cannons, projectiles, and NPC ships (player hulls are owner-simulated; station occupancy and cannon acceptance are host-validated; NPC authority remains)
- 🟡 Synchronize ship motion and station occupancy with usable remote interpolation (network objects and synchronized occupancy/sail/cannon state are implemented; remote feel remains to be tuned)
- 🟡 Handle join, leave, reconnect, owner loss, and ship cleanup (join, disconnect cleanup, and host slot rebuilding exist; reconnect/host migration need live verification)
- 🟡 Add host/client smoke checks for station authorization and cannon firing (`naval_test_network_shell` checks ownership structure; active denial/fire checks remain)

Exit criteria: two players can join, board only permitted stations, sail, observe each other, and fire without valuable state being client-controlled.

The integrated scene now uses `NavalMultiplayerSession` instead of the local prototype bootstrap. It keeps the authored player ship as a disabled template, clones one ship and sailor for each connection, assigns both to that connection, and creates camera/HUD presentation locally. Ship movement, sail trim, and cannon aim are owner-simulated for responsiveness. The host owns station occupancy decisions and cannon-fire acceptance; cannonballs are host-spawned network objects. Run the scene as host, choose **Join via new instance**, then run `naval_test_network_shell` on the host to inspect connection-to-ship ownership before performing the physical station and firing checks.

#### 2. Economy and production vertical slice

- 🟡 Add authoritative wallets with starting funds and transaction reasons (wallet foundation exists; transaction history/reasons remain)
- ✅ Local goods resource and capacity-limited ship cargo are wired into the playable scene
- 🟡 Offshore trade buoys validate ship distance, sailor proximity, speed, and RPC ownership; full multiplayer validation remains
- ✅ Per-player production with a visible countdown, reserved output, and capacity-safe loading at Beacon
- 🟡 Atomic production loading and cargo sales work through the request boundary; market purchases/unloading remain
- ✅ Local voyage HUD shows money, cargo, production, route guidance, sale prices, upgrade prices, and transaction feedback
- 🟡 Live `naval_test_voyage` covers investment, timed completion, early/duplicate/full-hold claims, out-of-range requests, sales, and upgrade affordability; disconnect and multiplayer cases remain

Exit criteria: a host and client can independently invest, wait, load, sail, sell, and see correct replicated balances without duplication or negative values.

#### 3. Ship combat, defeat, and piracy

- 🟡 Cannon aiming/projectile foundation exists locally; damage is not implemented
- 🟡 Add ship health, armor/damage rules, hit attribution, repair rules, and clear feedback (health/repair foundation exists)
- ⬜ Define disabled/sinking behavior that does not strand players
- ⬜ Convert a configured percentage of defeated cargo into authoritative floating loot
- ⬜ Add pickup validation, temporary ownership protection if needed, and despawn rules
- ⬜ Add ship respawn with spawn protection and anti-camping placement
- ⬜ Validate simultaneous pickup, disconnect during defeat, friendly-fire policy, and stale projectile ownership

Exit criteria: either player can damage and defeat the other, cargo loss is conserved exactly, loot can be stolen, and both players re-enter the economy loop.

#### 4. Upgrades and economic choices

- 🟡 Trade buoys provide a host-validated upgrade shop; dedicated shipyard presentation remains
- ✅ Local cargo capacity, maximum/current hull health, and sailing speed upgrades apply their resource-defined effects
- 🟡 HUD shows current level, price, and purchase result; richer effect previews remain
- ✅ Centralized upgrade modifiers apply from captured base stats without compounding each frame
- 🟡 Three levels and match-scoped reset are implemented; resale/refunds are not offered in this slice

Exit criteria: earnings create distinct ship builds, all effects replicate, and reconnecting cannot duplicate or discard purchases.

#### 5. Pirate NPC loop

- 🟡 Local NPC navigation, station use, pursuit, and cannon firing exist
- ⬜ Move NPC decisions and valuable outcomes under session authority
- ⬜ Add target scoring based on cargo value, distance, danger, and recent attackers
- ⬜ Add patrol, pursue, attack, loot, retreat, and deposit states
- ⬜ Add disengage rules, difficulty tuning, and recovery from stuck or lost stations
- ⬜ Reuse the same cargo, damage, loot, and market APIs used by players

Exit criteria: a pirate can discover a loaded ship, engage it, steal dropped goods, and leave; empty or newly spawned ships are not relentlessly targeted.

#### 6. Content, balance, and persistence

- ⬜ Expand to several goods with readable regional supply/demand differences
- ⬜ Add more production choices, islands, upgrade branches, and NPC profiles through definitions rather than bespoke code
- ⬜ Add event logging/telemetry for income, losses, route time, combat outcomes, and upgrade choices
- ⬜ Balance travel time, production time, margins, danger, recovery, and snowball prevention
- ⬜ Implement versioned persistence only after the match loop and reset rules are stable
- ⬜ Add onboarding, settings, accessibility, audio, final art, and performance budgets

Exit criteria: repeated sessions create viable trading and piracy decisions, losing players can recover, and content can be added primarily in the editor.

### Recommended first implementation slice

Build milestone 2 as a local authoritative-domain simulation before attaching it to networking or polished UI. Start with plain C# transaction rules for wallet, cargo, production, and trade, then expose thin S&box Components for scene references, interactions, replication, and presentation. This keeps economic invariants testable while the editor remains the place for island layout and tuning.

The first code slice should include:

1. Stable definition IDs and a minimal goods catalog.
2. Wallet and cargo APIs that cannot create negative balances, exceed capacity, or partially complete a failed transfer.
3. A production clock based on authoritative elapsed time rather than a client countdown.
4. Market quote and transaction results with explicit failure reasons for UI feedback.
5. Port proximity/ownership validation at the component boundary.
6. Diagnostic commands or tests that run the entire invest-to-sale loop and adversarial duplicate requests.

Only after those rules pass should the scene receive dock triggers, interaction prompts, panels, and network request handlers.

### Editor-driven development workflow

For each milestone:

1. Implement the smallest complete rule set in `Code/` and expose only designer-relevant properties.
2. Compile/hotload in S&box and resolve all game/editor errors before scene work.
3. Add or connect GameObjects, Components, triggers, references, and definition assets outside Play mode; save and reopen the scene to verify serialization.
4. Exercise the feature locally with diagnostics, then perform a physical play pass.
5. Use **Join via new instance** for all authoritative or replicated behavior and test both host and client perspectives, including denial cases.
6. Record actual validation in `docs/PROJECT_CONTEXT.md` and update the tracker here. Mark an item complete only when its exit criteria are met.

Avoid building core state solely in scene scripts or UI. The editor should configure and visualize the game; authoritative C# services and components should enforce its rules.

## Try sword combat

Open `Assets/scenes/combat_test.scene` and press Play. A separate enclosed arena spawns a swordsman and a red sparring NPC. WASD moves, mouse looks, left click slashes, Shift runs, Space jumps, and C switches camera view. Approach the NPC to spar; it turns toward nearby players and attacks within sword range, but does not chase.

Each fighter has 100 health. Sword hits deal 25 damage, with a 0.65-second cooldown (the NPC attacks half as often). Defeated fighters cannot attack and respawn after three seconds. The HUD shows your health and hit/miss feedback. Swords and characters use placeholder geometry and Citizen models.

For PvP, use the editor network menu to start hosting, then **Join via new instance**. Each connection owns its movement; the host validates sword cooldown, health, range, obstruction and damage. Run `naval_test_combat` in local play or `naval_test_combat_network` with exactly two connected players for integration checks. The arena is separate from sailing; open `combat_test.scene` explicitly when testing sword combat.

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

The sailing prototype remains local and refuses startup in an active network session. Multiplayer spawning is handled by the integrated gameplay scene, while sword combat remains available in the separate combat arena described above. The project now starts in `naval_gameplay.scene`; the original minimal scene remains preserved for reference.

Handling values are inspector properties on `ArcadeShip`; initial defaults live in `Code/Ships/ArcadeShip.cs`. The ship and islands are saved scene objects. Edit them outside Play mode and save the scene to retain changes. Player, camera, HUD and wind streaks are created at runtime; play-mode edits are temporary.

Verified in the installed S&box editor: successful compilation with no errors or warnings, rendered Citizen/clothing and HUD, deck landing/walking, helm range restrictions, mount, acceleration/steering, attachment to the moving ship, release with restored physics, walking after release, and overboard recovery.

To repeat the physics integration check, play the scene locally and run `naval_test_helm` or `naval_test_sails` in the editor console. It temporarily controls the player, reports each assertion, and restores input afterward. It only runs in the editor with networking inactive. Physical keyboard/mouse feel and multiplayer behavior are not covered by this automated check.

## Open the project

1. Open `naval_combat.sbproj` with the S&box editor.
2. Press Play to launch `Assets/scenes/naval_gameplay.scene`, or open another preserved test scene explicitly.
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

Save the scene after editing. Keep helm, mast, spawn and component references connected. Terrain and sail previews regenerate from their saved settings; they are not external model assets. The project startup scene is the integrated `naval_gameplay.scene`.

The ocean now uses a Wind Waker-inspired blue palette, drifting white foam outlines and broad rolling swells with softer small waves. WaveHeight and FoamStrength remain editable on the saved Ocean object.

Four editable cannon stations sit on the ship, two per side. Press E near one, use A/D to traverse and W/S to adjust elevation, then left-click to fire. E releases the station. Each cannon has a two-second reload, barrel recoil, ballistic iron shots, muzzle flash and water/solid impact effects. Cannonballs collide with the world; damage and destructible ships are not implemented.
