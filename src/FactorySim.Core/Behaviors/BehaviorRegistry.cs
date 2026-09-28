namespace FactorySim.Behaviors;

/// <summary>Maps behavior names used in content to implementations. Register your own to extend.</summary>
public sealed class BehaviorRegistry
{
    private readonly Dictionary<string, IBehavior> _behaviors = new();

    public static BehaviorRegistry CreateDefault()
    {
        var r = new BehaviorRegistry();
        r.Register(new ConveyorBehavior());
        r.Register(new MinerBehavior());
        r.Register(new ProcessorBehavior());
        r.Register(new SellerBehavior());
        r.Register(new RouterBehavior());
        return r;
    }

    public BehaviorRegistry Register(IBehavior behavior)
    {
        _behaviors[behavior.Name] = behavior;
        return this;
    }

    public bool TryGet(string name, out IBehavior behavior) => _behaviors.TryGetValue(name, out behavior!);

    public IEnumerable<string> Names => _behaviors.Keys;
}
