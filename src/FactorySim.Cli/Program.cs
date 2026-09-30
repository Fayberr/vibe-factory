using System.Diagnostics;
using System.Globalization;
using FactorySim;
using FactorySim.Cli;
using FactorySim.Content;
using FactorySim.Persistence;
using FactorySim.Samples;

// Headless host: runs the exact same simulation the Godot client runs, without any engine.
//   dotnet run --project src/FactorySim.Cli              → demo walkthrough
//   dotnet run --project src/FactorySim.Cli -- bench 200 → throughput benchmark with 200 lines
//   dotnet run --project src/FactorySim.Cli -- balance   → balance report (see BalanceCommand)
//   dotnet run --project src/FactorySim.Cli -- inspect save.json → report over a real save

var content = ContentRegistry.LoadDefault();
var mode = args.Length > 0 ? args[0] : "demo";

switch (mode)
{
    case "demo":
        RunDemo(content);
        break;
    case "bench":
        RunBench(content, args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 100);
        break;
    case "balance":
        return BalanceCommand.Run(args[1..]);
    case "inspect":
        return InspectCommand.Run(args[1..]);
    default:
        Console.WriteLine("usage: FactorySim.Cli [demo | bench <lines> | balance ... | inspect <save.json>]");
        return 1;
}
return 0;

static void RunDemo(ContentRegistry content)
{
    var sim = Simulation.CreateNew(content, startingMoney: 0);
    DemoLayout.Build(sim);
    sim.Events.Enabled = false;

    Console.WriteLine("== Demo factory ==");
    Console.WriteLine("Line A crosses over line B on a bridge (ramp up, z=1 belt, ramp down).\n");
    Console.WriteLine(AsciiView.RenderLayer(sim.World, 0));
    Console.WriteLine(AsciiView.RenderLayer(sim.World, 1));
    Console.WriteLine("M/C/L extractors  S smelter  R press  W sawmill  w workshop  P polisher  $ depot  U/D ramps  : ramp upper half\n");

    sim.Step(120 * Simulation.TicksPerSecond);
    PrintStats("After 2 minutes", sim);

    // Level up individual buildings with the earnings, through commands like any player would.
    foreach (var ((x, y), level) in new[] { ((0, 4), 3), ((4, 4), 2), ((11, 4), 3), ((0, 13), 2) })
    {
        var cell = DemoLayout.CellOf(sim, x, y);
        var e = sim.World.EntityAt(cell)!;
        var r = sim.Execute(new SetBuildingLevels(new[] { new LevelChange(cell, level) }));
        Console.WriteLine($"  {e.Def.Name,-14} {cell} → level {level}: {(r.Ok ? "ok" : r.Error)}");
    }
    sim.Step(120 * Simulation.TicksPerSecond);
    PrintStats("After 2 more minutes", sim);

    // Save/load determinism: continue both copies and compare.
    var json = SaveSystem.Serialize(sim);
    var loaded = SaveSystem.Deserialize(json, content).Simulation;
    sim.Step(600);
    loaded.Step(600);
    bool identical = SaveSystem.Serialize(sim) == SaveSystem.Serialize(loaded);
    Console.WriteLine($"Save size {json.Length / 1024.0:F1} KB, reload continues identically: {identical}\n");

    var report = sim.CatchUp(8 * 3600);
    Console.WriteLine("== Offline for 8 hours ==");
    Console.WriteLine($"  simulated {report.SimulatedTicks} ticks, extrapolated {report.ExtrapolatedTicks} ticks");
    Console.WriteLine($"  earned {report.Earned.Format()} at {report.IncomePerSecond.Format()}/s");
    foreach (var i in report.Items)
        Console.WriteLine($"  {i.Item,-14} made {i.MadePerMinute,6:0.#}/min  sold {i.SoldPerMinute,6:0.#}/min  earned {i.Earned.Format()}");
    if (report.Rewards > BigNum.Zero) Console.WriteLine($"  orders and goals earned {report.Rewards.Format()}");
    foreach (var p in report.Problems)
        Console.WriteLine($"  waiting: {p.Count}x {p.Building} {p.Reason.ToString().ToLowerInvariant()} ({p.Detail}) {p.IdleShare * 100:0}% of the time, e.g. #{p.ExampleId} at {p.ExamplePos}");
    PrintStats("After catch-up", sim);
}

static void PrintStats(string title, Simulation sim)
{
    var w = sim.World;
    Console.WriteLine($"== {title} (t={w.Tick / (double)Simulation.TicksPerSecond:F0}s) ==");
    Console.WriteLine($"  money {w.Money.Format()}  income {w.Stats.IncomePerSecond().Format()}/s  lifetime {w.Stats.TotalEarned.Format()}");
    foreach (var (item, count) in w.Stats.Sold.OrderBy(kv => kv.Key)) Console.WriteLine($"  sold {item,-12} {count,8}");
    Console.WriteLine();
}

static void RunBench(ContentRegistry content, int lines)
{
    var sim = Simulation.CreateNew(content, 0);
    sim.World.Sandbox = true;
    sim.World.Bounds = new GridBounds(new GridPos(0, 0, 0), new GridPos(40, lines * 2 + 2, 4));
    sim.Events.Enabled = false;

    for (int i = 0; i < lines; i++)
    {
        int y = i * 2;
        int x = 0;
        void Put(string def) => sim.Execute(new PlaceBuilding(def, new GridPos(x++, y, 0), Dir.East));
        Put("iron_miner");
        for (int k = 0; k < 15; k++) Put("conveyor");
        Put("smelter");
        for (int k = 0; k < 15; k++) Put("conveyor");
        Put("seller");
    }

    sim.Step(200); // fill belts
    const int ticks = 2000;
    var sw = Stopwatch.StartNew();
    sim.Step(ticks);
    sw.Stop();

    double tps = ticks / sw.Elapsed.TotalSeconds;
    Console.WriteLine($"{sim.World.EntityCount} entities: {tps:F0} ticks/s ({tps / Simulation.TicksPerSecond:F0}× real time), " +
                      $"{sw.Elapsed.TotalMilliseconds / ticks * 1000:F0} µs/tick");
}
