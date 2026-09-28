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
        Assert.IsType<ProcessorParams>(c.Buildings["alloy_forge"].Params);
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
