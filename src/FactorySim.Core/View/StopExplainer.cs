using FactorySim.Behaviors;
using FactorySim.Content;

namespace FactorySim.View;

/// <summary>Why a building is not working, in words: one headline and a few lines of what to do.</summary>
/// <param name="Headline">What is wrong ("Waiting for Iron Ore").</param>
/// <param name="Lines">Where the missing item comes from, or where the stuck one could go.</param>
/// <param name="Items">The items the explanation is about, for pictures.</param>
public sealed record StopExplanation(string Headline, IReadOnlyList<string> Lines, IReadOnlyList<string> Items);

/// <summary>
/// The "why is this stopped" explainer (idea H6). The status line already says "waiting for iron ore";
/// this adds where iron ore comes from (the buildings that make it, how many you have and whether they
/// run, or the tier that unlocks them) and, for a full output, what could take it. It only reads the
/// world, looks at a machine's state directly rather than through a new behavior hook, and knows the
/// processor, drill and lab; any other waiting building gets its status line as the headline.
///
/// To remove: this file, <c>StopExplainerTests.cs</c>, and the "Why it waits" section of the client's
/// Manage window.
/// </summary>
public static class StopExplainer
{
    /// <summary>At most this many items explained at once, so the section stays short.</summary>
    public const int MaxItems = 3;

    /// <summary>
    /// Null when the building works, or waits in a way that is not a problem (a belt, a depot). Pass
    /// <paramref name="reason"/> to explain a longer view (the <see cref="BottleneckTracker"/>'s, say) rather than
    /// this instant, which on an underfed line flips between working and waiting every few ticks.
    /// </summary>
    public static StopExplanation? Explain(World world, Entity e, IdleReason? reason = null)
    {
        var status = e.Behavior.GetStatus(e);
        var why = reason ?? (status.Working ? IdleReason.None : status.Idle);
        if (why == IdleReason.None) return null;
        // The status line is only the fallback headline, and says "making ..." while the machine happens to run.
        if (status.Working) status = status with { Detail = why == IdleReason.Starved ? "waiting for input" : "its output backs up" };
        var content = world.Content;
        return why == IdleReason.Starved ? Starved(world, content, e, status) : Blocked(world, content, e, status);
    }

    private static StopExplanation Starved(World world, ContentRegistry content, Entity e, EntityStatus status)
    {
        List<string> wanted;
        string headline;
        switch (e.State, e.Def.Params)
        {
            case (ProcessorState s, ProcessorParams p):
                long Have(string item) => s.Inputs.TryGetValue(item, out var buf) ? buf.Count : 0;
                var recipe = s.Chosen != null ? Array.Find(p.ResolvedRecipes, r => r.Id == s.Chosen) : null;
                recipe ??= Array.Find(p.ResolvedRecipes, r => r.Inputs.Any(i => Have(i.Item) > 0));
                if (recipe == null)
                {
                    // On automatic with nothing in: any recipe would do, so name what each one starts from,
                    // those this factory already makes first (an underfed line between two items lands here too).
                    wanted = p.ResolvedRecipes.SelectMany(r => r.Inputs).Select(i => i.Item).Distinct()
                        .OrderByDescending(i => content.BuildingList.Any(d => Makes(d, i) && world.CountOf(d.Id) > 0)).ToList();
                    headline = "Waiting for input";
                }
                else
                {
                    wanted = recipe.Inputs.Where(i => Have(i.Item) < i.Count).Select(i => i.Item).ToList();
                    // Between two items of an underfed line nothing is short at this instant; name the recipe's inputs.
                    if (wanted.Count == 0) wanted = recipe.Inputs.Select(i => i.Item).ToList();
                    headline = $"Waiting for {Join(wanted.Select(i => Name(content, i)))}";
                }
                break;
            case (LabState s, LabParams p):
                wanted = p.Items.Where(i => s.Held.GetValueOrDefault(i) <= 0).ToList();
                headline = $"Waiting for {Join(wanted.Select(i => Name(content, i)))}";
                break;
            default:
                return new StopExplanation(Sentence(status.Detail ?? "Waiting"), Array.Empty<string>(), Array.Empty<string>());
        }

        wanted = wanted.Where(content.Items.ContainsKey).Take(MaxItems).ToList();
        var lines = wanted.Select(item => SourceLine(world, content, item)).ToList();
        if (wanted.Count > 0 && lines.All(l => l.StartsWith("Made by", StringComparison.Ordinal) && l.Contains("running")))
            lines.Add("If they run and it still waits, check that a belt reaches this machine.");
        return new StopExplanation(headline, lines, wanted);
    }

    private static StopExplanation Blocked(World world, ContentRegistry content, Entity e, EntityStatus status)
    {
        var stuck = e.State switch
        {
            ProcessorState s => s.Output.Select(o => o.Type).Distinct().ToList(),
            MinerState { Output: { } o } => new List<string> { o.Type },
            _ => new List<string>(),
        };
        stuck = stuck.Where(content.Items.ContainsKey).Take(MaxItems).ToList();
        if (stuck.Count == 0)
        {
            var only = new List<string>();
            if (e.Def.OutputPorts.Count > 1) only.Add("Connect a belt to every output, or set its filters so each item has somewhere to go.");
            return new StopExplanation(Sentence(status.Detail ?? "Output blocked"), only, stuck);
        }

        var lines = new List<string>();
        foreach (string item in stuck)
        {
            var users = Unlocked(world, content.BuildingList.Where(d => Uses(d, item))).Select(d => d.Name).Distinct().Take(3).ToList();
            lines.Add(users.Count > 0
                ? $"{Name(content, item)} can go to a depot to be sold, or into {Join(users, "or")}."
                : $"{Name(content, item)} can go to a depot to be sold.");
        }
        lines.Add("Check that the belt from its output leads somewhere and is not backed up.");
        return new StopExplanation($"Output full: nothing takes its {Join(stuck.Select(i => Name(content, i)))}", lines, stuck);
    }

    /// <summary>Where an item comes from, as seen from this factory.</summary>
    private static string SourceLine(World world, ContentRegistry content, string item)
    {
        string name = Name(content, item);
        var makers = content.BuildingList.Where(d => Makes(d, item)).ToList();
        if (makers.Count == 0) return $"Nothing builds {name}: it comes from elsewhere (an order or research).";

        var open = Unlocked(world, makers).ToList();
        if (open.Count == 0)
        {
            var first = makers.OrderBy(d => d.Tier).First();
            string tier = first.Tier < content.Tiers.Count ? $" ({content.Tiers[first.Tier].Name})" : "";
            return $"{name} is made by {first.Name}, which unlocks at tier {first.Tier}{tier}.";
        }

        var built = open.Where(d => world.CountOf(d.Id) > 0).ToList();
        if (built.Count == 0)
            return $"{name} is made by {Join(open.Take(3).Select(d => d.Name), "or")}. You have none yet: build one and belt it here.";

        var parts = built.Take(3).Select(d =>
        {
            var all = world.Entities.Where(x => x.Def == d).ToList();
            int running = all.Count(x => x.Behavior.GetStatus(x).Working);
            string state = running == all.Count ? (all.Count == 1 ? "running" : "all running")
                : running == 0 ? (all.Count == 1 ? "stopped too" : "all stopped too")
                : $"{running} running";
            return $"{d.Name} (you have {all.Count}, {state})";
        });
        return $"Made by {Join(parts)}.";
    }

    private static bool Makes(BuildingDef def, string item) => def.Params switch
    {
        ProcessorParams p => p.ResolvedRecipes.Any(r => r.Outputs.Any(o => o.Item == item)),
        MinerParams m => m.Item == item,
        _ => false,
    };

    private static bool Uses(BuildingDef def, string item) => def.Params switch
    {
        ProcessorParams p => p.ResolvedRecipes.Any(r => r.Inputs.Any(i => i.Item == item)),
        LabParams l => l.Items.Contains(item),
        _ => false,
    };

    private static IEnumerable<BuildingDef> Unlocked(World world, IEnumerable<BuildingDef> defs) =>
        defs.Where(d => world.Sandbox || d.Tier <= world.UnlockedTier);

    private static string Name(ContentRegistry content, string item) => content.Items.TryGetValue(item, out var def) ? def.Name : item;

    private static string Sentence(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private static string Join(IEnumerable<string> parts, string last = "and")
    {
        var list = parts.ToList();
        return list.Count switch
        {
            0 => "",
            1 => list[0],
            _ => string.Join(", ", list.Take(list.Count - 1)) + $" {last} " + list[^1],
        };
    }
}
