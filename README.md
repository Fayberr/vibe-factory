# Vibe Factory

A modular factory/tycoon game about automation, resource pipelines and endless
progression. The game logic is a **deterministic, engine-agnostic C# simulation**.
**Godot 4** is one frontend for it; a headless CLI is another.

![The demo factory](docs/images/hero.jpg)

| Drag a belt across a line: it bridges itself | Manage a building, choose its recipe; windows side by side |
|---|---|
| ![Automatic bridge over a belt](docs/images/bridge.jpg) | ![Manage, Progress and Statistics windows](docs/images/windows.jpg) |
| **Build menu with rendered icons** | **Every building and item is generated in code** |
| ![Build menu](docs/images/build-menu.jpg) | ![Machine showcase](docs/images/showcase.jpg) |

Every 3D model is **generated in code**: beveled low-poly bodies, belt profiles swept
along curves, lattice towers, and smoking chimneys. Build-menu and hotbar icons are
rendered from those same models, so new content gets art and icons with no asset work.

The sound is the opposite: **every sound is a real recording**. Placing, removing,
upgrading and selling come from Kenney's CC0 packs (positional, with a little pitch
variation so repeats don't sound mechanical), and chilled lounge music by Kevin MacLeod
plays underneath. See [Credits](#credits).

**A full game around it:** a title screen with your factory running behind it, five save
slots (autosave, play time, last played), settings (volumes for music, effects and
interface, fullscreen, vsync, graphics quality, interface size, autosave interval), a
pause menu, a tutorial, and a long progression of eight tiers, customer orders and 26
goals.

## Repository layout

Assemblies and namespaces keep the code name `FactorySim`; the game is called Vibe Factory.

```
src/FactorySim.Core/     Simulation: grid, transport, machines, economy, blueprints, undo, saves, offline.
                         Plain .NET 8. No engine references (a test enforces this).
src/FactorySim.Cli/      Headless host: demo walkthrough, ASCII view, benchmark.
tests/FactorySim.Tests/  xUnit tests for the core (belt physics, splitting/merging, editing, determinism…).
godot/                   Godot 4.7 (.NET) client. Presentation and input only.
  scripts/Visual/          procedural models (MeshBuilder, ModelFactory), world view, shaders
  scripts/Input/           camera, build tools, ghost previews
  scripts/UI/              HUD, build menu, windows, title and pause menus, icons, thumbnails
  scripts/Audio/           sound effects and music (AudioManager)
  audio/                   recorded sounds and music (see audio/CREDITS.md)
  scripts/Dev/             scripted end-to-end UI test
docs/ARCHITECTURE.md     How the pieces fit, and where to extend them.
```

## Quick start

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download). The client needs
**Godot 4.7 .NET** (the "mono" download).

```bash
dotnet test                                   # core test suite
dotnet run --project src/FactorySim.Cli       # headless demo: ASCII layers, stats, save/load, offline catch-up
```

**Download (Windows):** the newest build is always the
[latest release](https://github.com/Fayberr/vibe-factory/releases/latest), direct link:
[VibeFactory-Windows.zip](https://github.com/Fayberr/vibe-factory/releases/latest/download/VibeFactory-Windows.zip).

**New to it?** A short tutorial walks you through your first factory (drill, belt, smelter,
depot, first upgrade) the first time you play. Reopen it any time from the Game menu (`G`).

**Play from source:** open `godot/project.godot` in Godot 4.7 .NET and press Play. The
title screen has *Continue*, *New factory* (tick *Start with the example factory* for a
ready-made factory), *Load factory*, *Settings* and *Credits*. Saves live in five slots
under Godot's user folder (`user://saves/`); older single saves move into slot 1.

## Building controls

Building is designed to be fast from the keyboard. Press `F1` in game for this table.

| Keys | Action |
|---|---|
| `1` to `0` | Hotbar building (press again to put it away) |
| `B` | Build menu. Hover a building and press `1` to `0` to put it on the hotbar |
| LMB | Place / select. Dropping a polisher, splitter or machine on a belt replaces that belt |
| Drag (building) | Lay a line. It forms an L, belts orient and curve themselves, and a belt dragged across another line bridges over it |
| Drag (selecting) | Box select. `Shift` adds, `Ctrl` removes |
| `R` / `Shift+R` | Rotate the placement, the selection, or the hovered building |
| `F` | Pick the hovered building (type, rotation and height) |
| `E` / `Q`, `PageUp` / `PageDown`, `Shift+wheel` | Build height up / down (the ladder next to the hotbar shows it) |
| `Tab` | Hide everything above the build height |
| `U` | Upgrade the selection, or open the upgrade tool (click, drag a box, `Shift`-click a whole belt line) |
| `X` | Delete tool (click or drag a box) · `Del` deletes the selection |
| `M` | Move the selection (keeps items on belts; `E`/`Q` lifts or lowers it) |
| `C` · `Ctrl+C` / `Ctrl+V` / `Ctrl+X` | Copy & paste selection · copy / paste / cut |
| `Ctrl+Z` / `Ctrl+Y` | Undo / redo (a dragged line is one step) |
| `Ctrl+A` | Select all |
| `Esc` / right-click | Cancel the tool, then clear the selection |
| `WASD`, MMB drag · RMB drag · wheel | Pan · orbit · zoom toward the cursor |
| `P` · `O` · `I` · `G` · `F1` | Progress (tiers, limits, goals) · orders · statistics · game menu · help. Windows can be open together; drag them by the title bar, `Esc` closes the last one |
| `Space` | Pause or resume the factory (you can keep building while it is paused) |
| `Esc` (nothing to cancel) | Pause menu: resume, save, settings, quit to the title screen or to the desktop |

**Heights.** Everything is built at the current build height, shown on the ladder next
to the hotbar and next to the cursor. `G` is the ground plate: nothing can go below it.
Belts one level up are bridges and get support pillars automatically. You rarely need
to change height by hand. Drag a belt straight across another line and it builds the
ramp up, the bridge and the ramp down itself. Place a *Ramp Up* and the build height
follows it up. A *Ramp Down* always lands on the ground it stands on.

A tooltip next to the cursor says what a click will do and what it costs. Ghost previews
show where items enter (blue) and leave (orange) a building.

**Manage window.** Click a building to manage it: picture, description, level, value,
speed and status, a big **Upgrade** button (with the price) and **Delete**. Machines show
a grid of everything they can make. Pick one and the machine only takes that recipe's
ingredients, or leave it on *Automatic*. The chosen item shows its parts, value and
time. Select several machines of one kind to upgrade or set them all at once. Copies keep
their choice.

## Progression and balance

- **Tiers.** Eight tiers (Basics, Workshop, Industry, Petrochemicals, Electronics,
  Robotics, Aerospace, Space). Each needs lifetime earnings plus a price, unlocks new
  extractors and machines, grows the plot and raises build limits. The factory card
  always shows the next goal; `P` opens the details. The late game adds bauxite,
  aluminium, drones, rocket fuel and satellites, and ends at a launch complex.
- **Orders.** Customers post up to three orders (`O`): deliver a quantity of one product
  before the deadline for about twice its value on top of the normal sale. Orders ask
  for things you can already make, sized to your current income, and newer products are
  asked for more often. Don't like one? Swap it for a small fee.
- **Goals.** 26 milestones (first sale, 50 conveyors, a level 10 building, 10 orders,
  first robot, first satellite, a trillion earned…), each with a cash reward. The Progress
  window shows the next four with progress bars.
- **Per-building upgrades.** There are no global upgrades. Every building has its own
  level: drills and machines get faster (machines also add a little value), belts get
  faster up to level 5, market depots pay more. Ten drills means ten upgrades. Levels
  show as coloured trims (bronze, silver, gold, cyan, violet) and are kept by copy/paste.
- **Raw resources sell for 25%.** Processing is what pays: an ingot sells for 8× what its
  ore fetches raw, and a robot is worth about 1600 iron ore.
- **Build limits.** Extractors and depots are capped per tier (for example 4 iron drills
  and 2 depots at the start, more with every tier). Belts and machines are unlimited.

## Testing

```bash
dotnet test                                                     # core tests (102)

# Godot client (headless): build, then a quick smoke run of the demo factory
godot --headless --path godot --build-solutions --quit
godot --headless --path godot -- --smoke

# Scripted end-to-end UI test: injects real mouse/keyboard input (line drag, undo,
# box select, copy/paste, delete, move, pipette, replace-on-place, auto-bridge, build
# height, upgrades, Manage window, windows, pause, title screen, audio) and checks the
# results. Needs a display (e.g. xvfb-run).
# Add --shots=/abs/dir to save screenshots.
godot --path godot -- --ui-test

# Every building (some upgraded) and every item shape in one scene, for checking models.
godot --path godot -- --smoke --showcase --screenshot=/abs/showcase.png
# Screenshot options: --wait=seconds, --view=yaw,pitch,distance,x,z, --select=x,y (opens
# the Manage window for that cell), --windows (opens Progress and Statistics),
# --orders (opens Progress and Orders), --tutorial (an empty factory at the tutorial's
# first step), --pause (opens the pause menu), --title (the title screen; add
# --menu=settings|new|load|credits to open one of its windows).
```

## Continuous integration and downloads

`.github/workflows/build.yml` runs on every push to `main`:

1. Runs the unit tests (`dotnet test`) and builds the client (`dotnet build godot -c Release`).
2. Exports the Windows build headlessly with Godot 4.7.2 .NET on Linux, using preset
   **Windows Desktop** in `godot/export_presets.cfg`, into `build/`.
3. Uploads `VibeFactory-Windows.zip` as a workflow artifact and publishes it as the
   repository's **Latest** release (tag `latest-build`, named after the run number and
   commit). The previous one is deleted first, so the repo page always shows exactly one
   current build. The latest push from any of those branches wins.

The export is self-contained (it bundles the .NET runtime). Unzip and run `VibeFactory.exe`,
keeping the `.pck` and the `data_*` folder next to it. Godot's C# export needs a solution
file. It lives in `godot/sln/` (project setting `dotnet/project/solution_directory`), which
keeps `godot/` itself to a single project file so `dotnet build godot` works.

Export locally with the same command (export templates for 4.7.2 installed):

```bash
cd godot && godot --headless --export-release "Windows Desktop" ../build/VibeFactory.exe
```

## Adding content

Most content is data. `src/FactorySim.Core/Content/Data/base.json` defines tiers,
items (value, `raw`), recipes and buildings (footprint, ports, behavior, params, tier,
`limit`, `upgrade` track, and `group`/`replaces` for dropping one building onto another).
A building's `meta.model` picks its procedural model (`belt`, `ramp`, `splitter`,
`merger`, `drill`, `treefarm`, `quarry`, `pump`, `furnace`, `forge`, `sawmill`, `press`,
`refinery`, `assembler`, `polisher`, `depot`), and `meta.accent` tints it. A new machine that
reuses an existing behavior and model therefore needs no code at all. Extra packs
layer on top and override by id:

```csharp
var content = ContentRegistry.LoadDefault(null, ContentRegistry.ParsePack(File.ReadAllText("my_pack.json")));
```

New *kinds* of behavior are a class deriving from `Behavior<TParams, TState>`. New
*looks* are a case in `godot/scripts/Visual/ModelFactory.cs`. See
[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Credits

- **Music:** "Dreamer", "Airport Lounge", "Chill Wave" and "Lobby Time" by Kevin MacLeod
  (incompetech.com). Licensed under Creative Commons: By Attribution 4.0 License,
  http://creativecommons.org/licenses/by/4.0/
- **Sound effects:** [Kenney](https://www.kenney.nl) (Interface Sounds, Impact Sounds,
  Casino Audio, Music Jingles), CC0 1.0.
- **Engine:** [Godot](https://godotengine.org) 4.7 (.NET).

Which clip is used for what, and how the files were converted:
[godot/audio/CREDITS.md](godot/audio/CREDITS.md). The same credits are in the game
(title screen, *Credits*).
