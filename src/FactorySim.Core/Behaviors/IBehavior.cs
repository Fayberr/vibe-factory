using FactorySim.Content;
using FactorySim.View;

namespace FactorySim.Behaviors;

/// <summary>
/// Logic for a family of buildings. Behaviors are stateless singletons: all mutable
/// data lives in the entity's <see cref="Entity.State"/> (plain, serializable) and all
/// tuning in <see cref="BuildingDef.Params"/>. Many defs can share one behavior with
/// different params (a smelter and an alloy forge are both "processor").
/// </summary>
public interface IBehavior
{
    /// <summary>Key referenced by <see cref="BuildingDef.Behavior"/>.</summary>
    string Name { get; }

    Type ParamsType { get; }
    Type StateType { get; }

    /// <summary>Validates a def's params/ports and resolves content references. Throws <see cref="ContentException"/>.</summary>
    void Bind(BuildingDef def, ContentRegistry content);

    object CreateState(BuildingDef def);

    /// <summary>Advance one tick. Entities tick downstream-first (see Topology).</summary>
    void Tick(TickContext ctx, Entity entity);

    /// <summary>
    /// Offer an item arriving through input port <paramref name="port"/>. Return true to take
    /// ownership. <paramref name="overflow"/> is how far (in conveyor units) the item already
    /// travelled past the sender's edge this tick, so belts keep exact speed across
    /// tiles, or <see cref="TickContext.Waiting"/> if the item was already waiting before this tick.
    /// </summary>
    bool TryAccept(TickContext ctx, Entity entity, ItemStack item, int port, int overflow);

    /// <summary>Items visible on this entity for rendering (progress 0..1 along its path).</summary>
    void CollectItems(Entity entity, List<ItemView> into);

    EntityStatus GetStatus(Entity entity);

    /// <summary>Inspector details (buffers, recipe, totals). Purely informational.</summary>
    void Describe(Entity entity, List<InfoLine> into);

    /// <summary>Level-up track used when a building def doesn't specify one.</summary>
    UpgradeTrack DefaultUpgrade(BuildingDef def);

    /// <summary>The player's choice for this building (a machine's recipe), or null for automatic.</summary>
    string? Selection(Entity entity);

    /// <summary>Applies a choice (null = automatic). Returns an error, or null on success.</summary>
    string? Select(Entity entity, string? option);

    /// <summary>
    /// Sorting rules on the outputs (a splitter's filters), one per output in the def's output order,
    /// or null when none is set. A copy: changing it changes nothing.
    /// </summary>
    IReadOnlyList<string?>? Filters(Entity entity);

    /// <summary>Sets the rule of output number <paramref name="output"/> (null = anything). Returns an error, or null on success.</summary>
    string? SetFilter(Entity entity, int output, string? filter);

    /// <summary>
    /// Checks state just restored from a save against today's content and repairs what no longer
    /// fits (a filter for an item that was removed), adding a warning for each repair.
    /// </summary>
    void CheckLoaded(Entity entity, List<string> warnings);
}

/// <summary>Typed convenience base: casts params/state once so implementations stay readable.</summary>
public abstract class Behavior<TParams, TState> : IBehavior
    where TParams : class, new()
    where TState : class, new()
{
    public abstract string Name { get; }
    public Type ParamsType => typeof(TParams);
    public Type StateType => typeof(TState);

    public void Bind(BuildingDef def, ContentRegistry content) => Bind(def, (TParams)def.Params!, content);

    public virtual object CreateState(BuildingDef def) => new TState();

    public void Tick(TickContext ctx, Entity entity) => Tick(ctx, entity, P(entity), S(entity));

    public bool TryAccept(TickContext ctx, Entity entity, ItemStack item, int port, int overflow) =>
        TryAccept(ctx, entity, P(entity), S(entity), item, port, overflow);

    public void CollectItems(Entity entity, List<ItemView> into) => CollectItems(entity, P(entity), S(entity), into);

    public EntityStatus GetStatus(Entity entity) => GetStatus(entity, P(entity), S(entity));

    public void Describe(Entity entity, List<InfoLine> into) => Describe(entity, P(entity), S(entity), into);

    public virtual UpgradeTrack DefaultUpgrade(BuildingDef def) => new() { MaxLevel = 10, SpeedPerLevel = 0.25 };

    public string? Selection(Entity entity) => Selection(entity, P(entity), S(entity));

    public string? Select(Entity entity, string? option) => Select(entity, P(entity), S(entity), option);

    public IReadOnlyList<string?>? Filters(Entity entity) => Filters(entity, P(entity), S(entity));

    public string? SetFilter(Entity entity, int output, string? filter) => SetFilter(entity, P(entity), S(entity), output, filter);

    public void CheckLoaded(Entity entity, List<string> warnings) => CheckLoaded(entity, P(entity), S(entity), warnings);

    protected virtual string? Selection(Entity e, TParams p, TState s) => null;

    protected virtual string? Select(Entity e, TParams p, TState s, string? option) =>
        option == null ? null : $"{e.Def.Name} has nothing to choose";

    protected virtual IReadOnlyList<string?>? Filters(Entity e, TParams p, TState s) => null;

    protected virtual string? SetFilter(Entity e, TParams p, TState s, int output, string? filter) =>
        filter == null ? null : $"{e.Def.Name} can't sort items";

    protected virtual void CheckLoaded(Entity e, TParams p, TState s, List<string> warnings) { }

    protected virtual void Bind(BuildingDef def, TParams p, ContentRegistry content) { }

    protected virtual void Tick(TickContext ctx, Entity e, TParams p, TState s) { }

    protected virtual bool TryAccept(TickContext ctx, Entity e, TParams p, TState s, ItemStack item, int port, int overflow) => false;

    protected virtual void CollectItems(Entity e, TParams p, TState s, List<ItemView> into) { }

    protected virtual EntityStatus GetStatus(Entity e, TParams p, TState s) => default;

    protected virtual void Describe(Entity e, TParams p, TState s, List<InfoLine> into) { }

    protected static TParams P(Entity e) => (TParams)e.Def.Params!;
    protected static TState S(Entity e) => (TState)e.State;

    protected static void Require(bool condition, BuildingDef def, string message)
    {
        if (!condition) throw new ContentException($"Building '{def.Id}' ({def.Behavior}): {message}");
    }
}

/// <summary>For behaviors without per-entity state.</summary>
public sealed class NoState
{
}
