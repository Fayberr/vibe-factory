# Roadmap: depth and long-term play

Where the game is going and why. Decisions are Fabian's; numbers are starting points that the
balance tool (`dotnet run --project src/FactorySim.Cli -- balance`) checks before they ship.

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

## Open questions

- **Belt look:** keep the slower belts (items travel 1 tile/s at level 1, packed a quarter tile
  apart) or keep the old item speed with wider gaps (2 tiles/s, half a tile apart)? Throughput
  is the same either way.
- **Polisher:** still runs at 8 items/s at level 1. Follow the new belt speeds or not?
- **Compounding polish:** processed items keep the value of their polished ingredients, so
  polishing every stage multiplies value by 1.5 per stage. Intended or not?
