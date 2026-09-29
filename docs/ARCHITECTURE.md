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
│  Behaviors ─── conveyor · router · miner · processor · seller · lab  (entity state) │
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
  at its middle and skips blocked ones at the exit. An item rides from its entry edge to the
  **middle** of the hub (`RouterBehavior.Middle`, half the lane). There it chooses its output,
  and there it waits when that output cannot take it. It may not pass the middle until the hub
  knows the receiver will have room by the time the item reaches the edge
  (`TickContext.WouldPush`, `IBehavior.WouldAccept`: a belt answers from its tail and its own
  draining, a machine from its input buffer, a depot always yes), and then it rolls the rest of
  the way out at `RollSpeed` (twice the lane speed) and hands over at the edge. So an item the
  hub cannot send stands in the middle of the building instead of on the cell edge it shares
  with the belt that is refusing it, where it was drawn on that belt's own first item and read
  as "it has already left" (3.8.9; 3.8.8 got the waiting right but still handed over from the
  middle, which hopped the item half a tile in one step). The look-ahead is what keeps that
  free: waiting for room to exist *now* and then travelling out would leave the receiving belt
  idle and halve the hub's rate, and `A_splitter_keeps_up_with_a_congested_belt_it_feeds` and
  `Merger_is_a_throughput_gate_that_widens_with_levels` pin that rate. A receiver that cannot
  answer ahead (nothing built in does) is tried the old way, out at the edge.
  When every output an item may use is full the item waits at its place and blocks the belt
  behind it, and that back pressure is the only thing that holds a line back when something
  downstream cannot take any more. Merging is fair: while the preferred input has items waiting,
  other inputs are refused, then the preference rotates. Both are tested with saturated inputs.
  A splitter can also sort, with a filter per output (see Byproducts below). A hub whose lane
  still holds an item past the middle while an output keeps refusing (a save written before
  3.8.9, or a receiver that closed up mid roll) lays its lane out again from the middle back to
  the entry (`Relane`): the items come back in, nothing is dropped, and they space out again as
  the line drains.
  A hub that cannot pass something says so instead of looking busy: once an output has
  refused items for a full second (`RouterState.RefusedTicks`, `JamTicks`) the status
  names it ("Left blocked", red lamp) and the panel adds "(belt full)" to its row, or
  "(no belt)" when a filter sits on an output with nothing attached to it. An output
  the item needs that has no belt at all is reported as such ("Front has no belt")
  before any full-belt message, because a belt that is missing or facing the wrong
  way is the one cause a player cannot see; a filter mistake where no output is set
  for an item reports "nothing takes <item>". "5 item(s)"
  on a frozen hub was indistinguishable from a working one, which cost the owner a
  session on 2026-09-29: the answer to "why is my plastic not moving" was that the
  tar belt at the end of the right output had been full for hours.
- **Shared geometry.** `View/TransportPath` defines every path in building-local
  space: straight, S-curved ramps (smoothstep), quarter-circle curves, and hub
  entry→centre→exit. The simulation positions items with it, and the Godot client
  sweeps belt meshes along it, so items ride exactly on the drawn belt.
- **Items are drawn where they really are.** `ItemPoint` uses the progress it is given, with no
  offset at either end of a path. 3.8.7 tried shifting the ends inwards so that a building would
  never draw an item on the cell edge it shares with its neighbour, but a path's end and the next
  path's start are the *same* point: two shifts made every hand-over a hop (a quarter of a cell
  between belt tiles, half a cell out of a hub) against a normal step of 0.05, and squeezed every
  belt to 0.76 of its length ("the spacings ... are messed up", "we need a little bit more spacing
  between items"). The one case a shift covered, two items waiting on either side of a shared
  edge, is a still picture in a jammed line and is not worth a hop on every item that moves. A hub
  needs no shift at all now: an item it cannot send waits in the middle of the hub.
  `Items_move_in_one_even_step_all_the_way_through_a_line` walks items through a miner, two belts,
  a splitter, two more belts and a depot and fails if any single step is bigger than the exit roll
  (it measured 0.658 of a cell before 3.8.9).

### Machines

| Behavior | Role | Notes |
|---|---|---|
| `miner` | source | Work accrues at `miner.rate` × the drill's level speed. Rates above 1 cycle/tick merge into bundles up to the stack size. |
| `processor` | recipes | Buffers inputs from any port and crafts at `machine.speed` × level speed. Output value = consumed input value × `valueMultiplier` × level value. Smelter, press and assembler are the same behavior with different recipes. |
| `seller` | sink | Pays value × count × `sell.multiplier` × level value, and only 25% for items marked `raw`. |
| `conveyor` | transport | Belts, ramps and in-line effects. |
| `router` | transport | Splitters and mergers (see above). |
| `lab` | sink | Takes science packs off a belt and banks one per `interval` × level speed into `World.Science` (see "Research"). Refuses only while it holds `capacity` packs. |

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
  `SetFilter` is the same for a splitter output (`IBehavior.Filters`/`SetFilter`), and
  `IBehavior.CheckLoaded` repairs such state when a save loads (a filter for a removed item).
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
- Global stat upgrades (`upgrades` in content) raise a stat for the whole factory.
  `World.Stat(key)` composes them and caches the result. The base game's only ones are the
  research trial's (see "Research"); packs can add more, priced in money, packs or both.
- `StatsTracker` keeps lifetime totals and a rolling 60 s income window, both in total and per
  item: `EarnedByItem` is the money each item's sales paid, and per item buckets aligned with the
  total window give `IncomePerSecondOf`/`IncomePerSecondByItem` and `IncomeShareOf` (rate over the
  total rate, zero when nothing was earned). They are saved with the stats; a save from before
  them loads with no per item income. Order rewards and offline extrapolation count toward
  `TotalEarned` only, so the per item numbers are sales. `Snapshot()` is the deterministic summary
  for leaderboards and shared stats, including `EarnedByItem`.

### Byproducts

A recipe can have several outputs: one craft makes all of them. `ProcessorBehavior.Craft` splits
the craft's value evenly over **every output unit**, whatever the item, and each output leaves as
its own stack through the machine's one output port, so a byproduct recipe puts a mixed stream on
the belt. The output buffer counts the units of all outputs. `ItemValues` and the balance tool use
the same split, and an item's reference value comes from the earliest tier that makes it (then the
most valuable recipe), so a byproduct recipe must not make an existing item earlier or at a higher
value than today, or that item's value changes.

The mixed belt is sorted by the splitter. `RouterState.Filters` holds one rule per output, in the
def's output order: null takes anything, an item id takes only that item, and
`RouterBehavior.Overflow` takes only what the others will not. An item goes to the connected
outputs set to its type, else to those that take anything, else to an overflow output; within that
group the outputs take turns (`LastPicked`) and a blocked one is skipped, and an overflow output
also takes what a full group refuses. An item that no output takes waits at the centre and holds up
the splitter, and an item whose whole group is full while another output is still taking items does
the same thing: it waits and stops the splitter. That is on purpose. A byproduct output that is full
is supposed to hold the line back, exactly like a full belt anywhere else, so a factory that cannot
get rid of its tar has to build something that can. Do not give the hub a pocket to set such items
aside in without asking him first: 3.8.1 and 3.8.2 did that (`RouterState.Held`, released as a fix
for this very scenario) and he rejected it on 2026-09-29, because a full byproduct belt then stopped
nothing and the hub silently accumulated hundreds of items that were nowhere on the map. With no
filter set `Filters` is null and the old round-robin code path runs, so a plain splitter routes and
saves exactly as before. The lazy answer to a byproduct is one splitter: set the wanted item on one
output and overflow on another that runs to a depot. Note what "anything" does and does not do: it is
the default rule of an output, and an item that has an output of its own never falls back to it, so a
belt set to "anything" is not a spillway for a byproduct whose own belt is full. "overflow" is the
spillway, and the panel says which output is holding the line up when one is.

`ItemDef.Byproduct` (`"byproduct": true`) marks an item that is made on the side. It sells and
crafts like any other item, but is never asked for in an order, so dealing with it stays optional.

A machine on automatic **consumes a byproduct it is holding**: `ProcessorBehavior.PickRecipe` takes a
recipe whose inputs include a byproduct ahead of the def's recipe order, before the rule that keeps
the current recipe while its inputs last. Without that, a blast furnace fed coal and tar together
burned coal for ever (`forge_steel` is listed first), so the tar sat in its buffer, the belt feeding
it filled up, and everything sorting into that belt stopped for good: the player built the whole
chain and still got a dead factory with no hint as to why. A byproduct is there to be consumed, not
banked. The player's chosen recipe still wins (`Chosen`), and with no byproduct in the buffer the
order is exactly as before. The only cost is that a machine whose byproduct arrives slower than it
can burn it alternates between the two recipes, and a switch resets `Work`, so it loses part of a
craft each time; that is the cheap price of never banking a byproduct.

**The trial content** in `base.json` is marked "Byproduct trial" in comments: an Oil Refinery can
crack crude oil (2 crude oil → 3 plastic + 1 tar), and a Blast Furnace can burn tar instead of coal
(iron ingot + tar → steel). Both are tuned so that every unit is worth what the plain recipes make:
plastic $1, tar $1 (the coal it replaces), steel $7.5. Cracking gives less plastic per oil but works
through oil 50% faster, and its tar saves coal. A refinery only cracks when the player chooses it.
`ByproductTrialTests` covers the content; `MultiOutputTests` and `SplitterFilterTests` cover the
mechanism with their own test machines, so they stay when the content goes.

**To tune it**, edit `base.json`: counts, `ticks` and `valueMultiplier` of `crack_oil` and
`forge_steel_tar`, then check with `balance items`, `balance item tar` and `balance item plastic`.
Keep plastic at $1 and steel at $7.5 per unit, or existing items change value.

**To remove it**, delete from `base.json`: the `tar` item, the `crack_oil` and `forge_steel_tar`
recipes, those two ids from the `refinery` and `blast_furnace` recipe lists, and the two comment
blocks marked "Byproduct trial"; then delete
`tests/FactorySim.Tests/ByproductTrialTests.cs`. Nothing else refers to them. Old saves keep every
building: a machine set to a removed recipe runs automatically, a filter for tar is cleared, an order
for tar is dropped (each with a load warning), and tar already on a belt still sells at a depot.
The filters, overflow and the `byproduct` flag are general and stay.

### Research

Research sits beside the tiers and never gates them: nothing a tier needs is bought with it, so a
player can ignore it and the game plays exactly as before. It is a bank, not a queue. A lab takes
packs off a belt whether or not anything is being bought, so a research line never backs up for want
of a choice, and the player spends the bank whenever they like in the Research window.

- **Packs.** `ItemDef.Science` (`"science": true`) marks a pack. It is made, sold and belted like any
  item and is never asked for in an order (orders skip it as they skip byproducts). The trial
  recipe has `valueMultiplier: 1`, so a pack sells for exactly its parts and selling packs is never a
  reason to make them. `balance items` lists packs on their own line and never as a dead end.
- **The bank.** `World.Science` holds banked packs per item (`ScienceOf`, `CanPay`, internal
  `AddScience`/`PayScience`). It is saved as `SaveData.Science`, sorted by id, zero entries left out,
  so old saves load with an empty bank. A banked item the content no longer has is dropped with a
  warning.
- **Labs.** `LabBehavior` (`"behavior": "lab"`) accepts the items in `params.items` (empty = every
  science item), holds up to `capacity`, and studies one per `interval` ticks times its level speed,
  adding it to the bank. `LabState.Banked` counts what one lab has banked; `Held` keeps its keys, so
  the save is a pure function of history. On load, held packs of an item the lab no longer takes are
  dropped with a warning (`CheckLoaded`).
- **Prices.** `UpgradeDef.Packs` prices a global upgrade in packs, which grow like the money cost
  (`PacksForLevel`: `count × costGrowth^level`, rounded up). `Tier` keeps it closed until that tier is
  unlocked, and `Description` is the line the window shows. `BuyUpgrade` checks the tier, the money
  and the packs, then pays both; an upgrade may cost money, packs or both. `ContentRegistry` refuses
  an upgrade whose tier or pack item does not exist, or a pack count below 1. Offline catch-up does
  not add packs to the bank.
- **Client.** The Research window (`ResearchPanel`, key L, flask button in the sidebar) shows the bank
  and one card per upgrade, all read from the content. The button hides while no upgrade's tier is
  open, and for good when the content has no upgrades. `--research` opens it in the smoke run.

**The trial content** in `base.json` is marked "Research trial" in comments: `science_1` (Basic
Science Pack, an iron plate and a copper wire), the `pack_1` recipe, the Science Bench (a processor
that only makes packs), the Lab, and three upgrades from tier 1, each five levels with pack prices
that double: `research_drills` (`miner.rate` +5% a level, 20 packs first), `research_prices`
(`sell.multiplier` +5%, 20 packs) and `research_machines` (`machine.speed` +10%, 10 packs). One lab
banks a pack every 4 seconds, so all five drill levels (620 packs) take about 41 minutes. The pack
changes no other item's value or tier.

**To tune it**, edit `base.json`: `perLevel`, `maxLevel`, `costGrowth` and `packs` on the upgrades, the
lab's `interval` and `capacity`, or the pack recipe. `ResearchTrialTests` pins the current prices and
speeds, so change it with them.

**To remove it**, delete from `base.json` the seven entries (`science_1`, `pack_1`, `science_bench`,
`lab`, and the three `research_*` upgrades) with their "Research trial" comment blocks, then delete
`tests/FactorySim.Tests/ResearchTrialTests.cs`. The Research button then never shows. Old saves load
with a warning each: the labs and benches are dropped (without a refund, like any building whose def
is gone), bought levels and the bank are dropped, and every other building is kept. The lab behavior, the bank and pack prices are general and
can stay dormant, as the upgrade mechanism did before.

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
| `UI/` | `Hud` (tool bar, sidebar, factory card with the next goal, height ladder, hotbar, key hints, cursor tooltip), `HudWindow` (draggable windows, several open at once), `ManageWindow` (the selection: stats, upgrade, recipe choice with every output, splitter filters), build menu with locks and limits, progress (tiers, limits), stats, vector `IconView`, `Thumbnails` (renders building and item icons from the 3D models) |
| `Dev/` | `UiScenario`: scripted end-to-end test that injects real input events |
| `UI/TutorialPanel` | Presents the tutorial steps with a pulsing outline on the control each step is about |
| `UI/Menus` | `MenuLayer`: title screen, pause menu, save slots, name and confirm dialogs, `SettingsPanel`, credits. It keeps processing while the tree is paused |
| `UI/HudPanels` | Progress (tiers, limits, next goals), `OrdersPanel` (order cards with a swap button), statistics (with income by product, and what is made but earns nothing) |
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

1. **Logistics depth:** lifts, belt tiers, and hotbar drag-and-drop (splitter filters are done).
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
