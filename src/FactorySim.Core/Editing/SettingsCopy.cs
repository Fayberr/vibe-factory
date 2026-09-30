namespace FactorySim.Editing;

/// <summary>A building's settings, copied: what it makes (or its sign text) and its output filters.</summary>
/// <param name="DefId">Settings go only onto buildings of this same type.</param>
/// <param name="Selection">The player's choice (a recipe, a sign's text); null for automatic.</param>
/// <param name="Filters">One sorting rule per output, or null when the building has none.</param>
public sealed record CopiedSettings(string DefId, string? Selection, IReadOnlyList<string?>? Filters);

/// <summary>
/// Copy settings between buildings (idea H3): take one machine's recipe, one splitter's filters or one
/// sign's text and put it on other buildings of the same type. Pasting is the existing
/// <see cref="SelectRecipe"/> and <see cref="SetFilter"/> commands, one per change, so a caller that runs them
/// inside one <see cref="EditHistory"/> group gets a single undo step and saves need nothing new.
///
/// To remove: this file, <c>SettingsCopyTests.cs</c>, <c>CopySettings</c>/<c>PasteSettings</c> and their keys in
/// the client's <c>BuildController</c>, and the settings row of the Manage window.
/// </summary>
public static class SettingsCopy
{
    /// <summary>What <paramref name="e"/> is set to, or null when it has nothing to copy (a belt, a depot).</summary>
    public static CopiedSettings? Capture(Entity e)
    {
        if (!HasSettings(e)) return null;
        return new CopiedSettings(e.Def.Id, e.Behavior.Selection(e), e.Behavior.Filters(e)?.ToList());
    }

    /// <summary>Whether a building has anything the player sets: a choice or output filters.</summary>
    public static bool HasSettings(Entity e) =>
        e.Def.Params is Behaviors.ProcessorParams or Behaviors.SignParams
        || (e.Def.Params is Behaviors.RouterParams && e.Def.OutputPorts.Count > 1);

    /// <summary>
    /// The commands that make every building in <paramref name="targets"/> of the copied type match the copy.
    /// Buildings of another type, and settings that already match, get none.
    /// </summary>
    public static List<Command> Paste(CopiedSettings copied, IEnumerable<Entity> targets)
    {
        var commands = new List<Command>();
        foreach (var e in targets)
        {
            if (e.Def.Id != copied.DefId) continue;
            if (e.Behavior.Selection(e) != copied.Selection) commands.Add(new SelectRecipe(e.Pos, copied.Selection));
            if (e.Def.Params is not Behaviors.RouterParams) continue;
            // A splitter with no filter set reports none at all: read that as every output taking anything.
            var filters = e.Behavior.Filters(e);
            for (int i = 0; i < e.Def.OutputPorts.Count; i++)
            {
                string? want = copied.Filters != null && i < copied.Filters.Count ? copied.Filters[i] : null;
                string? have = filters != null && i < filters.Count ? filters[i] : null;
                if (have != want) commands.Add(new SetFilter(e.Pos, i, want));
            }
        }
        return commands;
    }

    /// <summary>
    /// Pastes through <paramref name="history"/> as one undo step. Returns how many buildings changed, or the first
    /// error (a recipe the target cannot run, say) with the ones before it kept.
    /// </summary>
    public static (int Changed, string? Error) Apply(EditHistory history, CopiedSettings copied, IEnumerable<Entity> targets)
    {
        int changed = 0;
        string? error = null;
        history.BeginGroup();
        try
        {
            foreach (var e in targets.ToList())
            {
                var commands = Paste(copied, new[] { e });
                if (commands.Count == 0) continue;
                bool ok = true;
                foreach (var c in commands)
                {
                    var r = history.Execute(c);
                    if (r.Ok) continue;
                    error ??= r.Error;
                    ok = false;
                }
                if (ok) changed++;
            }
        }
        finally { history.EndGroup(); }
        return (changed, error);
    }

    /// <summary>How many of <paramref name="targets"/> a paste would change.</summary>
    public static int Changes(CopiedSettings copied, IEnumerable<Entity> targets) =>
        targets.Count(e => Paste(copied, new[] { e }).Count > 0);
}
