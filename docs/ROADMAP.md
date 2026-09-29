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
full). A full output only stalls the items bound for it: the splitter holds a bounded number of them
aside so items with a free output of their own keep leaving. The lazy way out is one splitter with
the wanted item on one output and overflow into a depot; no sink building. Filters are a `SetFilter`
command: undoable, saved, kept by blueprints.

The content is a trial, to see if it is liked: an Oil Refinery can crack crude oil into plastic and
**tar**, and a Blast Furnace burns tar instead of coal. Every output is worth what the plain recipes
make, so no existing value changed (`balance items`, `balance tiers` and `balance land` are identical
apart from the new Tar row), and tar is used by steel, so the satellite is still the only dead end.

To remove the trial in one pass, delete exactly these from `base.json`: the item `tar`, the recipes
`crack_oil` and `forge_steel_tar`, those two ids in the `refinery` and `blast_furnace` recipe lists,
and the comment blocks marked "Byproduct trial"; and delete `ByproductTrialTests.cs`. The numbers to tune are the counts, `ticks` and
`valueMultiplier` of those two recipes. Details: "Byproducts" in the architecture notes.

## Open questions

- Step 4 (more content): which products and tiers to add on top of the tree.
- Step 8 (multiplayer): the open items listed above, and when to start it.
- Step 9 (byproducts): keep, extend or remove the trial after playing it.
