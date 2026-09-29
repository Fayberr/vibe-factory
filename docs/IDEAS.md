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

---

## A. The map as a place (spatial depth)

- **A1. Ore deposits by seed (M).** Ore comes in patches of varying richness, so a drill must sit
  on one. This single change makes land, rim depots, long belts and mergers all matter at once,
  because distance becomes a real cost. It also relaxes the extractor `limit`, since placement
  becomes the constraint. The real cost is that the balance tool needs an ore supply model.
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
- **B4. A void or sink building (data).** An incinerator or flare stack that destroys items for a
  small cost. The escape valve that makes B1 safe.
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
  retention value on this page.
- **F2. History graphs (S).** Money, income and the production of a chosen item over time, from a
  ring buffer. Progress you can see.
- **F3. A bottleneck ranking (M).** "Your five biggest constraints right now", each one clickable to
  fly there. It is the question every player asks, and `EntityStatus` already computes the inputs.
- **F4. Alerts (S/M).** A dismissible log of jams, low power, full depots and contract deadlines,
  with a fly-to.
- **F5. The diagnostics overlay (S).** One hotkey lights up every machine that is not working,
  coloured by reason. The cheapest large win here, because the data already exists.
- **F6. A throughput view (M).** Show belts near capacity and mergers as gates.
- **F7. Self set targets (S/M).** "Keep 200 circuits in stock", with an indicator when you are under.

## G. Ownership and expression

- **G1. A named blueprint library (S).** Save, name, sort and reuse across sessions, plus a
  shareable string. Also a precondition for sharing with a friend later.
- **G2. Blueprint mirror and multi-rotate (S).** The clipboard already exists.
- **G3. Signs and labels (S).** Placeable text so a big factory explains itself.
- **G4. Paint or per line colours (S).** Cheap ownership.
- **G5. Photo mode and timelapse (S/M).** Hide the UI, orbit freely, and export the factory growing
  from `EditHistory`.
- **G6. A statistics page (S).** Totals per item, playtime, tiers and personal records.

## H. Convenience that removes friction

- **H1. Run until (S).** Fast forward until a building finishes, a tier unlocks, or money hits a
  target.
- **H2. A finer speed control (S).** A slider from x0.5 to x10 instead of fixed steps.
- **H3. Copy settings between machines (S).** Paste a recipe to a whole selection.
- **H4. Mass upgrade a line (S).** Partly exists through the upgrade tool.
- **H5. Search and filter in the build menu (S).**
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
