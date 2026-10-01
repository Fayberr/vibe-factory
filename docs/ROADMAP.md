# Roadmap: depth and long-term play

Where the game is going and why. Numbers are starting points that the balance tool (step 0 below)
checks before they ship.

## The problem

The game was too easy and too short: a full run fitted in one night, and every new tier replaced
the one before it instead of building on it. The goal is the opposite: big, interconnected
factories where old production lines stay needed, parts feed many recipes, and later products
pull material through the whole factory.

Why it happened (steps 2 and 3 fixed the first two):

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

## Design principles: deep but optional

The goal: a player should be able to go really deep into the game and really have to
think, but should never *have* to. You can also just play it for fun, on the side. The deep end is
crazy interconnected factories; the shallow end is a relaxing game you can leave running. Next to
that: more content, more versatility, more things to do.

So every system has to work on two levels, and the following rules follow from that:

1. **Shallow floor, deep ceiling.** Every mechanic needs a lazy default that just works. One machine
   instead of a balanced sub-factory, a single input instead of a perfect ratio, and selling the
   excess instead of a perfect loop. The advanced version is available, never mandatory.
2. **Depth pays off, it never gates.** Playing deep means earning more per minute and getting there
   faster, not being the only way to progress. A casual factory still reaches the last tier, just
   later.
3. **Additive, not invalidating.** A new mechanic must not make simple play wrong. A fluid network
   must work without perfect ratios, and an unwanted byproduct must be removable with one cheap
   building. The system is there for everyone; only the mastery is opt in.
4. **Soft costs, few hard fail states.** Prefer slower, cheaper, uglier over stopped, dead or
   punished. Anything that can halt a factory (a brownout, a meltdown, waste fines) is either gentle
   or opt in behind a mode.
5. **Teach in layers.** The route into the deep end is discoverable: tutorial, hints, and the tools
   below. Nothing important should only be learnable from a wiki.
6. **Tools instead of math.** The analysis features (income by product, bottleneck ranking, history
   graphs, chain ratios) are what make depth optional: a casual player sees what to fix without
   calculating anything, and an expert uses the same numbers to optimise. This makes the feedback
   ideas structural, not polish.
7. **Complexity is opt in.** Side branches, optional products, optional logistics and optional
   challenges. The default path stays clean.

Judged against this:

- **Serve it:** byproducts with a one building way to dispose of the excess, fluids that need no
  perfect ratios, research paid in goods with
  simple early packs, income by product, the away report, bottleneck ranking, diagnostics overlay,
  history graphs, side branches on top of the linear tiers, new game modifiers, the codex.
- **Neutral:** megaprojects, cross-run unlocks, terraforming, machine families, operating modes.
- **Cut against it, so soften or gate:** a power grid with hard brownouts (make it a slowdown), waste
  fines, nuclear meltdowns, rate contracts with deadlines (these add pressure), retooling costs
  (they punish experimenting, which is exactly what a casual player does most), and a full tech graph
  (make it side branches instead).

## Steps

### 0. Balance tool (done)

A calculator over the content file, part of the headless CLI. Every later change is checked
against it instead of guessed:

- `balance tiers` estimates how long each tier takes with the best factory its build limits
  allow (a lower bound: no ramp-up, belt travel, orders or milestone rewards). Since 4.3.0 the
  product mix is the exact optimum, a small linear program, not a greedy pick.
- `balance items` lists value, sale price, where it is made, what uses it, and which ores its
  value comes from.
- `balance item <name> [rate]` breaks down one production line: machines per step, ores per
  second, belt load, build cost, payback time.
- Options: `--level N`, `--polish none|products|all`, `--tier N`, `--pack extra.json`.
- Since 4.28.0, `balance items` has a Per ore column (sale price over the ore in one unit), which
  compares products fairly because ore is what limits a factory.
- Since 4.27.0, `--payback MIN` makes `balance tiers` play a player who upgrades: each tier's
  factory is rebuilt at whichever level up to the old one gets to the next tier first, then
  upgraded one whole-factory level at a time as soon as it can pay, if the upgrade earns its
  price back within MIN minutes and brings the next tier closer. A Level column shows where each
  tier starts and ends. Fixed levels bracket the game (level 1 is nobody's real game, level 10
  everywhere is unaffordable early); this is the measuring stick in between.
- Since 4.8.0, `inspect <save.json>` measures a loaded factory for one minute and reports its
  buildings, product rates and income, waiting reasons, and remaining tier requirements.

`src/FactorySim.Core/Balance/` holds it (recipe book, production chains, tier pacing); the
numbers are tested against the simulation, not just the content file.

### 1. Logistics (done)

- Prices: conveyor $10, ramps $25, splitter and merger $200. Starting money $250.
- Belts: 4 items/s at level 1, 20/s at level 9. Splitters match belts. Mergers run a quarter
  faster (5/s at level 1) and reach the 20/s cap at level 7.
- Everything is in `base.json`, including `startingMoney`.

### 2. Value from processing (done)

Every ore is worth $1 and nothing else has a base value, so an item is worth exactly the work
in it: a recipe's output is worth what went in times its valueMultiplier. A gear is worth more
than the ingot it came from whatever the ingot cost, and the newest ore no longer wins by
default. Tiers now add products on top of the old chains instead of replacing them.

### 3. Connected recipe tree (done)

- New parts that feed many recipes: iron rods, screws and gears (Machine Shop), cables and
  frames (Fabricator), batteries (Battery Plant), next to plates, wire, steel, glass, plastic
  and circuits. One product pulls iron plates and copper wire through the whole factory.
- Recipes got bigger: a crate takes 4 planks, 2 plates and 4 screws; a gear takes 2 plates; a
  frame takes 2 steel, 4 rods and 4 screws; a robot takes a toy body, 2 motors and 2 circuits;
  a drone takes a frame, 2 aluminium and a robot; a satellite takes a drone, 2 jewelry and 2
  rocket fuel. Batteries need circuits, motors need batteries, so one chain feeds the next.
- The old dead ends are ingredients now: crates pack toys, toys are robot bodies, jewelry is a
  satellite part. Only the satellite is sold and used in nothing.
- A tier opens by delivering goods as well as by earning: lifetime sales at depots of specific
  items (`deliver` in the tier), so a tier means running the chain before it, not only earning.
  Deliveries are lifetime totals, so old saves' history counts toward them.
- Small parts are worth so little that a contract for them would be thousands of units, so the
  contract board only offers an item while an order for it stays a sane size.

Numbers, as the balance tool checks them: tier incomes $8, 42, 190, 884, 4.5K, 18.4K, 34.7K and
93.8K per second, against $8, 39, 199, 721, 4.97K, 16.2K, 24.3K and 77.5K before. A full run is
about 1 day 11 hours (was 2 days 1 hour). Deliveries cost at most a minute beyond earning.

Steps 2 and 3 break existing saves. Loading one never drops a building: only unknown recipes and
buildings are dropped, and a machine whose chosen recipe is gone runs automatically.

### 4. More content

More products and tiers on top of that tree. Adding a tier or product should stay a data change
in `base.json`, with no code changes. Ideas not decided yet: ore deposits placed on the map by
seed, alternative recipes, end-game megaprojects.

**First drop: consumer goods (4.3.0).** A side line with one product and one machine a tier,
built from parts the main line already makes, so every tier has a second thing worth selling:

| Tier | Good | Machine | Made from | Share of the tier's income |
|---|---|---|---|---|
| Workshop | Chair | Carpenter | 4 planks, 1 iron plate | 43% |
| Industry | Lantern | Lamp Works | glass, steel beam, 2 copper wire | 44% (34% at Petrochemicals) |
| Petrochemicals | Tire | Tire Plant | 3 plastic, 1 steel beam | a car part only |
| Electronics | Phone | Phone Factory | circuit board, battery, glass | 11% |
| Robotics | Car | Car Plant | 2 motors, 4 tires, 2 chairs | 11% |
| Aerospace | Airliner | Aircraft Works | 2 frames, 2 motors, 2 phones | 47% (11% at Space) |

Each machine makes only its own product, so no existing machine on automatic changes what it
makes, and no tier asks for a consumer good. Chairs use the logs that sat idle until crates,
lanterns the coal that sat idle until steel. Cars and airliners multiply value by 5.5 (robots 4.5,
drones 3.2) because chairs, tires and lanterns are few steps from the ore. Six goals come with it
(50 chairs, 50 lanterns, 100 tires, first phone, first car, first airliner), and six item models.

`balance tiers` at level 1, before and after:

| | Basics | Workshop | Industry | Petrochem. | Electronics | Robotics | Aerospace | Space | Whole run |
|---|---|---|---|---|---|---|---|---|---|
| Before | $8 | $42 | $190 | $884 | $4.50K | $18.41K | $34.67K | $93.84K | 1d 11h |
| After | $8 | $60 | $248 | $1.04K | $4.79K | $18.61K | $41.41K | $97.80K | 1d 6h |

The longest wait, Aerospace to Space, went from 1d 6h to 1d 1h.

Two fixes came out of measuring it:

- **The balance tool picks the best mix exactly.** It used to add products greedily, the one
  that earned most from what was left first, so a product capped by one resource never got its
  share of a shared one: plates took all the iron before chairs, capped by logs, were looked at.
  `TierPacing` now solves a small linear program (`LinearProgram`, simplex with Bland's rule, so
  it is deterministic): one variable per product and depot, limited by every raw resource and
  every depot's belt. On the old content it gives the same numbers to within 1%.
- **Research packs sell for half their parts.** At `valueMultiplier: 1` two parts' worth rode in
  one unit, which beats selling plates wherever depots are the limit (the exact optimiser sold
  packs at Workshop). At 0.5 a belt of packs never earns more than a belt of plates or wire.

To remove the drop, delete every `base.json` entry marked "Consumer goods" (6 items, 6 recipes,
6 machines, 6 goals), put the tier descriptions back and delete `ConsumerGoodsTests.cs`; a test
checks that every other item keeps its value and tier without it. The item models can stay.

**Second drop: incinerator (4.5.0).** Petrochemicals unlocks a $1,000 one-input Incinerator that
destroys every item it receives and pays nothing. It is the shallow escape valve for byproduct
lines: route wanted goods onward and send overflow here when selling the waste is not desired.
The `discarder` behavior is general, accepts any item immediately, and records only the units and
value destroyed for the inspector. The existing forge model supplies its look, so the drop needs no
new asset. It adds no recipes or products and leaves every tier income and the whole-run estimate
unchanged.

To remove the drop, delete the single `base.json` entry marked "Incinerator" and delete
`IncineratorTests.cs`; the dormant general behavior may stay for content packs. The removal test
proves every previous item, recipe, building, tier, goal, upgrade and item value remains unchanged.

**Third drop: deep-space probes (4.6.0).** The Space-tier Probe Works turns one satellite, two circuit
boards and two rocket fuel into one deep-space probe. This is an optional final sale, not a tier
delivery, so satellites keep mattering after the Launch Complex without moving any progression gate.
The Probe Works has only this recipe, which leaves every existing machine's automatic choice alone.

At level 1 the eight tier incomes before the drop were $8, $60.2, $247.6, $1.04K, $4.79K, $18.61K,
$41.41K and $97.80K per second, with a whole-run estimate of 1d 6h. After the drop they are $8,
$60.2, $247.6, $1.04K, $4.79K, $18.61K, $41.41K and $105.03K. Earlier tiers and the whole-run
estimate are unchanged, while Space income rises 7.4%.

To remove the drop, delete every `base.json` entry marked "Deep-space probes" (one item, recipe,
machine and goal), delete `DeepSpaceProbeTests.cs`, remove the `space_probe` mesh case, and restore
the recipe-tree dead-end expectations to `satellite`. The removal test proves every previous item,
recipe, building, tier, goal, upgrade and item value remains unchanged.

**Fourth drop: industrial exports (4.7.0).** Two optional sell-only products add mid-game uses for
parts with relatively few destinations. At Industry, the Tool Works packs two gears, two iron rods
and four screws into an industrial toolkit. At Petrochemicals, the Pump Works combines a frame, two
cables and two steel beams into an industrial pump. Both have dedicated machines, so existing
automatic recipe choices and every tier delivery stay unchanged.

At level 1 the eight tier incomes before the drop were $8, $60.2, $247.6, $1.04K, $4.79K, $18.61K,
$41.41K and $105.03K per second, with a whole-run estimate of 1d 6h. After the drop they are $8,
$60.2, $253.2, $1.04K, $4.79K, $18.61K, $41.41K and $105.03K. Industry rises 2.3%, later displayed
tiers and the whole-run estimate are unchanged. The optimiser uses toolkits for 32% of Industry's
income. Pumps remain a profitable buildable alternative rather than displacing the stronger
Petrochemicals optimum.

To remove the drop, delete every `base.json` entry marked "Industrial exports" (two items, recipes,
machines and goals), delete `MidGameExportsTests.cs`, remove the `toolkit` and `pump` mesh cases, and
remove their deliberate dead-end exclusions from `RecipeTreeTests.cs` and `ByproductTrialTests.cs`.
The removal test proves every previous item, recipe, building, tier, goal, upgrade and item value
remains unchanged.

**Fifth drop: sustained production goal (4.9.0).** A new data-driven `produced_rate` milestone kind
rewards a factory that keeps a line running instead of merely accumulating a lifetime total. The
optional Steady Steel goal asks for 4 steel beams per second for 30 seconds and pays $750. One second
of production may carry forward so a recipe that finishes across second boundaries is treated fairly;
longer interruptions reset the timer. The running timer and credit survive save and load. No tier uses
this goal as a gate.

Four steel beams per second for 30 seconds means at least 120 beams worth $900, so the $750 reward is
less than the goods required. It changes none of the balance tool's tier incomes or its 1d 6h whole-run
estimate because milestones are deliberately excluded from those baseline pacing estimates.

To remove the drop, delete the single `base.json` milestone marked "Sustained production goal" and
delete `SustainedProductionGoalTests.cs`; the dormant generic goal kind may stay. Loading a save without
that milestone discards its unreferenced running sample. The removal test proves every previous item,
recipe, building, tier, goal, upgrade and item value remains unchanged.

**Sixth drop: mixed-goods customer order (4.10.0).** A general `contractBundles` definition lets one
order request several goods in fixed ratios under one deadline and payout. The first bundle is Workshop
supplies: two iron plates, two copper wire and one plank per batch. It is drawn only at Workshop, where
all three goods are available, and uses the ordinary order's 45 to 90 seconds of current-income sizing
and 6 to 12 minute deadline. It pays 75% of the requested goods' base content value. The goods still
sell normally, so the bonus
does not overpay their value. Existing single-good contracts and old saves keep their original fields.

The order system does not affect `balance tiers`: the eight level-1 incomes remain $8, $60.2, $253.2,
$1.04K, $4.79K, $18.61K, $41.41K and $105.03K per second, and the whole run remains 1d 6h.

To remove the drop, delete the single `contractBundles` entry marked "Mixed customer order" from
`base.json` and delete `MixedGoodsOrderTests.cs`; the dormant general multi-line contract support may
stay. The removal test proves every previous item, recipe, building, tier, goal, upgrade and item value
remains unchanged.

**Seventh drop: the Orbital tier (4.13.0).** A ninth tier after Space, opened with 25 satellites,
$12 billion earned and a $5 billion price. It adds titanium ore and four machines, each with its
own recipe:

| Machine | Makes | From | Value |
|---|---|---|---|
| Titanium Mine | Titanium Ore | the ground, 1 every 2 s | $1 |
| Arc Furnace | Titanium | 2 titanium ore, coal (×3.5) | $10.5 |
| Solar Works | Solar Panel | 2 glass, circuit board, aluminium (×3) | $200 |
| Module Yard | Habitat Module | 4 titanium, 2 solar panels, robot (×4) | $37.5K |
| Station Dock | Orbital Station | 2 habitat modules, satellite, 4 rocket fuel (×3.5) | $465K |

A station is worth about eight satellites and uses three robots (two modules and the satellite), so
it is the best use of the late chain without making satellites pointless: `balance tiers` sells
satellites and stations about half and half. Three goals (200 titanium, the first module, the first
station).

`balance tiers`, before and after:

| | Space income | Orbital income | Space to Orbital | Whole run |
|---|---|---|---|---|
| Level 1, before | $105.03K | - | - | 1d 6h |
| Level 1, after | $105.03K | $182.74K | 1d 6h | 2d 13h |
| Level 5, before | $2.01M | - | - | 4h 0m |
| Level 5, after | $2.01M | $3.35M | 13h 17m | 17h 17m |

Every earlier tier is unchanged. The new wait is about as long as the one before it at level 1
(Aerospace to Space, 1d 1h); at level 5 it is mostly the Space factory's own setup cost, which the
balance tool counts in full. The numbers to tune are the tier's `cost` and `requiredEarnings` and
the two top multipliers. To remove it, see "Orbital tier" in the architecture notes.

**Eighth drop: customer orders for every tier (4.23.0).** The 4.10.0 mixed order existed only at
Workshop. Now each tier from Industry to Orbital has one of its own, drawn while that tier is the
newest, at the same 75% of the goods' value: a Builder's order (steel, glass, screws), Hardware store
(frames, cable, plastic), Gadget shop (circuits, batteries, jewelry), Robot workshop (a robot, motors,
circuits), Hangar order (a drone, aluminium, frames), Mission supplies (a satellite, rocket fuel,
circuits) and Station supplies (a habitat module, solar panels, titanium). Each asks for something the
tier just unlocked next to older parts, so an order pulls from the whole factory. Orders never count
in `balance tiers`, so pacing is unchanged. Remove: delete the `contractBundles` entries marked "Tier
orders" and `TierOrdersTests.cs`.

**Ninth drop: home appliances (4.24.0).** A second side line built like the consumer goods: one
product and one machine a tier, main-line parts only, each only sold.

| Tier | Good | Machine | Made from | Worth |
|---|---|---|---|---|
| Petrochemicals | Electric Kettle | Kettle Works | steel beam, cable, 2 plastic | $56 |
| Electronics | Television | TV Plant | 2 glass, 2 circuit boards, 4 plastic | $321 |
| Robotics | Washing Machine | Appliance Plant | motor, 4 steel beams, 4 iron plates | $2.65K |
| Aerospace | E-Bike | Bike Works | 2 aluminium, 2 motors, 2 batteries | $7.32K |

Measuring it showed why a short-chain product never wins the optimum: from Petrochemicals on the
depots are the limit, so the best factory sells whatever is worth most per item, and that is always
the deep main-line product. Raising the multipliers to 4 or 5 did not change that. So appliances are
deliberately the lesser choice: variety, a place for spare parts, four goals (100 kettles, first TV,
washing machine and e-bike). `balance tiers` is unchanged except Aerospace, $41.41K to $41.61K a
second. Remove: see "Home appliances" in the architecture notes.

**Tenth drop: Fusion tier (4.25.0).** A tenth tier after Orbital, opened with 10 orbital stations,
$35B earned and a $15B price. Lithium brine is pumped from a Brine Well and boiled down to lithium;
superconductors take gold ingots, titanium and cable; fusion cells take lithium, superconductors and
batteries; and a Shipyard builds a starship around an orbital station with six fusion cells and four
titanium. Starships ($1.68M) are the new most valuable good, so the old last product feeds a new one.

| Tier | Income at level 1 | Wait for it |
|---|---|---|
| Orbital | $182.74K/s | 1d 6h |
| Fusion | $297.21K/s | 1d 13h |

Tuning notes: the first draft asked for $80B earned (a 5d 8h wait) and 20 titanium a hull, which made
titanium the limit and kept starships unsold; both were cut. Three goals (500 lithium, first fusion
cell, first starship) and a Shipyard order come with it. Remove: see "Fusion tier" in the architecture
notes.

**Eleventh drop: late exports (4.26.0).** The last three tiers had the fewest products, so each gets an
optional one with a machine of its own, main-line parts only, each only sold.

| Tier | Good | Machine | Made from | Worth |
|---|---|---|---|---|
| Space | Planetary Rover | Rover Works | 4 motors, 2 batteries, 6 aluminium | $11.4K |
| Orbital | Space Suit | Suit Lab | 4 titanium, 10 plastic, 2 batteries | $1.58K |
| Fusion | Maglev Train | Maglev Works | 8 superconductors, 4 motors, 20 aluminium | $16.4K |

The first draft had four ingredients for the rover and the suit, which the recipe rule (one ingredient
a machine side) rejects, so circuit boards and the solar panel came out. `balance tiers` is unchanged.
Three goals (first rover, suit and maglev). Remove: see "Late exports" in the architecture notes.
Repriced in 4.28.0 to be built on the tier before's main product; see step 24.

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

### 8. Multiplayer (planned, nothing built)

The two concepts: **neighbours** (each player runs their own factory side by side, with a
leaderboard comparing money, income per second and tier) and a **shared factory** (two players build
one together).

Requirements:

- **No Steam app page**, so no $100 and no Steamworks account. Steam is out for now, which also
  gives up Remote Play Together (it needs a store page) and Steam leaderboards. Steam can be added
  later if an app id ever exists, so nothing below may depend on its absence.
- **Seamless for the other player**: both just start the game and play. No VPN mesh, no port
  forwarding, no addresses to copy. The friend is a few houses away, so it must work over the open
  internet, behind ordinary home NAT.
- **Offline-first, no single point of failure**: the game must never depend on any service to be
  playable. Dual host: the Oracle VPS first, the Raspberry Pi as fallback when the VPS is down, and
  if both are down the game still plays normally, with only the online features unavailable.

Proposed design (not decided):

- **A small relay and rendezvous service on the Oracle VPS** (already paid for, and it has a public
  address). Both clients connect *outward* to it, so NAT is never a problem, not even under CGNAT,
  and the service pairs them and forwards messages. A factory game sends a few KB/s at most, so the
  running cost is effectively zero. A subdomain of fayber.dev gives it a name, so no address ever
  appears in the UI.
- **The relay is stateless**: no world state lives on it, it only forwards messages. So the host's
  factory keeps running when the relay dies, the save never depends on it, and a guest rejoins when
  it comes back. That is what makes "the game does not rely on it" true rather than a promise.
- **Two hosts, tried in order.** The client ships with an endpoint list, dials them in order with a
  short timeout, caches whichever answered, retries on failure and shows which one is in use
  ("Connected via: Pi"). A DNS record under fayber.dev could carry the list, so the fallback order
  can change without shipping a build. Online is always best-effort: starting the game must never
  wait on the network, and the UI says plainly when features are unavailable and the game is solo.
- **Reaching the Pi from outside the house is the one piece that needs a one-time decision**,
  because the Pi sits behind a home NAT: either a router port forward (no third party, but needs a
  public IPv4 and no CGNAT) or a tunnel that dials outward (for example Cloudflare Tunnel, free and
  no router changes). Same-house and same-LAN play needs neither. Speaking WebSocket over TLS keeps
  both hosts, and any future one, interchangeable.
- **Transport behind an interface** in the C# layer: the relay today, Steam (or anything else) later,
  without touching the simulation. The headless CLI exercises it in tests, so the netcode is testable
  with no Steam client, no GPU and no second account.
- **Host-authoritative first.** The host owns the world and sends changes; guests send commands. It is
  simpler than lockstep and immune to floating point or platform drift. Lockstep stays cheap to add
  later, because the sim is deterministic at a fixed 20 ticks/s and already logs `(tick, Command)`.
- **Friend linking, once.** A short friend code exchanged a single time; after that a session is
  "Host" and "Join (friend online)" with nothing to type. A shareable invite link covers players who
  are not linked yet. This is what replaces Steam's friends list and lobbies.
- **The same service can carry concept 1's leaderboard** (money, income per second, tier), which is
  the job Steam's leaderboards would have done.
- **A persistent shared factory is possible**: `FactorySim.Cli` runs the world headlessly, so the Pi
  or the VPS can host it and it keeps producing while everyone is away. Solo already has the offline
  catch-up this needs.
- **Save slots**: one authoritative save, the host's slot. Guests do not autosave (today every client
  would write its own copy of the same world), a guest may copy the save to continue alone, and
  offline catch-up is off for a shared world since extrapolating a world others are playing is wrong.

Open items (design, not technology): shared or separate money, tier and land; who may remove or
upgrade whose buildings; what pause means when one player is mid-build; and whether the service
lives on fayber.dev.

### 9. Byproducts (trial)

A recipe can make two things from one craft, and a splitter can sort a mixed belt: each output takes
anything, one item, or the overflow (what the other outputs do not take or refuse because they are
full). A full output stops the splitter rather than the hub storing the surplus, which is his call of
2026-09-29 (see the Router entry in ARCHITECTURE.md). The lazy way out is one splitter with the
wanted item on one output and overflow into a depot or the Incinerator. Filters are a `SetFilter`
command: undoable, saved, kept by blueprints.

The content is a trial, to see if it is liked: an Oil Refinery can crack crude oil into plastic and
**tar**, and a Blast Furnace burns tar instead of coal. Every output is worth what the plain recipes
make, so no existing value changed (`balance items`, `balance tiers` and `balance land` are identical
apart from the new Tar row), and tar is used by steel, so the satellite is still the only dead end.

To remove the trial in one pass, delete exactly these from `base.json`: the item `tar`, the recipes
`crack_oil` and `forge_steel_tar`, those two ids in the `refinery` and `blast_furnace` recipe lists,
and the comment blocks marked "Byproduct trial"; and delete `ByproductTrialTests.cs`. The numbers to tune are the counts, `ticks` and
`valueMultiplier` of those two recipes. Details: "Byproducts" in the architecture notes.

### 10. Research (trial)

Research sits beside the tiers and never gates them. A Science Bench (tier 1) makes a Basic Science
Pack from an iron plate and a copper wire. At Industry it also makes an Advanced Science Pack from
steel, a gear and two screws. A Lab takes either pack off a belt and banks it, and the Research
window (key L) spends the bank on three global bonuses: drill output +5%, market prices +5% and
machine speed +10% a level. The basic pack buys five levels and the advanced pack buys three more;
pack prices double each level. A lab banks one pack every 4 seconds whether or not anything is being
bought, so a research line never backs up. Packs sell for half their parts and are never ordered,
and neither slice changes an existing item's value, tier or baseline pacing.

This is slices 1 and 2 of `docs/RESEARCH-PLAN.md` (option A, a flat list, no tree). Slice 2 is the
five entries marked "Research slice 2" plus `pack_2` in the Science Bench recipe list. Removing it
leaves slice 1 unchanged. Removing both science items, both recipes, both buildings and all six
upgrades removes the whole system from content and hides the Research button. The numbers to tune are the recipes, upgrades' `perLevel`,
`maxLevel`, `costGrowth` and `packs`, and the lab's `interval`. Details: "Research" in the
architecture notes.

### 11. The away report (done)

After loading a factory that was closed at least two minutes, a "While you were away" window shows
what the offline catch-up found: the money, each product's rate and earnings (plus order and goal
rewards), packs banked, and the buildings that sat waiting at least half the time, grouped and
ranked by the time lost, each with a Show button that flies the camera there. Machine statuses now
say why they wait (starved or blocked, yellow or red lamps), and offline time banks science packs
like money. It is idea F1; the setting turns it back into the old toast. Details: "Offline
progress" in the architecture notes.

### 12. Live bottlenecks (done)

The away report's list, while you play (ideas F3 and F5). The Bottlenecks window (key K) ranks the
buildings that spent at least half of the last 30 seconds waiting, grouped by building and reason,
by the time lost, each with a Show button. Key J, or the switch in the window, floats a pin over
every one of them: yellow waits for input, red cannot get rid of its output, the same colours as
the status lamps. It only reads the world, so it changes no numbers and nothing is saved. The
numbers to tune are the window length and the 50% bar (`BottleneckTracker`). Details: "Offline
progress" in the architecture notes.

### 13. The line planner (done)

Idea F9. The Planner window (key H) answers "what does a line of this need": pick any product the
factory can make and a rate per second, and it lists every step from the ore up with the number of
buildings, belts when one is not enough, and a red count where the tier limit is too low, then the
raw inputs, byproducts, what the line earns sold at a depot, what it costs to build and how soon it
pays back. A building level switch shows how upgrades shrink the line. It is the balance tool's
math with a window on top, so the numbers always match `balance item`. Details: "The Planner" in
the architecture notes.

### 14. History graphs (done)

Idea F2. The History window (key T) draws income, money or the production rate of any item made so
far, over the last hour (a point every 30 seconds) or the last day (a point every 15 minutes). The
points are saved with the statistics, so the graphs survive a restart, and an older save simply
starts with empty graphs. Each point keeps its tick and running totals, so time away (which the
catch-up only partly simulates) shows as one long step at the right average. The numbers to tune
are the two intervals and capacities in `HistoryLog`. Details: "History graphs" in the architecture
notes.

### 15. Alerts (done)

Idea F4. Things that go wrong while you look elsewhere raise a toast and an entry in the Alerts
window (key N, with an unseen count on its sidebar button): a machine that was running and then did
nothing at all for 30 seconds (stopped for input, or jammed with nowhere to put its output), an
order with a fifth of its time left, and an order that ran out. A slow, underfed machine is the
Bottlenecks window's business, not an alert, and a machine that never ran yet (a line still being
built) raises nothing. Machines of one type that stop together make one entry, one that recovers
and stops again within two minutes is not reported twice, and entries mark themselves resolved when
the machines run again or the order is delivered. Nothing is saved. The numbers to tune are the
constants in `AlertLog`. Low power and full depots from the idea do not apply: there is no power,
and depots sell everything. Details: "Alerts" in the architecture notes.

### 16. Speed steps and Run until (done)

Ideas H1 and H2. The factory card has a speed row: − and + (keys `,` and `.`) step through ×0.5,
×1, ×2, ×4, ×8 and ×16, and **Run until…** runs the simulation as fast as the computer allows
(about 12 ms of simulation per frame, so the game stays responsive) until the next tier can be
unlocked, money doubles, or ten minutes pass. A machine that stops (an alert) ends it early, and it
gives up after an hour of game time, saying what the next tier still needs. The goals and the limit
live in `FastForward` in the core. The tier check the unlock command makes is now one method,
`Simulation.NextTierBlocker`, shared by both. Details: "Speed and Run until" in the architecture notes.

### 17. Signs (done)

Idea G3. A Sign is a cheap 1×1 building with no ports that shows a line of text (up to 40
characters) floating above it, turned to the camera. It never works or waits, so the bottleneck
pins and alerts ignore it. The text is written in the Manage window, for one sign or several at
once. It is stored as the sign's "selection", the slot a machine's recipe choice uses, so the
existing command, undo, blueprints and saves carry it with no new plumbing. Details: "Signs" in the
architecture notes.

### 18. Build menu search (done)

Idea H5. A search box at the top of the build menu filters the tiles by every word typed, matched
against the building's name, category, description, and the names of the items its recipes make and
use (or the ore a drill mines). Enter builds the first unlocked match, and closing the menu clears
the search. It lives entirely in the client's `BuildMenu`.

### 19. Production targets (done)

Idea F7. The Targets window (`Y`) holds up to 12 targets of the form "60 iron ingots a minute".
Each row shows what the factory made over the last minute, a bar, and − and + that move the target
through round steps. A new target starts one step above today's rate, so it is something to work
towards. A target that stays under for a minute raises an entry in Alerts, and the sidebar button
counts the targets that are under right now. Targets are saved and every change can be undone.
The idea's "keep 200 in stock" did not fit a factory that sells everything at depots, so a target
is a rate, not a stock. Details: "Production targets" in the architecture notes.

### 20. Why it waits (done)

Idea H6. When one selected building keeps waiting, the Manage window says why and what to do:
the input it lacks and which buildings make it, how many of those you have and whether they run
("Made by Iron Drill (you have 2, all running)"), that you have none yet, or the tier that unlocks
them. For a full output it names the stuck item and what could take it: a depot or the machines
that use it. It follows the Bottlenecks window's 30 s view, so a machine on an underfed line does
not flicker in and out between two items. Details: "Why it waits" in the architecture notes.

### 21. Copy settings (done)

Idea H3. Ctrl+Shift+C over a building copies its settings: what a machine makes (or automatic), a
splitter's output filters, or a sign's text. Ctrl+Shift+V pastes them onto the selection, or the
hovered building when nothing is selected. Only buildings of the same kind change, and one paste is
one undo step. The Manage window has the same as two buttons, and the paste button says how many
buildings it would change. Details: "Copy settings" in the architecture notes.

### 22. Statistics page (done)

Idea G6. The Statistics window gains records and totals: factory time, the best income over a full
minute, the most money and the most buildings, each with when it was set, the time each tier was
reached, and a table of every item made or sold with both counts. Records are saved; a save from
before this starts them fresh, and tiers reached before it read "reached". Details: "Personal
records" in the architecture notes.

### 23. Bulk machines (done)

Idea B11, as content. A Bulk Smelter, Bulk Press and Bulk Forge arrive with the Electronics tier.
Each runs twice the plain recipe's input and output in 1.5 times the time, at the same value
multiplier: a third more per machine, and an item is worth the same whichever machine made it. They
are separate buildings rather than extra recipes on the old ones, so a Smelter never has a strictly
better choice sitting next to its plain one and nothing needs a per-recipe unlock. They drop onto an
existing machine like any other machine. Remove them by deleting the base.json entries marked "Bulk
machines" (see `BulkMachinesTests`).

### 24. Big numbers and a smooth curve (done)

The 4.28.0 rebalance, all in `base.json` plus two knobs. Asked for: much bigger money late on, a wait
that grows smoothly tier by tier instead of one wall, side products that stay worth making, and the
game's length as one setting that can be changed later.

- **Two knobs.** A tier's `valueBoost` multiplies the value of every good first made at that tier,
  and it compounds down the chain (Electronics 1.15, Robotics 1.85, Aerospace 3.7, Space 1.55,
  Orbital 3.8, Fusion 4). The top-level `priceScale` (1) multiplies every price and no value:
  buildings, upgrades, tiers, land, starting money, goal rewards and money goals. Income stays the
  same, so the whole game takes exactly that many times as long. Details: "Economy knobs" in the
  architecture notes.
- **Tier prices** follow the new incomes: Electronics asks for $3.5M earned, Robotics $60M,
  Aerospace $1.8B, Space $27B, Orbital $260B, Fusion $2.9T, each with a price of 40% of that.

| Tier | Income at level 1, before | after | Wait at `--payback 30`, before | after |
|---|---|---|---|---|
| Petrochemicals | $1.04K/s | $1.04K/s | 8m | 10m |
| Electronics | $4.79K/s | $6.48K/s | 21m | 16m |
| Robotics | $18.61K/s | $43.00K/s | 24m | 23m |
| Aerospace | $41.61K/s | $294.88K/s | 3h 28m | 31m |
| Space | $105.03K/s | $2.26M/s | 14h 2m | 43m |
| Orbital | $182.74K/s | $12.54M/s | 2m | 1h 1m |
| Fusion | $297.21K/s | $76.94M/s | | |

The waits are the time before the next tier opens. A player who upgrades (`--payback 30`) ends at
$22.57B a second in a level 10 Fusion factory (was $734.82K at level 2), and a starship is worth
$302.63M (was $1.68M). Each late tier earns 3 to 12 times the one before at level 1 (a test).

- **Length is the open part.** The reference player now finishes in 3h 18m, against 18h 39m before,
  almost all of which was the 14 hour Space wall; at level 1 it is 5d 5h (was 3d 16h). `priceScale`
  stretches it: for the same `--payback 30` player 2 gives 6h 36m, 3 gives 9h 55m and 4 gives 14h 1m.
  Above 4 it jumps (4.5: 22h, 5: 1d 14h), because upgrades stop earning their price back within 30
  minutes and the factory stays a few levels lower. Level 1 scales exactly. Step 25 settled it
  another way: at least 10 hours for the best player.
- **Side products by ore.** `balance items` has a Per ore column: what one sells for over the ore
  in it, the fair comparison because ore is what limits a factory. The late exports, worth a few
  thousand dollars next to main products worth millions, are now built on the previous tier's main
  product (rover: a drone, a robot, 4 batteries; suit: a habitat module, 4 titanium, 2 batteries;
  maglev: 2 habitat modules, 8 superconductors, a fusion cell) and earn about 52, 66 and 41% per ore
  of the probe, the station and the starship. The other side products moved with the boosts (kettle
  x3.2, television x6, washing machine x3.5, car x5, e-bike x10). At level 1 the phone takes 20% of
  Electronics' sales, the car 21% of Robotics' and the airliner 30% of Aerospace's.
- **Idle coal.** A motor takes 2 steel (was 1), which uses up the coal Electronics left idle.
  Robotics still leaves 18% of its coal idle, which is accepted for now.
- **Land.** The first ring costs $1K and every ring 22 times more, up to $5.15B in the far corners,
  about a minute of an upgraded Fusion factory's income (was $2.5K times 8, up to $82M). The far
  corners stay a late goal.
- **Goals.** Rewards from Electronics on follow the new incomes, and a new goal asks for a
  quadrillion dollars earned (53 goals). Basics to Industry are unchanged, so the tutorial and the
  first quarter of an hour play exactly as before.

### 25. Ten hours at least (done)

The 4.29.0 rebalance. Asked for: the ten tiers should take at least 10 hours, and depot and export
terminal levels should get more expensive the higher they go. The 3h 18m of 4.28.0 came from cheap
upgrades: a player who upgraded ran about 40 times faster than one at level 1, so upgrading skipped
most of the game.

- **Level tracks in the content.** The upgrade track of each behavior now lives in `base.json`
  (`levelTracks`: processor, miner, seller); a building's own `upgrade` still wins, and the C#
  defaults are only the fallback. Changing what a level costs is now a content edit.
- **Pricier levels.** A machine's first level costs 2 times the machine (was 1.2) and every next
  one 2.2 times the last (was 1.7); a drill's 2 times, then 2.5 (was 1.2, then 1.65). Level 25 is
  still the cap for both.
- **Steeper depots.** A track can set `costGrowthStep`, added to the growth for every level: Market
  Depots and export terminals go x1.9, x2.0, x2.1 and so on, still without a cap. Going from level
  20 to 21 on a $50 depot costs about $21.5B (was $19.8M), from 30 to 31 about $4e16 (was $12B).
- **Smoothed tier prices.** Workshop $1.35K earned, Industry $33K, Petrochemicals $820K,
  Electronics $28M, Robotics $350M, Aerospace $5.2B, Space $53B, Orbital $390B, Fusion $2.8T, so
  every tier takes longer than the one before (a test). Deliveries and value boosts are unchanged.

| Tier | Wait at `--payback 30`, before | after |
|---|---|---|
| Basics | 2m 12s | 3m 2s |
| Workshop | 2m 47s | 8m 0s |
| Industry | 9m 8s | 19m 40s |
| Petrochemicals | 10m 23s | 40m 12s |
| Electronics | 15m 44s | 1h 3m |
| Robotics | 22m 44s | 1h 29m |
| Aerospace | 30m 44s | 1h 55m |
| Space | 43m 19s | 2h 17m |
| Orbital | 1h 1m | 2h 50m |
| Total | 3h 18m | 10h 46m |

- **Play styles.** The most patient player (upgrades whenever it brings the next tier closer) needs
  10h 28m, the fastest there is; one who only upgrades drills 14h 7m. Pickier players take longer:
  `--payback 10` 22h 21m, `--payback 5` 1d 17h, level 3 1d 16h, level 1 8d 1h (was 5d 5h).
  `GameLengthTests` keeps the best player at 10 hours or more.
- **Income.** Fewer levels means a smaller mid game: the reference player reaches Fusion at level 5
  earning $1.64B a second (was $22.57B at level 10). Fusion has no end, so the patient player keeps
  climbing there, to about $3.23T a second.
- **Other levers tried.** 100 times the deliveries only gave 4h 14m (the optimizer upgrades drills
  instead), and `priceScale` 3 gave 9h 55m with every price, land included, 3 times higher. The
  level costs were the cause, so they were the fix.
- **Old saves.** Removing a building refunds what its levels cost at today's prices, so an upgraded
  building from an older save refunds more than was paid for it, once. Accepted.

## Open questions

- Step 4 (more content): the consumer goods side line shipped in 4.3.0; next, whether to keep it
  and what to add after it, and which of the undecided ideas (ore deposits by seed, alternative
  recipes, end-game megaprojects) is worth a first try.
- Step 8 (multiplayer): the open items listed above, and when to start it.
- Step 9 (byproducts): keep, extend or remove the trial after playing it.
- Step 10 (research): keep, retune or remove the two-slice trial after playing it; whether a third
  pack or optional content unlocks should ever follow.
- Step 25 (length): the ten tiers now take at least 10 hours for the best player. Whether the
  levels above that (8 days at level 1) feel right after playing it.
