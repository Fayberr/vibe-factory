# Research and laboratories: plan

**Status (4.4.0): slices 1 and 2 are built as a trial.** Slice 1 is section 8. Slice 2 adds the
written-up `science_2` recipe from section 6 and three higher continuations of the same flat bonuses.
The Lab and Research window remain fully data-driven. How to tune or remove either slice: "Research"
in `docs/ARCHITECTURE.md`. The rest of this document is still a plan.

Planning only beyond slice 2, and nothing in this document is decided beyond what the
owner already decided: **research is paid in items the player manufactures**, science packs made in
the factory and eaten by a lab, never in money. Everything else (how big a tree, whether it
branches, how it relates to tiers) is a proposal with a recommendation.

Related: `docs/IDEAS.md` entries `I1` (science packs and labs), `I2` and `D1` (a graph or branching
tree), `R1` to `R11`, `F8`, `F9`; `docs/ROADMAP.md`, "Design principles: deep but optional".

---

## 1. What the existing plumbing already gives us

### The global upgrade mechanism

| Piece | Where | What it does today |
|---|---|---|
| `UpgradeDef` | `src/FactorySim.Core/Content/Definitions.cs` | `Id`, `Name`, `Stat` (a stat key), `Effect` (`Multiply` or `Add`), `PerLevel` (default 1.1), `BaseCost` (money, default 100), `CostGrowth` (default 1.5), `MaxLevel` (null = uncapped). |
| `UpgradeDef.CostForLevel(n)` | same file | `BaseCost × CostGrowth^n`, in dollars (`BigNum`). |
| `StatIds` | same file | The five keys behaviors read: `conveyor.speed`, `miner.rate`, `machine.speed`, `sell.multiplier`, `logistics.stackSize`. All default to 1. |
| `World.Stat(key)` | `src/FactorySim.Core/World/World.cs` | `1 × (product of PerLevel^level over Multiply upgrades) + (sum of PerLevel × level over Add upgrades)`, cached, cache cleared by `SetUpgradeLevel`. Deterministic integer power, no `Math.Pow`. |
| `BuyUpgrade(UpgradeId)` | `Simulation/Commands.cs`, handled in `Simulation.Buy` (`Simulation.cs`) | Checks the id and `MaxLevel`, charges `CostForLevel(level)` in money (free in sandbox), raises the level, emits `UpgradePurchased`. |
| Saving | `Persistence/SaveSystem.cs` | `SaveData.Upgrades` (id to level) is written and restored; an id the content no longer has is **dropped with a warning**, not an error. |
| Content packs | `ContentRegistry.Build` | `upgrades` from later packs override earlier ones by id, so a mod pack can add or retune upgrades. |
| Validation | `ContentRegistry.Validate` | Only two checks: `Stat` is not empty, `CostGrowth ≥ 1`. |
| Readers | the behaviors | `MinerBehavior` (`miner.rate`), `ProcessorBehavior` (`machine.speed`, every processor alike), `SellerBehavior` (`sell.multiplier`), `ConveyorBehavior` and `RouterBehavior` (`conveyor.speed`), `TickContext.MaxStackSize` (`logistics.stackSize`). |

`base.json` has `"upgrades": []`. The only upgrades anywhere are three test fixtures in
`tests/FactorySim.Tests/TestUtil.cs` (`belt_speed`, `miner_rate`, `sell_price`), exercised by
`MachineAndEconomyTests`. The Godot client has **no** caller of `BuyUpgrade` and no listener for
`UpgradePurchased` (the `Upgrade` in `BuildController` and `ManageWindow` is per-building levels, a
different mechanism).

### What an `UpgradeDef` can already express

- A permanent, global, numeric bonus on one of the five stats, bought in levels with a geometric
  price and an optional cap. Saved, loaded, overridable by a pack, free in sandbox.
- Several upgrades on the same stat compose (all multiplies multiply, all adds add).

### What it cannot express today

1. **A price in items.** `BaseCost` is money only. This is the one change the decided direction
   needs no matter which option is picked.
2. **Any gate.** No tier requirement and no prerequisite: every upgrade is buyable from the first
   second, if affordable.
3. **Unlocking anything.** Buildings are gated only by `BuildingDef.Tier`, recipes only by which
   building owns them. An upgrade cannot unlock a building, a recipe or a limit.
4. **Scope.** Stats are global. `machine.speed` speeds up every processor at once; there is no
   "smelters only". A new key such as `smelter.speed` passes validation and silently does nothing,
   because nothing reads it.
5. **Text.** No description and no display of the effect ("Drills +5%"); the UI would have to
   derive it from `Stat`, `Effect` and `PerLevel`.
6. **Undo or refund.** Upgrades are deliberately outside `EditHistory`.
7. **Visibility to the balance tool.** `Balance/RecipeBook` never reads `World.Stat`, so
   `balance tiers` cannot show what research does to pacing. Section 6 works around this by
   emulating a stat with an overlay pack.

Two quirks worth knowing before content uses it:

- `Add` sits beside the product, not inside it: `1.1² × 1 + 0.5` is `1.71`, not `1.1² × 1.5`.
  Fine for `logistics.stackSize` (the only natural `Add` stat), surprising elsewhere.
- `conveyor.speed` is clamped by `ConveyorBehavior.EffectiveSpeed` at the belt's `spacing`, the same
  cap level 9 belts already reach (4/s at level 1, 20/s at level 9, `ROADMAP.md` step 1). A global
  belt speed research is worth nothing on maxed belts, so it is a weak research reward.

### Other pieces research would lean on

- **Tiers** (`TierDef`): `Cost`, `RequiredEarnings`, and `Deliver` (lifetime *sales* of items, read
  from `StatsTracker.Sold` by `DeliveriesMet`). `ContentRegistry.ValidateTiers` refuses a delivery
  of something that cannot be made before the tier. `UnlockTier` is the command.
- **Milestones** (`MilestoneDef`, kinds `earned`, `sold`, `produced`, `built`, `level`,
  `contracts`, `tier`), rewarded in money by `Progression/Goals.cs`.
- **Stats** (`StatsTracker`): lifetime `Sold`, `Produced`, `EarnedByItem`, and per item 60 s income
  windows (`IncomePerSecondOf`, `IncomeShareOf`), shown in the statistics panel. `Produced` is
  exactly what research by doing (`R6`) would read.
- **Processor** behavior: any building JSON can craft packs with no code. Its recipe must have an
  output (`Validate` refuses an empty one), so a processor cannot be the lab itself.
- **Seller** behavior: the nearest existing thing to a lab. It already takes items off a belt and
  turns them into a counter (money); a lab is the same shape with a different counter.
- **Balance tool** (`src/FactorySim.Core/Balance/`, CLI `balance`): prices a chain
  (`ProductionChain`), and paces tiers (`TierPacing`) as the best factory the **extractor limits**
  allow. That last point matters for research: pacing is bound by raw ore supply, so a pack line
  competes with sold products for ore, and a `miner.rate` bonus raises income almost one to one.

### Things that would trip over a pack item today

- `RecipeTreeTests.Only_the_final_product_is_a_dead_end` expects `satellite` to be the only item no
  recipe uses. A pack is used by a lab, not a recipe, so it is a new dead end.
- `Goals.cs` offers contracts for any non-raw item of an unlocked tier with a value. Packs would show
  up as orders unless excluded.
- `TierPacing.BestFactory` will sell a pack if it ever pays best. With `valueMultiplier: 1` it never
  does (verified in section 6: pacing is unchanged with the packs added). (True of the greedy picker
  of the time. The exact optimiser of 4.3.0 sold packs wherever depots were the limit, so packs now
  sell for half their parts.)

---

## 2. The options

Common to all three: packs are ordinary items with ordinary recipes, crafted by an ordinary
processor building (JSON only), and a **Lab** is a new small behavior that consumes packs.

### Option A, lightest: one lab, a flat list of upgrades

**Content.** Three pack items (one per era: tier 1, tier 2 and tier 3 ingredients, see section 6),
one crafting building for them, one Lab building, and 6 to 10 `upgrades` whose price is packs. No
prerequisites. Each upgrade may say from which tier it is shown.

**Core.**
- `UpgradeDef` gains `Packs` (`ItemAmount[]`, the price of level 1, scaled by `CostGrowth` per level
  and rounded up), `Tier` (hidden before that tier) and `Description`. `BaseCost` may be 0.
- `World.Science`: a bank of packs per item id, saved in `SaveData` (unknown ids dropped on load,
  like upgrades).
- `LabBehavior`: a sink modelled on `SellerBehavior`. It accepts only the items listed in its params,
  takes one pack per `interval`, and adds it to the bank. Status "waiting for packs" when starved.
  It never blocks: a lab always accepts packs, whatever is being researched.
- `Simulation.Buy`: checks and removes packs from the bank as it checks money.
- `ItemDef.Science` (bool): keeps packs out of contracts and lets the dead-end test accept them.
- Validation: pack items exist; warn on a `Stat` no behavior reads.

**Client.** A Research window (`HudWindow`, list like `OrdersPanel`): each upgrade with its effect
in words, level, price as pack icons with counts, and a Buy button; the bank at the top. A lab model
in `ModelFactory` (the depot model with an accent will do). A toast on `UpgradePurchased`.

**Minute to minute.** Build a small line that makes basic packs from plates and wire, belt it into a
lab, watch the bank count up, open Research and spend it on "Drills +5%". Later tiers add a second
and third pack, and the better upgrades want them. It plays like a shop whose currency you
manufacture.

**Cost.** Core about half a day (one behavior, three fields, one bank, tests). Client about half a
day. Content and balance an hour or two. **Small to medium.**

### Option B, middle: A plus a small data-driven tree with side branches

Everything in A, plus:

**Content.** `requires` on upgrades (ids, optionally a level), and a second kind of research that
**unlocks content** instead of raising a stat: an optional side branch, for example bulk recipes
(`B11`), an alternative recipe (`B5`), a Mk2 drill (`M7`), or the byproduct sink (`B4`). A branch is
a content edit: a few research entries plus the recipes and buildings they unlock. The linear tiers
stay exactly as they are (the "middle option" of `I2`).

**Core.**
- `UpgradeDef.Requires` and `UpgradeDef.Unlocks` (building and recipe ids). Validation: no cycles,
  no unknown ids, a prerequisite shown no later than what needs it.
- A second gate on buildings and recipes: "tier reached **and** research done". It has to be honoured
  by placement, paste, blueprints, `BuildPlanner`, the processor's automatic recipe choice and
  `SelectRecipe`, `ItemValues` (the tier an item becomes available), `ValidateTiers`, contracts and
  the balance tool's `RecipeBook.Available`. That list is the real cost of B.
- Loading a save whose buildings are gated by research the save does not have: keep them (the same rule
  `ROADMAP.md` step 6 applies to depots: something the player paid for is never thrown away because
  a rule changed).

**Client.** The Research window becomes a small tree: one column per tier, entries as cards, lines
for prerequisites, locked cards greyed with the reason. The build menu shows "needs research X" the
way it shows tier locks today.

**Minute to minute.** As A, plus choices that change the factory: "do I research bulk recipes for
my steel line, or the Mk2 drill first". Two runs differ in the order the branches come.

**Cost.** Core one to two days, mostly the second gate threaded through every place above. Client
one to two days for the tree view. **Medium to large.**

### Option C, heaviest: a full branching graph

Everything in B, plus mutually exclusive branches (`R7`, `excludes`), possibly a graph generated per
seed (`R8`), and the graph **replacing** the linear tiers as the way new buildings unlock.

**Core.** Tier gates rewritten in terms of the graph (`BuildingDef.Tier`, `BuildLimit.At`,
`ValidateTiers`, `TierPacing`, milestones of kind `tier`, contracts weighting by tier all assume a
line). Save migration from tiers to graph state. A seeded graph generator that must stay solvable,
plus tests that every seed can reach the satellite. The balance tool has to pace paths, not a line.

**Client.** A pannable graph view, exclusive choices with confirmation, a way to see what a branch
locks out, a progress panel rebuilt around it.

**Minute to minute.** Every run is a route through the graph; research is the main thing the player
steers.

**Cost.** One to two weeks, touches most of the progression code and the tutorial. **Extra large.**

**What rules it out for now.** `ROADMAP.md` lists "a full tech graph" under "cut against it, so
soften or gate", and `IDEAS.md` says not to build `I2` or `D1` without asking. Exclusive branches
punish experimenting, which is what a casual player does most (the same reason retooling costs are
listed against the principles). A per-seed graph makes the balance tool's job combinatorial. It is
also the most expensive to remove (section 5). Keep it as the far end of the direction, not a plan.

---

## 3. How each option sits with the existing tiers

Three possible relations:

- **Beside.** Tiers unchanged. Research is a second axis: stat bonuses (A) and optional side content
  (B). Nobody needs a lab to reach the satellite.
- **Feeding.** Research makes tiers come sooner, but never blocks them. Stat bonuses already do
  this: section 6 shows a +10% drill rate makes tier 3 earn 10% more and the full run 3 hours
  shorter. A stronger form would be research that lowers a tier's price or earnings target.
- **Replacing part.** Packs become part of the tier gate (for example a tier's `Deliver` asks for
  packs consumed by labs), or the graph replaces tiers altogether. This is the literal reading of
  `I1` ("every tier ends with build the next science chain").

| | Beside | Feeding | Replacing part |
|---|---|---|---|
| A | natural fit | natural fit, via stat bonuses | possible (a pack in `Deliver`) but forces labs on everyone |
| B | natural fit | natural fit | possible for side branches only |
| C | not meaningful | not meaningful | this is what C is |

**Recommendation: beside, and feeding through its effects. Do not replace any part of the tier gate,
at least not now.**

Why:

1. Principle 2 ("depth pays off, it never gates") rules out a tier that cannot open without a lab.
   A casual player who never builds one must still reach the last tier, and today they do.
2. The tiers already demand "run the chain before this" through `Deliver`. A pack delivery would
   duplicate that gate, not add a new idea.
3. Feeding comes for free: pacing is bound by extractor supply, so a drill rate research turns
   directly into earlier tiers. Depth is rewarded in exactly the currency the roadmap names, "earning
   more per minute and getting there faster".
4. It keeps the feature deletable (section 5): if research sits beside the tiers, removing it leaves
   the tier game untouched.

The one reason to reconsider: if the owner wants `I1`'s original shape, where the *main* progression
is building science lines. That is a question (section 7, question 1), not something to assume.

---

## 4. How it stays optional

The lazy default for every option: **never build a lab, and the game is exactly today's game.**

- **Option A.** Nothing requires a pack. Tiers, orders, milestones and land ignore research. The
  first pack is two parts the player already makes at tier 1 (a plate and a wire), so trying it is
  one building and a belt. Labs never jam a line: a lab always takes packs into the bank whether or
  not anything is being researched, so there is no "pick a research or your belt backs up" failure
  (principle 4). The deep player is rewarded by picking the upgrades that move income (drill rate,
  sale prices) and by sizing pack lines against the ore they steal (section 6), using the income by
  product panel (`F8`) to see the trade. Keep the total bonus bounded with `maxLevel`, so research
  is an advantage, never a requirement to keep up.
- **Option B.** As A, and every unlock is a side branch: something extra, never the only way to make
  a tier's product (principle 7). The default path, tier by tier, contains no research-gated
  building. A test can enforce that: `TierPacing` without any research must still reach the last
  tier.
- **Option C.** Hard to keep optional. If the graph replaces tiers, the lab is the progression, and
  the only lazy default is an "auto-research" mode that picks for you, which is a second system to
  build and balance. Exclusive branches also make a wrong choice possible, which is the "punished"
  outcome principle 4 wants to avoid.

Teaching (principle 5): the tutorial stays as it is; a hint card appears when tier 1 opens ("Labs
turn packs into permanent bonuses"), and the Research window says what each pack is made of, with a
link to the chain helper (`F9`) once that exists.

---

## 5. How easy each option is to tune and to remove

**Option A: cheap to tune, cheap to remove.**
- Tune: every number (pack recipes, lab speed, prices in packs, growth, effect per level, caps) is in
  `base.json`. A pack overlay can try a retune with the balance tool without touching the file.
- Remove, content only: delete the pack items, their recipes, the two buildings and the upgrades
  from `base.json`. The save loader already drops unknown upgrades and unknown buildings with a
  warning, and the bank would drop unknown ids the same way, so old saves load. The game is then
  identical to today.
- Remove, code too: one behavior, three fields on `UpgradeDef`, one bank, one window. They can also
  just stay dormant, as the upgrade mechanism has for the whole project so far.

**Option B: cheap to tune, moderate to remove.**
- Tune: same as A, and a branch is a content edit.
- Remove: research-gated buildings and recipes have to be deleted or moved back onto a tier. Saves
  keep any gated building already built (the loader never drops a paid building), so removal is
  safe, but the second gate is threaded through placement, blueprints, planner, recipe choice, item
  values, contracts and balance, so taking the code out is a real change, not one file.

**Option C: expensive to tune, expensive to remove.**
- Tune: every change interacts with every path; per-seed graphs multiply that. The balance tool
  would need a new pacing model before anything could be checked.
- Remove: undoing a replaced tier system means restoring the tier gate, migrating saves back, and
  redoing progression balance. That is the definition of hard to delete.

---

## 6. Real numbers from the balance tool

All commands were run in this worktree at commit `94e8527`, with the CLI built once in Release
(`dotnet build src/FactorySim.Cli -c Release`). The hypothetical packs are **not** in `base.json`;
they live in a throwaway overlay loaded through the pack option, so the numbers are exactly what the
shipping tool computes for that content.

Each block below shows only the arguments. The full command is `dotnet run --no-build -c Release
--project src/FactorySim.Cli` plus the line shown, separated by `--`.

### The overlay: three plausible packs

`/tmp/s87/research-packs.json`, abridged (items have `baseValue: 0`, like every non-ore item):

```json
"recipes": [
  { "id": "pack_1",  "inputs": [ {"item":"iron_plate","count":1}, {"item":"copper_wire","count":1} ],
    "outputs": [ {"item":"science_1","count":1} ], "ticks": 40, "valueMultiplier": 1 },
  { "id": "pack_2",  "inputs": [ {"item":"steel","count":1}, {"item":"gear","count":1}, {"item":"screw","count":2} ],
    "outputs": [ {"item":"science_2","count":1} ], "ticks": 60, "valueMultiplier": 1 },
  { "id": "pack_3",  "inputs": [ {"item":"cable","count":1}, {"item":"gear","count":1}, {"item":"glass","count":1} ],
    "outputs": [ {"item":"science_3","count":1} ], "ticks": 80, "valueMultiplier": 1 },
  { "id": "pack_3h", "inputs": [ {"item":"frame","count":1}, {"item":"cable","count":1} ],
    "outputs": [ {"item":"science_3h","count":1} ], "ticks": 80, "valueMultiplier": 1 }
],
"buildings": [ { "id": "science_bench", "behavior": "processor", "cost": 500, "tier": 1,
                 "params": { "recipes": [ "pack_1", "pack_2", "pack_3", "pack_3h" ], "inputCapacity": 10 } } ]
```

`science_3` is a light tier 3 pack (cable, gear, glass); `science_3h` a heavy one (frame, cable).
`valueMultiplier: 1` means a pack is worth its parts and adds nothing when sold. (Shipped with 0.5 for
two parts instead: at 1, two parts' worth in one unit sells better wherever depots are the limit.)

### Baseline, and packs do not disturb it

```
$ balance tiers
Tier  Name            Income/s     Setup  Next in    Total  Next tier asks for                     Sells
0     Basics                $8      $810   2m 12s   2m 12s  -                                      Iron Ingot 100%
1     Workshop             $42    $3.27K   3m 33s   5m 45s  150 Iron Plate, 150 Copper Wire (38s)  Iron Plate 86%, Copper Ingot 14%
2     Industry          $189.8   $32.24K  12m 18s   18m 3s  150 Steel Beam, 60 Crate (1m 0s)       Crate 65%, Gear 15%, Copper Wire 11%, +2
3     Petrochemicals    $883.5   $75.08K  34m 54s  52m 57s  80 Toy, 60 Frame (1m 21s)              Toy 75%, Crate 17%, Glass 5%, +2
4     Electronics       $4.50K    $2.86M   1h 43m   2h 36m  60 Motor, 40 Jewelry (1m 20s)          Motor 96%, Circuit Board 1.7%, Jewelry 1.3%, +4
5     Robotics         $18.41K    $9.23M   3h 19m   5h 55m  40 Robot (59s)                         Robot 99%, Jewelry 0.6%, Circuit Board 0.4%, +4
6     Aerospace        $34.67K  $175.01M    1d 6h   1d 11h  30 Drone (2m 0s)                       Drone 62%, Robot 36%, Circuit Board 0.8%, +5
7     Space            $93.84K    $5.78B        -   1d 11h  -                                      Satellite 93%, Robot 7.1%, Jewelry 0.1%, +5
```

(Idle raw column trimmed.) `balance tiers --pack /tmp/s87/research-packs.json` prints the same
table row for row: the packs are never worth selling, so adding them changes no pacing.

### What a tier 3 pack costs to make

```
$ balance item science_3 --pack /tmp/s87/research-packs.json --tier 3
Petrochemical Science Pack        1  Science Bench      4        4   25%
Glass                             1  Glassworks       1.2        2   25%
Sand                              2  Sand Quarry      1.6   2 of 5   50%
Gear                              1  Machine Shop     1.2        2   25%
Iron Plate                        2  Press            1.6        2   50%
Iron Ingot                        2  Smelter          1.6        2   50%
Iron Ore                          2  Iron Drill         2  2 of 10   50%
Cable                             1  Fabricator       1.5        2   25%
Plastic                           1  Oil Refinery    0.75        1   25%
Crude Oil                       0.5  Oil Pump           1   1 of 2   13%
Copper Wire                       2  Press            0.8        1   50%
Copper Ingot                      1  Smelter          0.8        1   25%
Copper Ore                        1  Copper Drill     1.5   2 of 7   25%
Worth $35 each, sells for $52.5 at Market Depot: $52.5/s.
Build cost: machines $49.34K, with belts and depots $50.59K. Pays back in 16m 4s.
Value from ores: Iron Ore 46%, Copper Ore 30%, Sand 17%, Crude Oil 7.4%.
```

```
$ balance item science_3 0.25 --pack /tmp/s87/research-packs.json --tier 3
Petrochemical Science Pack     0.25  Science Bench      1        1  6.3%
...
Crude Oil                      0.13  Oil Pump        0.25   1 of 2  3.1%
...
Build cost: machines $29.82K, with belts and depots $30.53K. Pays back in 38m 46s.
```

The heavy variant, same command with `science_3h`: worth $81.9, **4.67 iron ore/s (5 of 10 drills)
and 2 coal/s (3 of 5) per pack/s**, iron rods at 133% of a belt, $101.25K to build for 1/s.

The other two eras, for scale: a `science_1` line at 0.25 per second costs $1.62K to build (half the
$3.27K tier 1 factory), 1 of 6 iron drills and 1 of 3 copper drills; `science_2` is worth $25.3 and
needs 3.33 iron ore/s per pack/s.

### Packs per minute, and whether that is a real constraint

The dollar value of a pack is misleading. At 0.25/s a light tier 3 pack is worth $8.75/s, 1% of the
tier's $883.5/s. But tier pacing is bound by extractor limits, and at tier 3 **crude oil is the
binding ore**: the toy is 75% of the tier's income, and a toy line at 1/s needs 1 crude/s, which is both
oil pumps the tier allows (`balance item toy --tier 3`: "Crude Oil 1, Oil Pump 2 of 2"). Every pack's cable takes oil
from toys.

To measure that, the lab's appetite was modelled as lost extractor supply: overlay packs that
override the extractors' `interval` so they deliver what is left after the lab has taken its share.
Only the **tier 3 row** of these runs is meaningful (the override applies at every tier), and
intervals are whole ticks, so the drains are rounded:

| Lab eats (light tier 3 packs) | Oil pump | Iron drill | Copper drill | Sand quarry |
|---|---|---|---|---|
| 0.25/s = 15/min | 40 → 46 (−13%) | 20 → 21 (−5%) | 30 → 32 (−6%) | 16 → 17 (−6%) |
| 1/s = 60/min | 40 → 80 (−50%) | 20 → 25 (−20%) | 30 → 38 (−21%) | 16 → 24 (−33%) |

```
$ balance tiers --pack /tmp/s87/drain-light-0.25.json
3     Petrochemicals    $812.1   $76.97K  37m 58s  56m 38s  80 Toy, 60 Frame (1m 32s)   Toy 71%, Crate 20%, Glass 5.2%, +2
$ balance tiers --pack /tmp/s87/drain-light-1.json
3     Petrochemicals      $421  $104.86K   1h 13m   1h 34m  80 Toy, 60 Frame (2m 40s)   Frame 84%, Glass 8.9%, Copper Wire 5.4%, +1
```

| Lab rate | Tier 3 income | Tier 3 lasts |
|---|---|---|
| none (baseline) | $883.5/s | 34m 54s |
| 15 packs/min | $812.1/s (−8%) | 37m 58s |
| 60 packs/min | $421/s (−52%) | 1h 13m |

**Verdict: at 15 packs a minute the demand is a genuine but soft constraint** (it costs 8% of income
and a $30K line, 40% of the tier's $75K factory); at 60 a minute it halves the tier and would make
research a trap for the casual player. So a lab should eat about **one pack every 4 seconds (15/min)**,
and a tier 3 research should cost a few hundred packs: 300 packs is 20 minutes of one lab, a little
more than half the tier. A deep player runs two labs and a bigger pack line; a casual one runs one
lab or none.

### What research is worth back

The tool does not read global stats (section 1), so a +10% `miner.rate` research was emulated the
same way, every extractor's interval divided by 1.1 and rounded (iron 20 → 18, copper 30 → 27, logs
24 → 22, coal 24 → 22, sand 16 → 15, oil, gold and bauxite 40 → 36):

```
$ balance tiers --pack /tmp/s87/miner-rate-plus10.json
3     Petrochemicals    $972.6   $87.90K  31m 42s  48m 52s  80 Toy, 60 Frame (1m 13s)   Toy 76%, Crate 17%, Glass 4.3%, +1
7     Space           $104.25K    $7.81B        -    1d 8h  -                           Satellite 93%, Robot 7.1%, Jewelry 0.1%, +5
```

Tier 3 income +10.1% ($883.5 → $972.6), and the full run from 1 day 11 hours to 1 day 8 hours. A
drill-rate research pays back almost one to one, so its price and cap need care; `machine.speed`
barely moves pacing (machine count is unlimited, so it only saves build cost), and `conveyor.speed`
is capped by belt spacing. The strong rewards are `miner.rate` and `sell.multiplier`.

Before anything ships, the tool should learn this directly: a research option that applies upgrade
levels to `RecipeBook` (drill rate, sale multiplier, belt speed), and a `balance research` report
listing each research's price in packs, in lab minutes and in ore.

---

## 7. Open questions for the owner

Only the ones that change the design.

1. **Beside the tiers, or part of the gate?** The recommendation is beside (no tier ever needs a
   lab). `I1` as written says "every tier ends with build the next science chain", which would make
   labs mandatory. Which one is meant?
2. **What does research buy?** Only permanent bonuses (Option A), or also optional content such as
   bulk and alternative recipes or Mk2 buildings (Option B)? Content unlocks feel more like
   progression; bonuses are far cheaper to build and to remove.
3. **A bank or an active research?** Recommended: labs bank packs and you spend the bank (never
   blocks a belt). The alternative is Factorio's: pick one research, labs work on it, a progress bar
   fills; more "watching the line build it", but a lab with nothing selected backs up its belt.
4. **Can packs be sold?** Recommended: yes at their parts' value (as in the overlay, so selling is
   never a reason to make them), but never offered as orders.
5. **Global or per building?** Global stats exist today. "Smelters +10%" needs scoped stat keys
   read by the behaviors, a small but real core change.
6. **How strong in total?** A +10% drill rate saves 3 hours of a 35 hour run. Is a cap of roughly
   +25 to +50% across all research the right order, or should research be a bigger lever?

---

## 8. Recommended smallest first slice

**"Research slice 1: basic science pack, one lab, three bonuses."** Option A with one pack, built so
the whole slice is content plus one small behavior, and removable by deleting its content entries.

Content (`base.json`):
- Item `science_1`, Basic Science Pack, `science: true`.
- Recipe `pack_1`: 1 iron plate + 1 copper wire → 1 pack, 40 ticks, `valueMultiplier: 1`.
- Building `science_bench` (processor, tier 1, $500) with that recipe.
- Building `lab` (new `lab` behavior, tier 1, about $500): accepts `science_1`, one pack per 4 s.
- Three upgrades priced in `science_1`, shown from tier 1:
  - Drill output, `miner.rate` ×1.05 per level, max 5, 20 packs growing ×2 (20, 40, 80, 160, 320).
  - Market prices, `sell.multiplier` ×1.05 per level, max 5, same price curve.
  - Machine speed, `machine.speed` ×1.1 per level, max 5, 10 packs growing ×2.
  All five drill levels (620 packs) take one lab about 41 minutes, which lands in tier 2 to 3 for a
  player who starts at tier 1.

Core:
- `UpgradeDef.Packs`, `Tier`, `Description`; `ItemDef.Science`.
- `World.Science` bank, saved, unknown ids dropped on load.
- `LabBehavior` (sink, never blocks), registered in `BehaviorRegistry`.
- `Simulation.Buy` pays packs; contracts skip `science` items; the dead-end test accepts them.
- Tests: a lab banks packs from a belt; buying spends packs and raises the stat; save round trip;
  a save with a bank and upgrade levels loads cleanly into content without research.

Client:
- A Research window with the bank, the three upgrades and Buy buttons, opened from the HUD.
- A lab model (reuse the depot model with an accent) and a toast on purchase.

Not in the slice: tier 2 and 3 packs, prerequisites, unlocks, the research option in the balance tool.

Try it by playing one run to tier 3 with and without a lab. Keep it if building the pack line feels
like progress. At the time, its seven content entries were the whole removable trial; section 9 now
adds the independently removable second slice.

## 9. Second slice shipped in 4.4.0

The next smallest increment keeps option A and adds one higher pack, without prerequisites or a
technology graph:

- `science_2`, Advanced Science Pack, opens at Industry.
- `pack_2`: one steel beam, one gear and two screws, 60 ticks in the existing Science Bench.
- Three advanced upgrades add three more levels of drill output, market prices and machine speed.
  They use the same effects and doubling price curve as slice 1, paid only in Advanced Science Packs.
- The recipe uses `valueMultiplier: 0.5`, so the pack sells for half its parts and never beats its
  best ingredient at a depot. Baseline tier pacing is unchanged.

The strong bonuses top out at about +48% across both slices, inside section 7's proposed +25% to
+50% range. Remove the slice by deleting the five marked entries, removing `pack_2` from the Science
Bench and deleting `ResearchSlice2Tests.cs`; slice 1 then behaves exactly as it did before.
