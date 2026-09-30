using FactorySim.Content;
using FactorySim.View;

namespace FactorySim.Behaviors;

public sealed class SignParams
{
    /// <summary>Longest text a sign holds, in characters.</summary>
    public int MaxLength { get; set; } = 40;
}

public sealed class SignState
{
    public string Text { get; set; } = "";
}

/// <summary>
/// A sign (idea G3): a building that does nothing but show a line of text, so a big factory can say
/// what each part is for. It has no ports and never works or waits, so bottlenecks and alerts ignore it.
///
/// The text is the sign's "selection", the same slot a machine's recipe choice uses, so the existing
/// <c>SelectRecipe</c> command sets it and undo, redo, saves and blueprints all carry it without new code.
///
/// To remove: this file, its line in <c>BehaviorRegistry</c>, the <c>sign</c> building in
/// <c>base.json</c>, <c>SignTests.cs</c>, and in the client the sign model (<c>ModelFactorySign.cs</c>),
/// the label in <c>WorldView</c> and the text box in the Manage window. Signs in a save then fail to
/// load as an unknown building, the same as any removed building.
/// </summary>
public sealed class SignBehavior : Behavior<SignParams, SignState>
{
    public override string Name => "sign";

    protected override void Bind(BuildingDef def, SignParams p, ContentRegistry content)
    {
        Require(p.MaxLength > 0, def, "maxLength must be positive.");
    }

    public override UpgradeTrack DefaultUpgrade(BuildingDef def) => new() { MaxLevel = 1 };

    protected override EntityStatus GetStatus(Entity e, SignParams p, SignState s) => default;

    protected override string? Selection(Entity e, SignParams p, SignState s) => s.Text.Length == 0 ? null : s.Text;

    protected override string? Select(Entity e, SignParams p, SignState s, string? option)
    {
        string text = Clean(option);
        if (text.Length > p.MaxLength) return $"A sign holds up to {p.MaxLength} characters";
        s.Text = text;
        return null;
    }

    protected override void CheckLoaded(Entity e, SignParams p, SignState s, List<string> warnings)
    {
        string text = Clean(s.Text);
        if (text.Length > p.MaxLength)
        {
            text = text[..p.MaxLength];
            warnings.Add($"A sign's text was cut to {p.MaxLength} characters");
        }
        s.Text = text;
    }

    protected override void Describe(Entity e, SignParams p, SignState s, List<InfoLine> into)
    {
        into.Add(new InfoLine("Text", s.Text.Length == 0 ? "(empty)" : s.Text));
    }

    /// <summary>One line, no control characters, no spaces at the ends. Null is empty.</summary>
    public static string Clean(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var chars = text.Select(c => char.IsControl(c) ? ' ' : c).ToArray();
        return new string(chars).Trim();
    }
}
