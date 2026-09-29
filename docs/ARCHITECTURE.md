# Architecture

## Layers

```
┌───────────────────────────── frontends (replaceable) ─────────────────────────────┐
│  godot/ (Godot 4 .NET)     src/FactorySim.Cli (ASCII, bench, balance)  future: server │
│  SimHost · GameFlow · WorldView · BuildController · Hud · AudioManager              │
└───────────────┬──────────────────────────────────────────────┬─────────────────────┘
       reads    │ World queries, View/*, drained SimEvents      │ writes: Execute(Command) / EditHistory
┌───────────────▼──────────────────────────────────────────────▼─────────────────────┐
│ FactorySim.Core  (net8.0, BCL only)                                                  │
│  Simulation ── fixed 20 Hz tick, commands, event queue, offline catch-up             │
│  World ─────── sparse 3D grid, entities, money, upgrades→stats, stats, RNG           │
│  Behaviors ─── conveyor · router · miner · processor · seller  (state on entity)    │
│  Editing ───── blueprints, batch commands, undo/redo, BuildPlanner (drags, bridges) │
│  Progression ─ customer orders (contracts), milestones                             │
│  Content ───── JSON packs → validated registry (tiers, items, buildings, recipes,   │
│                placement rules)                                                     │
│  Balance ───── the content as an economy: recipe book, production chains, tier      │
│                pacing. Calculates only, never runs; used by the CLI's balance report │
│  Persistence ─ versioned JSON saves        View ─ shared path geometry, view models │
└──────────────────────────────────────────────────────────────────────────────────────┘
```

Rules that keep it decoupled:

- **The core never references an engine.** `ContentAndArchitectureTests` fails if
  `FactorySim.Core` references anything but the base class library.
- **Frontends read, never write.** They read `World`, `Entity`, `View/*` and
  drained `SimEvent`s, and change state only through `Simulation.Execute(Command)`.
  Every change is validated, applied between ticks, and can be logged (`CommandLog`).
- **All engine coordinate mapping lives in one file** (`godot/scripts/GridMapping.cs`).

## Simulation model

### Grid and verticality

`GridPos(X, Y, Z)`: X east, Y south, Z up. Z = 0 is the ground plate and the lowest
level (`GridBounds.Min.Z` is 0, and saves from older versions are clamped to it).
The grid is sparse (a dictionary), so its size costs nothing. `World.Bounds` is the
whole map (`Land.Bounds`): a fixed grid of plots, `MapDef` in `base.json` (15-cell plots,
5 columns by 5 rows, start plot bottom middle, base price and growth per ring).

### Land

`Land` (`World.Land`) is the ownership layer on that map: a set of owned `PlotId`s (column,
row), always containing the start plot. Only owned cells can be built on (`World.OwnsCell`,
`World.CellProblem` say why not); sandbox owns everything without changing the set. `BuyPlot`
is the command and `PlotBought` the event. A plot can be bought when it shares an edge with an
owned plot (`Land.Touches`, `Land.WhyNot`). Its price is `PlotPrice * PriceGrowth^(d-1)`, where
`d` is the Manhattan distance in plots from the start plot (`Land.PriceOf`), so it never depends
on how much land is owned. `Land.Buyable()` lists what can be bought, cheapest first. Belt
routing only runs over owned cells. `LandPacing` (Balance) prices the rings against income for
`balance land`.

The saved land (`LandSave`: plot size and owned plots, save version 2) is restored plot by plot.
Loading never drops a building: a plot under any saved building is owned, and a map too small
for an old save grows to fit it. Version 1 saves (before plots) own every plot their old
buildable area touched, which can leave owned plots apart from the start plot.

A building def has a **footprint** (local cells, facing north) and **ports**. A
port is an `in`/`out` on a *side* of a *footprint cell*. An output on side S of
cell C delivers into cell C+S. It connects only if the building there has an
input on the facing side of that same cell.

Verticality needs no special cases. A ramp is a conveyor whose footprint spans two
layers, with its input on the lower cell and its output on the upper one (or the
reverse). A bridge is ramp up → belt at z+1 → ramp down, and lines at z pass
underneath. Lifts and multi-level machines use the same mechanism.

### Tick and ordering

- Fixed timestep: `Simulation.TicksPerSecond = 20`. `Advance(seconds)` accumulates
  real time, and `Alpha` gives the fraction toward the next tick for interpolation.
- `Topology` resolves port links and a **downstream-first update order** (post-order
  DFS along outputs). It is rebuilt only when the layout changes. Ticking sinks
  before sources frees space before anything pushes into it, so saturated lines
  move as one without gaps, and an item moves at most once per tick.

### Items and transport

- `ItemStack` is a bundle: `Type`, `Count`, `UnitValue` (BigNum) and `Tags` (status
  effects and markers like `polished` or `heat`). `Count` is the scaling lever:
  stack-size upgrades let one simulated object carry many units, so throughput can
  grow without increasing simulation cost.
- **Conveyor**: each tile holds items at integer positions 0…1000, front first.
  Items advance `speed` units per tick and queue at `spacing`. Overflow past an
  edge carries into the next tile, so speed is exact across tiles (tested). Back
  inputs enter at the overflow position, and side inputs from machines and hubs
  enter at mid-tile when there is a gap. Ramps and in-line upgraders (`effect`: value multiplier or tag,
  optionally once per item) are the same behavior with different data.
- Senders pass `TickContext.Waiting` for an item that was already waiting at their
  edge. Belts can then place it as far forward as their speed allows, so machine
  outputs keep belts fully compressed.
- **Curves are automatic.** A belt whose back input isn't fed but exactly one side
  input is becomes a curve (`ConveyorBehavior.CurveSide`): items from that side
  enter at the start of the tile instead of merging at mid-tile. Players never pick
  a "corner piece".
- **Belts do not merge.** `Topology.Rebuild` cuts a belt-to-belt link into a side unless that
  side is the receiving belt's only input (a curve), so a belt beside a running line just backs
  up against it; joining lines is the merger's job. Machines and hubs may still feed a belt's
  side. The cut is decided from the links as first resolved (order independent) and `Fed` is
  recounted afterwards, so the receiving belt stays straight. The planner mirrors it
  (`ConveyorBehavior.TakesBeltAt`): no route ends at a side a belt would refuse.
- **Router** (splitter 1→3, merger 3→1): a hub tile that picks outputs round-robin
  at mid-tile and skips blocked ones at the exit. Merging is fair: while the
  preferred input has items waiting, other inputs are refused, then the preference
  rotates. Both are tested with saturated inputs.
- **Shared geometry.** `View/TransportPath` defines every path in building-local
  space: straight, S-curved ramps (smoothstep), quarter-circle curves, and hub
  entry→centre→exit. The simulation positions items with it, and the Godot client
  sweeps belt meshes along it, so items ride exactly on the drawn belt.

### Machines

| Behavior | Role | Notes |
|---|---|---|
| `miner` | source | Work accrues at `miner.rate` × the drill's level speed. Rates above 1 cycle/tick merge into bundles up to the stack size. |
| `processor` | recipes | Buffers inputs from any port and crafts at `machine.speed` × level speed. Output value = consumed input value × `valueMultiplier` × level value. Smelter, press and assembler are the same behavior with different recipes. |
| `seller` | sink | Pays value × count × `sell.multiplier` × level value, and only 25% for items marked `raw`. |
| `conveyor` | transport | Belts, ramps and in-line effects. |
| `router` | transport | Splitters and mergers (see above). |

### Economy, upgrades, stats

- `BigNum` (mantissa × 10^exponent) for money and values, far past 1e308. It uses
  only basic IEEE arithmetic and a power-of-ten table (never `Math.Pow`/`Log10`),
  so results are **bit-identical across machines**.
- **Per-building levels.** Every entity has a `Level`. Its def's `UpgradeTrack`
  (speed and value per level, cost factor and growth, optional max) comes from the
  JSON `upgrade` field or the behavior's `DefaultUpgrade`. `SetBuildingLevels` changes
  several levels atomically and is undoable; removing a building refunds everything
  invested in it; blueprints keep levels and charge for them.
- **Tiers and limits.** `TierDef`s gate buildings by `tier`, need lifetime earnings, a
  delivery of goods (`deliver`: lifetime sales of items, so `DeliveriesMet` reads
  `StatsTracker.Sold`, which is saved and never spent) plus a price (`UnlockTier`).
  `ContentRegistry.ValidateTiers` refuses a delivery of an item that cannot be made
  before that tier, which would make the tier impossible. `limit` (base, plus `perTier` for every
  `every` tiers after the building's own) caps extractors and depots; placement, paste
  and undo all check it. Depots have a single input, which makes them the bottleneck.
- **Choices.** `SelectRecipe` sets what a machine makes (null = automatic): it then only
  accepts that recipe's ingredients and drops buffered ones it can't use. Behaviors expose
  it through `IBehavior.Selection`/`Select`; it is undoable, saved, and kept by blueprints.
- **Reference values.** `ItemValues` (`ContentRegistry.ItemValue`) follows the recipes from
  raw resources to give every item's level-1 value and the tier it becomes available. The
  Manage window shows it, and a test checks that each tier's best product is worth at
  least double the last. Every ore is worth $1, so an item's value is the work in it.
- **Recipe tree.** `base.json` holds one connected tree: shared parts feed many recipes and
  only the final product (the satellite) is sold and used in nothing. Tests walk the tree to
  check that: no recipe cycle, no dead end, one late product pulls every raw resource
  through the factory, and every machine's `inputCapacity` can hold the inputs of all its
  recipes (`ProcessorBehavior.Bind` refuses one that cannot, which would idle the machine).
- **Replacing.** A def's `group` and `replaces` say what it may be dropped onto
  (`PlaceBuilding(Replace: true)`); the old building is refunded, and items on a belt
  survive a swap between belt pieces.
- Global stat upgrades still exist as a mechanism for mod packs (research, events).
  The base game defines none. `World.Stat(key)` composes them and caches the result.
- `StatsTracker` keeps lifetime totals and a rolling 60 s income window.
  `Snapshot()` is the deterministic summary for leaderboards and shared stats.

### Orders and goals

`Progression/Goals.cs` runs once per simulated second while `World.Goals` is on (it is off
for the title-screen backdrop and in most unit tests, which count money exactly):

- **Orders** (`ContractBoard`, three slots). Every 30 s, once the factory has sold
  something, a free slot gets a new `Contract`: a non-raw item from an unlocked tier
  (newer tiers weighted 3/2/1), a quantity worth 45 to 90 s of current income rounded to a
  nice number, a 6 to 12 minute deadline, and a reward of 2 to 2.5 times its value. An item
  is only offered while an order for it stays under the largest nice number (1000), so cheap
  parts are asked for while income is small and expensive goods take over later.
  `TickContext.Sell` calls `Deliver`, so items still sell normally and also count
  toward the oldest open order for that item. `RerollContract` swaps one for 10% of its
  reward. Everything is drawn from the seeded `Rng`, so orders are deterministic.
- **Milestones** (`MilestoneDef` in the content pack, kinds `earned`, `sold`, `produced`,
  `built`, `level`, `contracts`, `tier`). `MilestoneProgress` gives 0..1 for the UI;
  reaching one pays its reward and raises `MilestoneReached`.
- Both are saved (`SaveData.Contracts`, `Milestones`), and rewards count as earnings.

### Determinism

Identical content + save + command sequence ⇒ identical results. The simulation
never reads a clock or `System.Random` (it uses a seeded `Rng` stored in the save),
iterates entities in id/topology order, and uses deterministic math. Tests check
that two runs match byte for byte, and that save → load → continue matches an
uninterrupted run byte for byte. This is what makes **replay-verified leaderboards**
possible: a server can re-run a submitted command log with the same core.

### Offline progress

`sim.CatchUp(seconds)`:

1. Actually simulates up to `MaxSimulatedSeconds` (default 10 min), so belts fill
   and bottlenecks show.
2. Measures income over the second half of that window (steady state).
3. Extrapolates that rate over the rest of the time (× `Efficiency`, a design
   lever). The cost is bounded however long the player was away, and short
   absences are exact.

The host supplies the wall-clock gap from the save's `SavedAtUtc`.
Extrapolated time adds money and lifetime earnings but not per-item sold counts.

### Editing

`FactorySim.Editing` makes building ergonomic without special-casing the simulation:

- `Blueprint`: building entries relative to an origin. It is captured from a
  selection, can be rotated, and serializes to JSON (the clipboard, and later a
  sharing format and the input to "compress into one machine").
- Batch commands: `PlaceBlueprint` is atomic (all cells free and affordable, or
  nothing happens). `RemoveBuildings` removes many at once. `MoveBuildings` moves
  and rotates a group atomically, keeping entity ids and state, so items stay on
  moved belts.
- `EditHistory` wraps `Execute`: each edit is recorded with its inverse, computed
  from the world *before* it runs. Inverses are expressed by cell rather than
  entity id, so they stay valid when undo/redo recreates buildings. `BeginGroup`
  and `EndGroup` make a dragged line a single undo step. Level changes are undoable;
  tier unlocks are not.
- `BuildPlanner` turns a click or drag into placement steps, independent of any
  frontend: L-shaped paths, belts facing along the drag, re-aiming existing belts,
  never downgrading pricier pieces, keeping a replaced building's direction, the
  anchor height for ramps (a ramp down placed on the ground stands on it), turning a
  single-input building (a depot) toward the belt that feeds its cell (`FaceFeeder`),
  **routing** (`Route`, in `BuildPlanner.Routing.cs`: A* over your land for the fewest cells,
  then the fewest turns; it goes around buildings, bridges perpendicular belt lines as one
  macro step (ramp up, deck, ramp down, straight on), avoids cells other buildings output
  into, and starts from an output or ends in an input when the drag starts or ends on a
  building or belt; `ExtendTrail` is the hand-drawn Shift-drag path), and
  **automatic bridges** (a belt dragged straight across other belt lines gets a ramp up,
  a deck one level higher and a ramp down). It is unit-tested like the rest of the core.

### Tutorial

`FactorySim.Guide.Tutorial` is the first-factory walkthrough as data: each step has a
title, text, an optional check against the `World` (a drill exists, a smelter is fed,
$10 earned, a drill reached level 2) and a focus hint for the frontend to point at. The
client's `TutorialPanel` presents it and moves on when a check passes; a test plays it
through with commands and the starting money.

### Persistence

`SaveSystem` writes versioned JSON: world scalars, orders and reached goals, upgrades, stats, and entities
with their behavior state (each serialized through the behavior's own state type,
so new behaviors need no central registration). Caches such as the grid index,
links and stat cache are rebuilt on load. Unknown content is dropped with warnings
instead of failing. `Migrate()` is the hook for version bumps.

The client writes every file through `SafeFile` (`godot/scripts/Persistence`): the new
contents go to a `.tmp` file, are flushed to disk, and only then replace the real file,
so a crash, kill or power cut mid-save can't leave a truncated save. Save slots also keep
the previous save as `slotN.json.bak`; if a slot fails to load, the backup is loaded and
restored, with a notice to the player. A save from a newer game version fails instead of
falling back, so the next save can't overwrite it with the older backup.

## Extending

- **New building with existing behavior:** JSON only (footprint, ports, params, meta).
- **New behavior:** subclass `Behavior<TParams, TState>` and implement `Tick`,
  `TryAccept`, `CollectItems` and `GetStatus`. Validate params in `Bind`. Register
  it in `BehaviorRegistry`. Keep state plain and serializable, and keep randomness
  on `ctx.Rng`.
- **New command:** add a `Command` record and a case in `Simulation.Execute`.
- **New frontend:** read `World`, `TransportPath.CollectAll` for item positions,
  and `Behavior.GetStatus`/`Describe` for machine state, drain `Simulation.Events`,
  and send `Command`s (through `EditHistory` for undoable edits).
- **New look:** add a case to `godot/scripts/Visual/ModelFactory.cs`, keyed by the
  def's `meta.model`. Models are built with `MeshBuilder` (beveled boxes, faceted
  cylinders, beams, profile sweeps), cached per def, and shared between instances.
  Animated parts (spinners, bobbers, glows, smoke) go into the `ModelRig`, which
  eases them with the machine's working state.

## Godot client

| Folder | Contents |
|---|---|
| `Visual/` | `MeshBuilder` (procedural geometry), `ModelFactory` (all building models), `WorldView` (instancing, curve/pillar-aware rebuilds, item MultiMeshes with tick interpolation, highlights, floating income), shaders, lighting and ground |
| `Input/` | `CameraRig` (orbit/pan/zoom-to-cursor), `BuildController` (select, build, upgrade, delete, move, paste, pipette, undo, build height; uses `BuildPlanner`), `GhostLayer` (translucent previews with port arrows and pillars) |
| `UI/` | `Hud` (tool bar, sidebar, factory card with the next goal, height ladder, hotbar, key hints, cursor tooltip), `HudWindow` (draggable windows, several open at once), `ManageWindow` (the selection: stats, upgrade, recipe choice), build menu with locks and limits, progress (tiers, limits), stats, vector `IconView`, `Thumbnails` (renders building and item icons from the 3D models) |
| `Dev/` | `UiScenario`: scripted end-to-end test that injects real input events |
| `UI/TutorialPanel` | Presents the tutorial steps with a pulsing outline on the control each step is about |
| `UI/Menus` | `MenuLayer`: title screen, pause menu, save slots, name and confirm dialogs, `SettingsPanel`, credits. It keeps processing while the tree is paused |
| `UI/HudPanels` | Progress (tiers, limits, next goals), `OrdersPanel` (order cards with a swap button), statistics |
| `Audio/` | `AudioManager`: Music, Effects (positional) and Interface buses; random clip and pitch per play, per-sound cool-downs, crossfading playlists; maps `SimEvent`s to sounds and gives every button a click |
| `GameFlow` | Title → playing ⇄ paused. The title runs a demo factory with goals off behind the menu and an orbiting camera; the pause menu pauses the whole tree, `Space` only the simulation |
| `GameSettings` | Preferences in `user://settings.json`: audio (volumes, mute in background), display (window mode, vsync, FPS cap and counter, quality, UI scale capped to the window), camera (pan speed, edge pan, inverted zoom), gameplay toggles, autosave, tutorial seen, rebound keys |
| `Input/Keybinds` | Rebindable single-key actions with defaults; binding a used key swaps the two. Esc, hotbar numbers, Ctrl shortcuts, Delete, arrows and PageUp/PageDown stay fixed. Tooltips, hints, help and tutorial text read the current keys (tutorial text uses `{action}` tokens) |
| `SimHost` | Owns the `Simulation`: five save slots (`user://saves/slotN.json` plus a small `.meta.json` for the slot list), autosave, play time, offline catch-up on load, and moving saves from older versions into slot 1 |

**Audio assets.** Sounds live in `godot/audio/sfx/{name}_{n}.ogg`; a named sound plays one
of its files at random. Adding a variation is dropping in another numbered file. Music is
`godot/audio/music/*.ogg`. Every file is a recording (Kenney CC0, Kevin MacLeod CC BY 4.0);
`godot/audio/CREDITS.md` lists them.

Scene graph order matters for input: the HUD is the last child, so it sees unhandled
keys first (hotbar, menus, Esc for panels). Everything else falls through to the
`BuildController`.

## Roadmap (suggested next steps)

1. **Logistics depth:** filters/sorters, lifts, belt tiers, and hotbar drag-and-drop.
2. **Active loop:** per-entity overclock (a stat scoped to an entity) and anomalies
   (seeded `Rng` events that spawn on machines and reward a click).
3. **Status effects:** a heat model on items (`heat` tag + decay), heaters,
   coolers, and quench recipes that require a tag.
4. **Compression:** "blueprint → condensed machine". Measure a sub-layout's
   steady-state input/output ratios with the headless sim and emit a generated
   `BuildingDef` (a processor with a synthesized recipe) that replaces it on a
   single footprint.
5. **Progression:** research-style global unlocks on top of tiers (the stat-upgrade
   mechanism is still there for it), and orders from named customers with reputation.
6. **Online:** record `CommandLog` + seed, submit with `StatsSnapshot`, and verify
   server-side by replaying with the same core (ASP.NET or a CLI worker).
7. **Performance, when needed:** belt segments (Factorio-style transport lines),
   struct-of-arrays for items, running the sim on a worker thread (events are
   already queued, not callbacks), and System.Text.Json source generation for
   NativeAOT/iOS.
