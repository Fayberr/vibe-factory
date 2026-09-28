using System.Text.Json.Serialization;
using FactorySim.Content;
using FactorySim.View;

namespace FactorySim.Behaviors;

public sealed class ProcessorParams
{
    /// <summary>Recipe ids this machine can run, in priority order.</summary>
    public string[] Recipes { get; init; } = Array.Empty<string>();

    /// <summary>Max buffered units per input item type before the machine refuses more.</summary>
    public int InputCapacity { get; init; } = 10;

    /// <summary>Max units waiting in the output buffer before crafting pauses.</summary>
    public int OutputCapacity { get; init; } = 10;

    [JsonIgnore] internal RecipeDef[] ResolvedRecipes { get; set; } = Array.Empty<RecipeDef>();
    [JsonIgnore] internal HashSet<string> Ingredients { get; set; } = new();
}

public sealed class InputBuffer
{
    public long Count { get; set; }
    public BigNum ValueSum { get; set; }
}

public sealed class ProcessorState
{
    // Keys are never removed, so enumeration order (and therefore save output) is
    // a pure function of history — required for byte-identical save/load determinism.
    public Dictionary<string, InputBuffer> Inputs { get; set; } = new();
    public string? Recipe { get; set; }
    public double Work { get; set; }
    public List<ItemStack> Output { get; set; } = new();
}

/// <summary>
/// Recipe-driven machine: buffers ingredients from any input port, crafts, and emits
/// output bundles. Refining, smelting and multi-ingredient merging (alloys, parts) are
/// all this behavior with different recipes. Output value derives from consumed input
/// value × recipe multiplier, so upstream upgrades carry through the chain.
/// </summary>
public sealed class ProcessorBehavior : Behavior<ProcessorParams, ProcessorState>
{
    public override string Name => "processor";

    protected override void Bind(BuildingDef def, ProcessorParams p, ContentRegistry content)
    {
        Require(p.Recipes.Length > 0, def, "needs at least one recipe.");
        Require(def.InputPorts.Count >= 1 && def.OutputPorts.Count >= 1, def, "needs input and output ports.");
        Require(p.InputCapacity > 0 && p.OutputCapacity > 0, def, "capacities must be > 0.");
        var resolved = new List<RecipeDef>();
        foreach (var id in p.Recipes)
        {
            Require(content.Recipes.TryGetValue(id, out var r), def, $"unknown recipe '{id}'.");
            resolved.Add(r!);
        }
        p.ResolvedRecipes = resolved.ToArray();
        p.Ingredients = resolved.SelectMany(r => r.Inputs).Select(i => i.Item).ToHashSet();
    }

    protected override void Tick(TickContext ctx, Entity e, ProcessorParams p, ProcessorState s)
    {
        // Output crafted on an earlier tick counts as waiting at the edge.
        if (s.Output.Count > 0 && ctx.Push(e, e.Def.OutputPorts[0], s.Output[0], TickContext.Waiting)) s.Output.RemoveAt(0);

        var recipe = PickRecipe(p, s);
        if (recipe == null)
        {
            s.Recipe = null;
            s.Work = 0;
            return;
        }
        if (s.Recipe != recipe.Id)
        {
            s.Recipe = recipe.Id;
            s.Work = 0;
        }

        long waiting = 0;
        foreach (var o in s.Output) waiting += o.Count;
        if (waiting >= p.OutputCapacity) return; // output backed up: pause, don't bank work

        s.Work += ctx.Stat(StatIds.MachineSpeed);
        if (s.Work < recipe.Ticks) return;

        long crafts = Math.Min((long)(s.Work / recipe.Ticks), MaxCrafts(recipe, s));
        s.Work = Math.Min(s.Work - crafts * recipe.Ticks, recipe.Ticks);
        Craft(ctx, e, recipe, s, crafts);
    }

    private static RecipeDef? PickRecipe(ProcessorParams p, ProcessorState s)
    {
        // Stick with the current recipe while it is still possible (avoids thrashing).
        foreach (var r in p.ResolvedRecipes)
            if (r.Id == s.Recipe && MaxCrafts(r, s) > 0) return r;
        foreach (var r in p.ResolvedRecipes)
            if (MaxCrafts(r, s) > 0) return r;
        return null;
    }

    private static long MaxCrafts(RecipeDef r, ProcessorState s)
    {
        long max = long.MaxValue;
        foreach (var input in r.Inputs)
        {
            long have = s.Inputs.TryGetValue(input.Item, out var buf) ? buf.Count : 0;
            max = Math.Min(max, have / input.Count);
        }
        return max;
    }

    private static void Craft(TickContext ctx, Entity e, RecipeDef recipe, ProcessorState s, long crafts)
    {
        BigNum consumedValue = BigNum.Zero;
        foreach (var input in recipe.Inputs)
        {
            var buf = s.Inputs[input.Item];
            long units = input.Count * crafts;
            BigNum share = units == buf.Count ? buf.ValueSum : buf.ValueSum * units / buf.Count;
            buf.Count -= units;
            buf.ValueSum = buf.Count == 0 ? BigNum.Zero : buf.ValueSum - share;
            consumedValue += share;
        }

        long outUnits = 0;
        foreach (var o in recipe.Outputs) outUnits += o.Count * crafts;
        BigNum unitValue = consumedValue * recipe.ValueMultiplier / outUnits;

        long maxStack = ctx.MaxStackSize;
        foreach (var o in recipe.Outputs)
        {
            for (long left = o.Count * crafts; left > 0;)
            {
                long n = Math.Min(left, maxStack);
                var item = ctx.CreateItem(o.Item, n, unitValue);
                ctx.RecordProduced(e, item);
                s.Output.Add(item);
                left -= n;
            }
        }

        ctx.Emit(new CraftCompleted(ctx.Tick, e.Id, recipe.Id, crafts));
    }

    protected override bool TryAccept(TickContext ctx, Entity e, ProcessorParams p, ProcessorState s, ItemStack item, int port, int overflow)
    {
        if (!p.Ingredients.Contains(item.Type)) return false;
        if (!s.Inputs.TryGetValue(item.Type, out var buf)) s.Inputs[item.Type] = buf = new InputBuffer();
        if (buf.Count >= p.InputCapacity) return false;
        buf.Count += item.Count;
        buf.ValueSum += item.TotalValue;
        return true;
    }

    protected override EntityStatus GetStatus(Entity e, ProcessorParams p, ProcessorState s)
    {
        var recipe = s.Recipe == null ? null : Array.Find(p.ResolvedRecipes, r => r.Id == s.Recipe);
        return recipe == null
            ? new EntityStatus(false, 0, "idle")
            : new EntityStatus(true, (float)Math.Min(1, s.Work / recipe.Ticks), recipe.Id);
    }
}
