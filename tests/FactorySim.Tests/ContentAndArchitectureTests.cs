using FactorySim.Behaviors;
using FactorySim.Content;

namespace FactorySim.Tests;

public class ContentAndArchitectureTests
{
    [Fact]
    public void Base_pack_loads_with_typed_params()
    {
        var c = TestUtil.Content;
        Assert.IsType<ConveyorParams>(c.Buildings["conveyor"].Params);
        Assert.IsType<ProcessorParams>(c.Buildings["blast_furnace"].Params);
        Assert.Equal(1.5, ((ConveyorParams)c.Buildings["polisher"].Params!).Effect!.ValueMultiplier);
        Assert.Equal("conveyor", c.BuildingList[0].Id);
    }

    [Fact]
    public void Later_packs_override_by_id()
    {
        var patch = ContentRegistry.ParsePack("""
            { "buildings": [ {
                "id": "conveyor", "name": "Fast Conveyor", "behavior": "conveyor",
                "ports": [ { "kind": "in", "side": "back" }, { "kind": "out", "side": "front" } ],
                "params": { "speed": 200, "spacing": 250 } } ] }
            """);
        var c = ContentRegistry.LoadDefault(null, patch);
        Assert.Equal(200, ((ConveyorParams)c.Buildings["conveyor"].Params!).Speed);
        Assert.Equal("conveyor", c.BuildingList[0].Id); // keeps original position
    }

    [Theory]
    [InlineData("""{ "buildings": [ { "id": "x", "behavior": "teleporter" } ] }""", "unknown behavior")]
    [InlineData("""{ "buildings": [ { "id": "x", "behavior": "miner", "ports": [ { "kind": "out", "side": "front" } ], "params": { "item": "unobtainium" } } ] }""", "unknown item")]
    [InlineData("""{ "buildings": [ { "id": "x", "behavior": "seller", "ports": [ { "kind": "in", "side": "back", "cell": [0, 0, 1] } ] } ] }""", "not in the footprint")]
    [InlineData("""{ "recipes": [ { "id": "r", "inputs": [ { "item": "iron_ore", "count": 1 } ], "outputs": [ { "item": "nope", "count": 1 } ] } ] }""", "unknown item")]
    public void Invalid_content_fails_with_a_clear_message(string json, string expected)
    {
        var ex = Assert.Throws<ContentException>(() => ContentRegistry.LoadDefault(null, ContentRegistry.ParsePack(json)));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void Core_assembly_depends_only_on_the_base_class_library()
    {
        // The simulation must stay engine-agnostic: no Godot, Unity, or UI references.
        var refs = typeof(Simulation).Assembly.GetReferencedAssemblies().Select(a => a.Name!).ToList();
        Assert.All(refs, name => Assert.True(
            name.StartsWith("System") || name is "netstandard" or "mscorlib",
            $"FactorySim.Core must not reference '{name}'."));
    }
}

public class TextStyleTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "FactorySim.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    [Fact]
    public void No_em_dashes_anywhere_in_the_game()
    {
        // House style: no em dashes in anything players read (content, messages, UI) or in the code behind it.
        var root = RepoRoot();
        var files = new[] { "src", "godot/scripts", "tests" }
            .SelectMany(d => Directory.EnumerateFiles(Path.Combine(root, d), "*.*", SearchOption.AllDirectories))
            .Where(f => f.EndsWith(".cs") || f.EndsWith(".json") || f.EndsWith(".md"))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Append(Path.Combine(root, "README.md"))
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "docs"), "*.md"));
        var offenders = files
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (f, i, line)))
            .Where(x => x.line.Contains((char)0x2014))
            .Select(x => $"{Path.GetRelativePath(root, x.f)}:{x.i + 1}")
            .ToList();
        Assert.True(offenders.Count == 0, "Em dash found at " + string.Join(", ", offenders));
    }
}
