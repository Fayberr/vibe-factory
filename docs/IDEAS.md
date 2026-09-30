# Ideas: depth, versatility, replay value, long-term fun

Nothing here is decided. This is a menu to pick from, ordered by area, not by priority.
Numbers for anything that ships are checked with the balance tool (`balance tiers/items/item/land`)
before it goes in, the same as steps 2 and 3.

Effort tags: **(data)** content only in `base.json`, **(S)** small, **(M)** medium, **(L)** large,
**(new)** a new subsystem.

## Where the game is strong and weak today

Strong: the recipe tree (steps 2 and 3), logistics as a real gate (the merger caps a line),
land plots, contracts and milestones, a deterministic core, and content as data.

Weak, and this is what most of the list below attacks:

- **The map is decoration.** All 8 drills are a fixed rate and a count cap, and work anywhere, so
  where you build does not matter and a far plot only buys space.
- **Nothing ever overflows.** All 25 recipes have exactly one output, so no recipe creates a
  logistics problem beyond "bring me more".
- **There is only one progression axis.** Tiers. The global stat upgrade list is empty, so money
  only buys buildings, land and levels.

## Picks so far

Wanted:

- **Research that is paid in manufactured items** (`I1`, science packs and labs). Research is a
  production line: labs eat packs, packs are built in the factory, and the tree is bought with goods
  rather than money.
- **Byproducts** (`B1`), and with it `B2` priority outputs and `B3` filters, the tools that make
  byproducts solvable.
- **Fluids, handled differently from items**: a pipe network, with pumps and tanks (`B9`, `I7`).
- **The away report** (`F1`). Built in 4.2.0, roadmap step 11.
- **Income by product** (`F8`). Built, in the Statistics window.

Liked but unsure:

- **A1, ore deposits.** The required-placement version is not wanted: having to place drills on
  designated spots is the objection. Build the no-chore variant in A1 above, where a patch is a rate
  bonus and never a requirement, or drop the idea entirely.

Not yet decided:

- **The tech graph (`I2`/`D1`) and megaprojects (`I4`).** Neither settled nor ruled out, and both
  currently read as unclear, so each now carries a plain words explanation at its entry, plus, for
  `I2`, a much smaller middle option (keep the linear tiers, let research packs additionally unlock
  optional side branches). Do not build either without asking.

Parked but not rejected: everything else in Parts 1 to 3.

Unsure, decide later: everything else in Part 2 and Part 3.

Parked but not rejected: the rest of Part 1. Part 2 and Part 3 are the larger menus.

---

## A. The map as a place (spatial depth)

- **A1. Ore deposits by seed (M). Liked but hesitant: needs the no-chore variant below.** Ore comes
  in patches of varying richness. This change makes land, rim depots, long belts and mergers all
  matter at once, because distance becomes a real cost. The real cost is that the balance tool needs
  an ore supply model.
  - **The no-chore variant (preferred).** A drill still works anywhere, but standing on a patch
    multiplies its rate. Nothing is ever invalid or impossible, so there is no hunting for spots and
    no fussy placement, and the map still matters because a rich patch is worth reaching. The patch
    can also come with the plot when land is bought, so it is never something you go looking for.
    This is the version to build if A1 is built at all.
- **A2. Terrain: water, cliffs, boulders (M).** Blocks building, must be routed around or cleared
  with dynamite. Turns every map into a different layout puzzle.
- **A3. Map features (M).** A river for a water intake, a geothermal vent for power, a crater with
  a rare ore. Landmarks that shape where the factory grows.
- **A4. Underground belts and tunnels (M).** Cross one belt under another with no merge. The most
  loved logistics convenience in the genre, and verticality is already in the engine.
- **A5. Bridges over water and lines (M).** Rides on A4 once that exists.
- **A6. Trains: rails, stations, wagons (L).** Only worth it after A1 makes distances long.
- **A7. A second region to travel to (L).** Another island with its own ores, goods shipped between.

## B. New kinds of recipe puzzle

- **B1. Byproducts, recipes with two outputs (S code + data).** Refining that yields plastic *and*
  fuel. Creates the one puzzle the tree lacks: what do you do with the excess. `RecipeDef.Outputs`
  is already a list, so this is mostly content plus handling two stacks.
- **B2. Priority outputs on splitters (S).** "Left first, right only when left is full." Makes
  byproducts solvable and unlocks overflow designs.
- **B3. Filters on splitters (S).** "Iron left, copper right." Separates a mixed belt, which is the
  most requested logistics tool in the genre.
- **B4. A sink building (done in 4.5.0).** An incinerator for solid items or a flare stack for gases, which
  destroys whatever it is fed for a small cost. This is the escape valve that makes B1 safe: feed the
  unwanted output in and the line keeps running instead of jamming. Worth knowing that the depot
  already sells anything it is given, so anything sellable has a simpler answer and the sink is only
  needed for what cannot be sold, or when the money is not wanted.
- **B5. Alternative recipes (data).** The same product from different inputs, so you can adapt to
  what your map actually has. A large replay lever for very little work.
- **B6. Tag inputs (S).** A recipe asks for "any acid" rather than one exact item. The tag effect
  mechanism is already in the code.
- **B7. Probabilistic recipes (S).** One ingot plus a 10% chance of a gem, from the seeded `Rng`.
  Texture, and a reason to over-build a line.
- **B8. Recycling and dismantling (S).** Finished goods back into parts or scrap. A sink that is
  also a loop.
- **B9. Fluids: pipes, pumps, tanks (new, L).** Makes petrochemicals stop being "another ore in a
  barrel". The largest idea on this page.
- **B10. Catalyst loops (M).** Something consumed and regenerated, so the loop has to balance.
- **B11. Bulk recipes (data).** Twice the input in 1.5 times the time, for late game throughput.
- **B12. Quality variants (M).** A recipe that can output a graded item worth more.

## C. Things to manage (operational depth)

- **C1. Power (new, L).** Machines need power, generators burn coal and oil, the grid has a
  capacity and brownouts when you exceed it. This is the biggest management layer available: it
  creates a second factory whose product is power, it keeps coal relevant forever, and it gives
  the factory a heartbeat you can watch.
- **C2. Waste and pollution (M/L).** Machines emit waste that must be treated, stored or vented, and
  ignoring it costs something.
- **C3. Heat (M).** Temperature as an item tag with decay, plus heaters, coolers and quench recipes.
  Already sketched in the architecture notes.
- **C4. A logic network (new, L).** Signals and conditions, such as "switch this machine off while
  the chest is full". The deep endgame toy for optimisation players.
- **C5. Per-entity overclock (S/M).** Pay more input for more speed on one machine.
- **C6. Workers (M).** Hire and assign people to buildings for bonuses. An economy sink and a bit
  of personality.
- **C7. Anomalies (S).** Seeded events that appear on machines and reward attention. Gives a reason
  to look at the factory rather than only at the numbers.
- **C8. Wear and repair (M).** Gentle condition loss that needs a repair kit. Risky, because it can
  read as nagging, so keep it optional.

## D. Ways to play differently (versatility)

- **D1. A branching research tree (M).** Global upgrades with prerequisites, so you choose logistics
  or efficiency or value and cannot have everything. This is the real key to replay value, because
  two runs stop looking the same. It also fills the empty `upgrades` list and gives money a long
  term sink.
- **D2. New game modifiers (S).** Rich ore but costly land, scarce ore but cheap machines, fast belts
  but no mergers. Multiplier knobs, enormous replay value.
- **D3. Scenario maps (M).** A fixed seed and a fixed goal, such as "supply 1000 circuits in 30
  minutes". Also leaderboard ready.
- **D4. Structural tier goals (M).** "Make a robot without touching the oil chain" rather than only
  numbers. Keep it to a handful of hand written checks, not a general rule language.
- **D5. Named customers with reputation (M).** Buyers with preferences who pay better as they trust
  you. Texture plus goals.
- **D6. Several endgame megaprojects to choose between (M).** So two finished factories do not end
  the same way.
- **D7. Mod packs, first class (M).** The loader already overlays packs. Add a mods folder, an
  in-game list, clear validation messages and a share format. Because content is data, this is
  mostly plumbing, and it gives unlimited content and a community.

## E. Fresh starts (replay value)

- **E1. Seeded maps (part of A1).** Ore distribution, terrain and features differ every game. This
  is the core of replay value.
- **E2. Shareable seeds (S).** A short string so two players can run the same map and compare.
- **E3. A randomised starting area (S).** Different openings: which ore is near, what terrain.
- **E4. Branching progression (with D1).** Two runs do not follow the same order.
- **E5. Optional challenge rules (S).** No mergers, one depot only, no land beyond the start. Mostly
  content plus a check.

## F. Feedback that keeps you playing (long-term fun)

- **F1. The away report (M).** On return, show what was produced, what jammed and what starved while
  you were gone. This turns absence into instruction, and offline catch-up already runs. Highest
  retention value on this page. *Built in 4.2.0* (roadmap step 11): products with rates and
  earnings, and the buildings that waited, grouped by reason, each with a Show button.
- **F2. History graphs (S).** Money, income and the production of a chosen item over time, from a
  ring buffer. Progress you can see. *Built in 4.14.0* (roadmap step 14): the History window,
  key T, with an hour and a day of saved points.
- **F3. A bottleneck ranking (M).** "Your five biggest constraints right now", each one clickable to
  fly there. It is the question every player asks, and `EntityStatus` already computes the inputs.
  *Built in 4.11.0* (roadmap step 12): the Bottlenecks window, key K.
- **F4. Alerts (S/M).** A dismissible log of jams, low power, full depots and contract deadlines,
  with a fly-to. *Built in 4.15.0* (roadmap step 15): the Alerts window, key N, for stopped and
  jammed machines and ending and expired orders.
- **F5. The diagnostics overlay (S).** One hotkey lights up every machine that is not working,
  coloured by reason. The cheapest large win here, because the data already exists.
  *Built in 4.11.0* (roadmap step 12): key J pins every building that keeps waiting.
- **F6. A throughput view (M).** Show belts near capacity and mergers as gates.
- **F7. Self set targets (S/M).** "Keep 200 circuits in stock", with an indicator when you are under.
  *Built in 4.18.0* (roadmap step 19): as rates (items a minute), in the Targets window, with an alert.
- **F8. Income by product (S).** A ranked list of which products actually
  earn: income per second per item over the last few minutes, lifetime earned per item, and each
  one's share of total income, so it is obvious that circuits are 60% of the money and crates are 2%.
  It answers the question every factory owner asks: what do I expand, and what do I stop making.
  Nearly free, because `StatsTracker` already keeps lifetime `Sold` and `Produced` per item and a
  rolling 60 s income window, so only a per-item window is new.
  *Built* in the Statistics window: income per second, share and lifetime earnings per product, ranked,
  plus the products that are made but earn nothing.
- **F9. A chain and ratio helper (M).** Ask "what feeds this" or "how much do I need for one circuit
  per second", and the game answers with the input rates and the machine counts, from the recipe
  tree. The balance tool already computes exactly this in the CLI (`balance item <name> [rate]`), so a
  player facing version is mostly UI over math that exists. This is the feature that lets a casual
  player skip the arithmetic the deep end normally demands, which is what makes depth optional
  (roadmap principle 6).
  *Built in 4.12.0* (roadmap step 13): the Planner window, key H.

## G. Ownership and expression

- **G1. A named blueprint library (S).** Save, name, sort and reuse across sessions, plus a
  shareable string. Also a precondition for sharing with a friend later.
- **G2. Blueprint mirror and multi-rotate (S).** The clipboard already exists.
- **G3. Signs and labels (S).** Placeable text so a big factory explains itself.
  *Built in 4.17.0* (roadmap step 17): the Sign building, written in the Manage window.
- **G4. Paint or per line colours (S).** Cheap ownership.
- **G5. Photo mode and timelapse (S/M).** Hide the UI, orbit freely, and export the factory growing
  from `EditHistory`.
- **G6. A statistics page (S).** Totals per item, playtime, tiers and personal records.

## H. Convenience that removes friction

- **H1. Run until (S).** Fast forward until a building finishes, a tier unlocks, or money hits a
  target. *Built in 4.16.0* (roadmap step 16): until the next tier is ready, money doubles or ten
  minutes pass, stopping early when a machine stops.
- **H2. A finer speed control (S).** A slider from x0.5 to x10 instead of fixed steps.
  *Built in 4.16.0* (roadmap step 16) as steps: ×0.5 to ×16 on the factory card, keys `,` and `.`.
- **H3. Copy settings between machines (S).** Paste a recipe to a whole selection.
- **H4. Mass upgrade a line (S).** Partly exists through the upgrade tool.
- **H5. Search and filter in the build menu (S).**
  *Built in 4.17.0* (roadmap step 18): matches names, descriptions and the items made or used.
- **H6. A real "why is this stopped" explainer (S).** Name the missing input in the inspector, and
  where it could come from.

---

## If I could only pick five

1. **A1 plus A2, the map as a place.** It is the change that makes land, depots, mergers and long
   belts all pay off at once, and it is the foundation for trains and regions later.
2. **B1 plus B2 plus B3, the overflow puzzle with the routing tools to solve it.** Adds a whole class
   of design problem, and filters and priority are the two most wanted logistics features.
3. **C1, power.** The biggest single management layer, and it keeps coal meaningful for the whole
   run.
4. **D1 plus D2, research branches and new game modifiers.** The cheapest large jump in replay value
   in the list.
5. **F1 plus F3 plus F5, feedback.** Away report, bottleneck ranking and a diagnostics overlay. These
   are what make a large factory feel operable instead of overwhelming.

## Deliberately not proposed

- **Prestige or rebirth.** Already decided against in the roadmap.
- **Artificial slowdown** to stretch playtime. Already decided against.
- **Per copy price scaling on logistics.** Already decided against.
- **Finite depleting ore as a punishment.** Prefer varying richness (A1): it creates the same
  pressure without ever bricking a running factory.

---

# Part 2: big systems

The large additions. Each one is a project, not a task, and each changes the shape of the game
rather than polishing a corner. Ranked inside each group roughly by fit with what the game already is
(open-ended, data-driven, deterministic, no prestige). Effort: **L** large, **XL** very large.

## I. Progression you build, not buy

- **I1. Science packs and labs (L). This is the strongest idea on the page.** Instead of tiers that
  open when you have earned and delivered enough, research is a *physical product*: labs consume
  science packs, packs are made in the factory from increasingly complex parts, and the tech tree is
  paid in packs rather than money. It turns progression into a production line, so every tier ends
  with "build the next science chain" instead of "sell enough stuff". It marries directly with D1
  (branching research) and it makes the whole factory matter, because packs pull from every chain.
- **I2. A technology graph instead of a line (L).** A graph with parallel branches means a run is a
  set of choices: go deep on electronics first, or on oil, or on logistics. This is the biggest
  structural replay lever available, and it is the natural home for I1 and D1.
  - **In plain words.** Today the 8 tiers are a straight line: tier 3 opens after tier 2, in the same
    order in every game, so every run walks the same sequence. A graph replaces that line with a web
    you choose from. You spend research packs on the branch you want, so one player goes electronics
    first and another goes oil first, and their factories end up different.
  - **Middle option, much smaller (recommended if this is wanted at all).** Keep the linear tiers
    exactly as they are and let research packs *additionally* unlock optional side branches (extra
    recipes and machines). You choose what to spend on, runs differ, and progression is not rebuilt.
- **I3. Launches as a real loop (L).** The satellite is already the final product and the only dead
  end. Make it the engine: build a launch pad, launch what you build, and each launch grants a
  permanent bonus (a stat, a recipe, a land ring) plus a record to beat. The endgame becomes a goal
  you push toward repeatedly instead of a wall.
- **I4. Megaprojects or wonders (L).** One huge structure that consumes the factory's output for
  hours and visibly grows, so there is something to look at and something to feed. Optional, and the
  player picks which one to attempt, so finished factories differ.
  - **In plain words.** A single giant build target, such as a space elevator or an orbital station,
    that needs a steady supply of many late products delivered to one structure over hours. It gives
    an open-ended game an optional long finish line and something to watch grow, and finishing it
    could pay a permanent bonus or open a new area. It stays optional, so the game keeps its open end
    either way. Skip it if a finish line is not wanted.
- **I5. Cross-run unlocks (M).** Achievements and records that unlock new starting options for the
  next run (a new starting plot, a modifier, a blueprint). Not prestige: nothing is ever lost, no
  reset loop, but a fresh start is genuinely different. Strong replay value without the downward
  pressure prestige brings.

## II. Moving things at scale

- **I6. Robots: construction and logistics (XL).** Construction bots build a blueprint for you,
  logistics bots haul between chests without belts. This is the biggest change to *how you play* on
  the whole list: laying out the factory stops being mouse work and becomes design. It also needs a
  power network (C1) and it pairs with the blueprint library (G1).
- **I7. The oil and chemistry layer, expanded from B9 (L).** Fluids done properly: a refinery splits
  crude into fractions, cracking turns heavy into light, some processes produce waste heat or waste
  products, and gases, liquids and solids each need the right container. This is what makes
  petrochemicals feel like a different industry instead of another ore on a belt.
- **I8. Trains with schedules, stations and signals (XL).** Long-haul logistics with real decisions:
  where the stations go, how many trains, how to avoid one blocking another. Only makes sense once
  distance costs something, so it builds on A1 or on ore that lives far from the factory.
- **I9. Separate energy networks: heat, steam and electricity (L).** Instead of one generic power
  number, different machines need different carriers, and waste heat has to be cooled away. Makes a
  second factory whose product is energy, and gives every tier a new infrastructure layer.
- **I10. Underground and multi-level factories (L).** A layer below the surface, or belts that stack
  vertically, so a dense factory grows in three dimensions instead of sprawling. Also a way to cross
  terrain and other lines without A4's single-tile tunnels.

## III. A world worth being in

- **I11. Regions or planets (XL).** A second and third place with its own climate, its own ores and
  its own problems, connected by shipping. Each one reuses the whole content pipeline, so new ores
  and recipes stay data.
- **I12. Exploration and ruins (M/L).** Fog on the untouched map, wrecks and ruins to find, each
  granting a unique recipe, a blueprint or a rare part you cannot make. Gives the map a reason to be
  looked at, and a reason to buy land beyond space.
- **I13. A second resource domain: agriculture (L).** Farms, animals, fertiliser and organics,
  producing food, fibre and chemicals that mining cannot. It doubles the number of distinct chains
  and it gives the map a living, renewable half next to the extracting half.

## IV. The economy and the neighbours

- **I14. A dynamic market (L).** Prices move with what you sell, so flooding the market for one
  product makes it worthless and diversity becomes the winning strategy. This is the cleanest way to
  make a big factory need many lines, and it makes every product stay relevant forever.
- **I15. Rate contracts and customers with reputation (L).** Instead of "deliver 200 plates", a
  client asks for a *rate* for a stretch of time, which cannot be met by hoarding, only by building
  capacity. Customers gain trust, pay better, and unlock their own goods. Turns contracts from pocket
  money into a business you are running.
- **I16. A rival factory (L).** An AI producer working the same map or the same market, competing for
  the same customers. It is the single player version of the neighbours concept, and it gives the
  numbers an opponent.
- **I17. An optional defense mode (XL).** Something comes for the factory and you must build
  production for defense as well as for sale. This changes the identity of the game, so it belongs
  behind a game mode toggle rather than in the default experience.

## V. Structural

- **I18. Factory compression (L).** Sketch the architecture notes' idea: measure a sub-layout's
  steady-state input and output with the headless sim, then emit a single machine that does the same
  thing. Factories inside factories. It also solves the performance ceiling, and it gives the endgame
  a whole new kind of puzzle: designing the best block to compress.
- **I19. A campaign of scenarios (M/L).** Hand authored maps with fixed seeds and fixed goals, some
  teaching a mechanic, some a hard puzzle. This is how most players would meet fluids, trains or
  robots, and it is also the leaderboard-ready content for later.

## How they combine

These are not nineteen separate games. The natural first act is **I1 + I2 + D1**: research becomes a
product, progression becomes a graph, and suddenly the factory is feeding progression instead of
money. The natural second act is **B9 + I7 + C1 + I9**: oil, fluids, power and heat, which is what
turns a late game into a real industry. The natural third act is **II**: robots and trains, which is
about scale. The world and economy ideas (III and IV) can be layered in any time, and each one is
independent.

---

# Part 3: more ideas

The third pass, asked for after science packs, the research tree and the chemistry layer were
confirmed. Leans into those two directions and then into mechanics not yet listed anywhere.
Effort: (S) small, (M) medium, (L) large, (XL) very large.

## VI. Research you manufacture (the confirmed direction, expanded)

- **R1. Discovery instead of a menu (L).** You do not pick recipes off a list. You feed items into a
  lab and *discover* what they make, so exploring the recipe space is the gameplay.
- **R2. A tech needs several different packs at once (M).** Forces breadth: one deep line cannot
  finish a research, so the whole factory is always part of the answer.
- **R3. Lab tiers and demanding packs (M).** A better lab eats more packs faster, and the last packs
  need a cold chain or an orbital lab, so late research needs real infrastructure.
- **R4. Modules you manufacture and insert (L). This is the big one after I1.** Speed, efficiency and
  productivity modules are *items*, built from their own chain and inserted into machines. It adds a
  whole production branch and the deepest optimisation layer in the genre.
- **R5. Beacons (M).** Area machines that boost everything around them, so dense layouts pay off.
- **R6. Research by doing (S).** A tech finishes once you have *produced* N of an item. Cheap to build
  and it makes ordinary production feel like progress.
- **R7. Mutually exclusive branches (M).** Two paths, you pick one and the other locks until late, so
  two runs genuinely cannot look the same.
- **R8. A procedurally generated tech graph (L).** The tree and its dependencies differ per seed, so
  what you can research first changes every run.
- **R9. A codex that fills in (S/M).** Every product, recipe and process you discover is recorded, so
  completing the book is a long-term goal.
- **R10. License a recipe from a customer (M).** A client you supply well teaches you their product,
  which ties the contract board to the tree.
- **R11. Prove a tech by building a line (M).** A research completes only once a working line for it
  exists, so theory and factory have to meet.

## VII. Chemistry and fluids, deeper

- **K1. Recipe variants by condition (L). This is the most interesting chemistry idea.** The same
  reactor run hot or cold, or at high or low pressure, yields different products. One machine becomes
  several processes, and the setting is a real decision.
- **K2. A distillation tower (M).** One input, several outputs split by cut point, so the tower is a
  routing puzzle and its outputs must be balanced.
- **K3. Heat integration (L).** Exothermic reactions give off heat, endothermic ones consume it, so
  heat becomes a resource to route. It makes the chemistry layer a system.
- **K4. Fluids in barrels (M). This is chemistry without pipes.** Liquids move as ordinary items in
  barrels, filled and emptied by machines. Far cheaper than a pipe network, and it keeps the belt
  game intact.
- **K5. Mixing and blending (M).** Two liquids in a tank become a third, and the wrong ratio makes
  waste.
- **K6. Flow limits and pumps (M).** A fluid's rate falls with distance, so pumps and pipe sizing
  matter the way belt capacity does.
- **K7. A reactor with sliders (L).** Temperature and pressure are player set parameters on the
  building, so the same machine is many recipes depending on how it is tuned.
- **K8. Waste that must be treated (M).** Processes leak waste to treat, store or vent, and ignoring
  it costs money or reputation.
- **K9. Nuclear power (L).** A chain reaction that needs cooling and can overheat, giving the heat
  network a spectacular reason to exist.
- **K10. Catalysts that wear out (M).** A catalyst is consumed slowly and must be regenerated, so the
  loop has to balance itself.
- **K11. A bottling and packaging chain (S/M).** Fill, ship, empty, with the empty container
  returning, so containers are a loop rather than a cost.

## VIII. Mechanical depth not yet listed

- **M1. Machine operating modes (M).** A machine runs fast but wasteful, or slow but efficient, so
  the same building plays two ways and the player sets the policy.
- **M2. Retooling cost (M).** Switching a recipe costs time or money, so committing to a layout is a
  real decision and mistakes have weight.
- **M3. Sockets on machines (M).** A standard interface for modules and attachments, which is what
  makes R4, R5 and M1 possible without touching every behavior.
- **M4. Terraforming (M).** Fill water, break cliffs, plant forest, as researched abilities.
- **M5. Selling designs (M).** A manufactured blueprint or design is itself an export good, which
  gives the blueprint library a role in the economy.
- **M6. A manager layer (M).** Hire managers who take over a line's logistics decisions, so the late
  game becomes supervising a system rather than touching every building.
- **M7. Machine families (M).** A researched Mk2 Smelter as its own def, so progress shows up as
  better buildings, not only higher levels.
- **M8. Shared reactions (M).** Several input machines feeding one reactor, where the recipe needs a
  ratio across types, so a whole sub-factory is one process.

## Where I would start, given the confirmed taste

1. **I1 + R2 + R6, research as a product.** Science packs from several chains, and some techs that
   finish by producing rather than spending. This is the intended direction, and R6 is cheap enough
   to prove the feel immediately. No tech graph yet: `I2` is undecided.
2. **R4 + M3, modules and sockets.** Once research is a product, modules are the natural next product
   to research, and they deepen every machine at once.
3. **B9, the chemistry layer: pipes, pumps and tanks.** That is the wanted version of fluids. Then
   K1 and K2 (condition based variants, a distillation tower), which are what make it feel like
   chemistry rather than another belt. `K4`, fluids in barrels, is only an optional cheap on-ramp for
   a casual player: suggested, never chosen, and not the plan.
