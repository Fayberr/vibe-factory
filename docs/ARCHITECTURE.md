# Architecture

## Layers

```
┌───────────────────────────── frontends (replaceable) ─────────────────────────────┐
│  godot/ (Godot 4 .NET)          src/FactorySim.Cli (ASCII, bench)     future: server │
│  SimHost · WorldView · BuildController · Hud                                        │
└───────────────┬──────────────────────────────────────────────┬─────────────────────┘
       reads    │ World queries, View/*, drained SimEvents      │ writes: Execute(Command) / EditHistory
┌───────────────▼──────────────────────────────────────────────▼─────────────────────┐
│ FactorySim.Core  (net8.0, BCL only)                                                  │
│  Simulation ── fixed 20 Hz tick, commands, event queue, offline catch-up             │
│  World ─────── sparse 3D grid, entities, money, upgrades→stats, stats, RNG           │
│  Behaviors ─── conveyor · router · miner · processor · seller  (state on entity)    │
│  Editing ───── blueprints, batch commands, undo/redo (EditHistory)                  │
│  Content ───── JSON packs → validated registry (items, buildings, recipes, upgrades) │
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

`GridPos(X, Y, Z)`: X east, Y south, Z up. Negative Z is underground, for tunnels.
The grid is sparse (a dictionary), so its size costs nothing. `GridBounds` limits
the buildable plot, and expanding it is a natural progression hook.

A building def has a **footprint** (local cells, facing north) and **ports**. A
port is an `in`/`out` on a *side* of a *footprint cell*. An output on side S of
cell C delivers into cell C+S. It connects only if the building there has an
input on the facing side of that same cell.

Verticality needs no special cases. A ramp is a conveyor whose footprint spans two
layers, with its input on the lower cell and its output on the upper one (or the
reverse). A bridge is ramp up → belt at z+1 → ramp down, and lines at z pass
underneath. Lifts, tunnels and multi-level machines use the same mechanism.

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
  inputs enter at the overflow position, and side inputs merge at mid-tile when
  there is a gap. Ramps and in-line upgraders (`effect`: value multiplier or tag,
  optionally once per item) are the same behavior with different data.
- Senders pass `TickContext.Waiting` for an item that was already waiting at their
  edge. Belts can then place it as far forward as their speed allows, so machine
  outputs keep belts fully compressed.
- **Curves are automatic.** A belt whose back input isn't fed but exactly one side
  input is becomes a curve (`ConveyorBehavior.CurveSide`): items from that side
  enter at the start of the tile instead of merging at mid-tile. Players never pick
  a "corner piece".
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
| `miner` | source | Work accrues at `miner.rate`. Rates above 1 cycle/tick merge into bundles up to the stack size, so the upgrade has no hard cap. |
| `processor` | recipes | Buffers inputs from any port and crafts at `machine.speed`. Output value = consumed input value × `valueMultiplier`. Smelter and alloy forge are the same behavior. |
| `seller` | sink | Pays value × count × `sell.multiplier`. |
| `conveyor` | transport | Belts, ramps and in-line effects. |
| `router` | transport | Splitters and mergers (see above). |

### Economy, upgrades, stats

- `BigNum` (mantissa × 10^exponent) for money and values, far past 1e308. It uses
  only basic IEEE arithmetic and a power-of-ten table (never `Math.Pow`/`Log10`),
  so results are **bit-identical across machines**.
- Upgrades are data. Each one targets a stat key, multiplies or adds, and scales
  cost geometrically. They are uncapped unless `maxLevel` is set; belt speed is
  capped because it is physically bounded by spacing. `World.Stat(key)` composes
  them and caches the result.
- `StatsTracker` keeps lifetime totals and a rolling 60 s income window.
  `Snapshot()` is the deterministic summary for leaderboards and shared stats.

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
  and `EndGroup` make a dragged line a single undo step. Upgrades are not undoable.

### Persistence

`SaveSystem` writes versioned JSON: world scalars, upgrades, stats, and entities
with their behavior state (each serialized through the behavior's own state type,
so new behaviors need no central registration). Caches such as the grid index,
links and stat cache are rebuilt on load. Unknown content is dropped with warnings
instead of failing. `Migrate()` is the hook for version bumps.

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
| `Input/` | `CameraRig` (orbit/pan/zoom-to-cursor), `BuildController` (select, build with line drag, delete, move, paste, pipette, undo, ramp-aware layers), `GhostLayer` (translucent previews with port arrows) |
| `UI/` | `Hud` (tool bar, sidebar, factory card, hotbar, key hints, panels), build menu, inspector, upgrades/stats, vector `IconView`, `Thumbnails` (renders icons from the 3D models) |
| `Dev/` | `UiScenario`: scripted end-to-end test that injects real input events |

Scene graph order matters for input: the HUD is the last child, so it sees unhandled
keys first (hotbar, menus, Esc for panels). Everything else falls through to the
`BuildController`.

## Roadmap (suggested next steps)

1. **Logistics depth:** filters/sorters, lifts, belt tiers, and hotbar drag-and-drop.
2. **Active loop:** per-entity overclock (a stat scoped to an entity), anomalies
   (seeded `Rng` events that spawn on machines and reward a click), and timed
   production contracts (a `Contract` system reading `ItemSold` events).
3. **Status effects:** a heat model on items (`heat` tag + decay), heaters,
   coolers, and quench recipes that require a tag.
4. **Compression:** "blueprint → condensed machine". Measure a sub-layout's
   steady-state input/output ratios with the headless sim and emit a generated
   `BuildingDef` (a processor with a synthesized recipe) that replaces it on a
   single footprint.
5. **Progression:** tech tree unlocking defs, plot expansion (`GridBounds`), and
   tiered content packs.
6. **Online:** record `CommandLog` + seed, submit with `StatsSnapshot`, and verify
   server-side by replaying with the same core (ASP.NET or a CLI worker).
7. **Performance, when needed:** belt segments (Factorio-style transport lines),
   struct-of-arrays for items, running the sim on a worker thread (events are
   already queued, not callbacks), and System.Text.Json source generation for
   NativeAOT/iOS.
