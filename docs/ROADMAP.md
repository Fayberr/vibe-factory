# Roadmap: depth and long-term play

Where the game is going and why. Decisions are Fabian's; numbers are starting points that the
balance tool (step 0 below) checks before they ship.

## The problem

The game is too easy and too short: a full run fits in one night, and every new tier replaces
the one before it instead of building on it. The goal is the opposite: big, interconnected
factories where old production lines stay needed, parts feed many recipes, and later products
pull material through the whole factory.

Why it happens today:

- **Value follows the ore, not the work.** An item is worth its ingredients times a multiplier,
  and each tier's ore is worth far more than the last (iron 1, crude oil 25, gold 80, bauxite
  400). The newest ore wins, so iron is only 0 to 5% of the value of anything after tier 2.
- **Dead ends.** Crates, toys and jewelry are never used in anything else, and iron plates only
  go into crates. A finished chain is sold and then made pointless by the next tier.
- **Small recipes.** Most recipes take one or two of each input, so one advanced machine never
  needs a large base of simpler machines behind it.
- **Logistics was free.** Belts carried 8 items/s while a drill makes 1/s, and splitters and
  mergers cost $15, so nothing ever jammed and hubs could be spammed.

## Decisions

- Logistics must cost something: higher prices, **no per-copy price scaling and no caps** on any
  building. Machines keep their current prices.
- Mergers are a real throughput gate: items wait their turn when too much arrives.
- No target playtime and no artificial slowdown. The game is **open-ended**: no fixed end goal,
  longevity comes from more content and more complexity over time.
- No prestige or rebirth loop.
- Reference for the feel: a Roblox factory game where a go-kart needs motors, wheels and a frame,
  the frame needs steel and another metal, motors need batteries, batteries need electronics.

## Steps

### 0. Balance tool (done)

A calculator over the content file, part of the headless CLI. Every later change is checked
against it instead of guessed:

- `balance tiers` estimates how long each tier takes with the best factory its build limits
  allow (a lower bound: no ramp-up, belt travel, orders or milestone rewards).
- `balance items` lists value, sale price, where it is made, what uses it, and which ores its
  value comes from.
- `balance item <name> [rate]` breaks down one production line: machines per step, ores per
  second, belt load, build cost, payback time.
- Options: `--level N`, `--polish none|products|all`, `--tier N`, `--pack extra.json`.

`src/FactorySim.Core/Balance/` holds it (recipe book, production chains, tier pacing); the
numbers are tested against the simulation, not just the content file.

### 1. Logistics (done)

- Prices: conveyor $10, ramps $25, splitter and merger $200. Starting money $250.
- Belts: 4 items/s at level 1, 20/s at level 9. Splitters match belts. Mergers run a quarter
  faster (5/s at level 1) and reach the 20/s cap at level 7.
- Everything is in `base.json`, including `startingMoney`.

### 2. Value from processing

Give all ores roughly the same low value, so an item is worth more the more work went into it.
New tiers then add new products on top of the old chains instead of replacing them.

### 3. Connected recipe tree

- Shared intermediate parts that feed many recipes (gears, screws, rods, frames, cables,
  batteries, circuit boards and so on).
- Later products need several different parts in larger amounts, so one of them pulls iron
  plates and copper wire through the whole factory.
- Today's dead ends become ingredients (crates as packaging, toys and jewelry as parts).
- Tiers unlock by delivering specific items, not only by lifetime earnings.

Steps 2 and 3 break existing saves.

### 4. More content

More products and tiers on top of that tree. Adding a tier or product should stay a data change
in `base.json`, with no code changes. Ideas not decided yet: ore deposits placed on the map by
seed, alternative recipes, end-game megaprojects.

### 5. Polish and belt look (done)

Two follow-ups from looking at the game after step 1:

- **Items overlapped their belt slot.** A belt tile holds four slots (a quarter tile each), but
  planks (0.30), logs (0.28), glass (0.24), ingots (0.32) and motors (0.26) were longer than
  that, so a busy belt read as one continuous ribbon. Every item mesh now fits its slot with a
  visible gap (nothing above 0.18). Pure art: no speed, spacing or throughput changed.
- **Polishing compounded.** A polisher multiplied the value of what passed it, and a machine
  took that value over, so a polisher at every stage multiplied value by 1.5 each time (a
  three-step chain earned 3.4× the plain one). Decided against: a bonus is now paid when the
  item is sold and nowhere else, so it never carries into the next product. Polishing an
  ingredient does nothing; the polisher goes at the end of the line. The tool keeps the
  compounding variant as `--polish all`, a what-if for comparing balance changes.

### 6. Depots on the map edge (done)

Market depots and export terminals sit on the boundary of the map, with their one input facing
inward, so goods leave at the rim and the belts that reach it become the map's arteries. It is a
content flag (`"placement": "mapEdge"` on the building), checked when something is placed, turned
or moved, and deliberately not checked when a save is loaded: a depot the player already paid for
is never thrown away because a rule changed. Sandbox (free building) ignores it.

The example factory was rebuilt to match: each of its three lines now ends with a belt run to the
rim, turning south into a depot that faces off the map. A test builds the example with the rule
on, so the reference factory cannot drift into being illegal.

### 7. The map is a grid of plots you buy (done)

The map is a fixed 5 x 5 grid of 15 x 15 plots. Everyone starts with the plot at the bottom middle
and buys the rest, one plot at a time, only where it shares an edge with land they own. Price
grows with distance from the start plot (not with how many are owned), so the three neighbours
cost the same and the far corners are an endgame goal. The outer rim of the whole map is where
depots and export terminals work, which replaced the earlier plot that grew with every tier.
Tiers still unlock buildings and raise build limits, but no longer change the size of the world.

`balance land` shows the price rings against income. Saves from before the land are migrated
without losing a building; see the Land section of the architecture notes.

## Open questions

- Which of steps 2 to 4 to start.
