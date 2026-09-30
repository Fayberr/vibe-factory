using FactorySim.View;

namespace FactorySim;

/// <summary>What an <see cref="Alert"/> is about.</summary>
public enum AlertKind
{
    /// <summary>Buildings that were working now mostly wait for input.</summary>
    Stopped,

    /// <summary>Buildings that were working now mostly cannot get rid of their output.</summary>
    Jammed,

    /// <summary>An open order has little time left.</summary>
    OrderEnding,

    /// <summary>An order ran out of time.</summary>
    OrderExpired,

    /// <summary>A production target (idea F7) stayed under for <see cref="AlertLog.TargetSeconds"/>.</summary>
    TargetMissed,
}

/// <summary>One entry in the <see cref="AlertLog"/>.</summary>
public sealed class Alert
{
    public int Id { get; init; }
    public long Tick { get; init; }
    public AlertKind Kind { get; init; }

    /// <summary>The building type, for <see cref="AlertKind.Stopped"/> and <see cref="AlertKind.Jammed"/>.</summary>
    public string Building { get; init; } = "";

    /// <summary>The status text of the example building ("needs iron ore", "output full").</summary>
    public string Detail { get; init; } = "";

    /// <summary>The building to fly to, or 0.</summary>
    public int ExampleId { get; init; }

    /// <summary>The order, for the order kinds.</summary>
    public Contract? Order { get; init; }

    /// <summary>The item, for <see cref="AlertKind.TargetMissed"/>.</summary>
    public string Item { get; init; } = "";

    /// <summary>Every building the alert is about; <see cref="Resolved"/> once all of them work again.</summary>
    public HashSet<int> Buildings { get; } = new();

    public int Count => Kind is AlertKind.Stopped or AlertKind.Jammed ? Buildings.Count : 1;

    /// <summary>The problem went away on its own: the buildings run again, or the order was delivered.</summary>
    public bool Resolved { get; internal set; }
}

/// <summary>
/// Alerts (idea F4): a short, dismissible log of things that went wrong while you looked elsewhere. A building
/// that was working and then did not work at all for <see cref="StoppedSeconds"/> (a slow, underfed machine
/// is the Bottlenecks window's business, not an alert), an order about to run out, and an order that did.
/// Buildings that never worked yet (still being built up) raise nothing, several buildings of one type that
/// stop together make one entry, and a building that recovers and stops again within
/// <see cref="RepeatSeconds"/> is not reported twice.
///
/// Like <see cref="BottleneckTracker"/> it only reads the world and is not saved. A client calls
/// <see cref="Observe"/> after advancing the simulation, passes events to <see cref="OnEvent"/>, and calls
/// <see cref="Reset"/> when the world is replaced. The numbers to tune are the constants below.
///
/// To remove: this file, <c>AlertTests.cs</c>, the <c>Alerts</c> property and its three calls in
/// <c>SimHost</c>, and the Alerts window and its key in the client.
/// </summary>
public sealed class AlertLog
{
    /// <summary>Entries kept; the oldest go first.</summary>
    public const int Capacity = 40;

    /// <summary>An order raises an alert when this part of its time is left.</summary>
    public const double OrderWarningShare = 0.2;

    /// <summary>A building that worked before is reported once it has not worked at all for this long.</summary>
    public const int StoppedSeconds = 30;

    /// <summary>A building that recovered is not reported again for this long.</summary>
    public const int RepeatSeconds = 120;

    /// <summary>A production target is reported once it has been under for this long without a break.</summary>
    public const int TargetSeconds = 60;

    private readonly List<Alert> _entries = new();
    private readonly Dictionary<int, long> _lastWorked = new();
    private readonly HashSet<int> _flagged = new();
    private readonly Dictionary<int, long> _lastAlerted = new();
    private readonly HashSet<int> _warnedOrders = new();
    private readonly Dictionary<string, long> _underSince = new();
    private readonly HashSet<string> _missedTargets = new();
    private int _nextId = 1;
    private long _lastTick = long.MinValue;

    /// <summary>Newest first.</summary>
    public IReadOnlyList<Alert> Entries => _entries;

    /// <summary>Entries added since <see cref="MarkSeen"/>, for a badge.</summary>
    public int Unseen { get; private set; }

    /// <summary>Changes whenever the list or an entry changes, so a view knows when to redraw.</summary>
    public int Version { get; private set; }

    public event Action<Alert>? Raised;

    public void MarkSeen() => Unseen = 0;

    public void Dismiss(int id)
    {
        if (_entries.RemoveAll(a => a.Id == id) > 0) Version++;
    }

    public void Clear()
    {
        _entries.Clear();
        Unseen = 0;
        Version++;
    }

    public void Reset()
    {
        Clear();
        _lastWorked.Clear();
        _flagged.Clear();
        _lastAlerted.Clear();
        _warnedOrders.Clear();
        _underSince.Clear();
        _missedTargets.Clear();
        _lastTick = long.MinValue;
    }

    /// <summary>Checks the buildings and orders, at most once per sampling stride.</summary>
    public void Observe(World world)
    {
        if (_lastTick != long.MinValue && world.Tick >= _lastTick && world.Tick - _lastTick < IdleSampler.Stride) return;
        _lastTick = world.Tick;
        ObserveBuildings(world);
        ObserveOrders(world);
        ObserveTargets(world);
    }

    public void OnEvent(SimEvent ev)
    {
        switch (ev)
        {
            case ContractExpired x:
                Add(new Alert { Id = _nextId++, Tick = x.Tick, Kind = AlertKind.OrderExpired, Order = x.Contract });
                break;
            case ContractCompleted c:
                foreach (var a in _entries.Where(a => a.Kind == AlertKind.OrderEnding && a.Order?.Id == c.Contract.Id && !a.Resolved))
                {
                    a.Resolved = true;
                    Version++;
                }
                break;
        }
    }

    private void ObserveBuildings(World world)
    {
        long now = world.Tick, stopped = StoppedSeconds * (long)Simulation.TicksPerSecond, repeat = RepeatSeconds * (long)Simulation.TicksPerSecond;
        var fresh = new Dictionary<(string Def, IdleReason Reason), List<(Entity E, EntityStatus Status)>>();
        foreach (var e in world.Entities)
        {
            var status = e.Behavior.GetStatus(e);
            if (status.Working)
            {
                _lastWorked[e.Id] = now;
                if (_flagged.Remove(e.Id)) _lastAlerted[e.Id] = now; // runs again; do not report it again straight away
                continue;
            }
            if (status.Idle == IdleReason.None || _flagged.Contains(e.Id)) continue;
            if (!_lastWorked.TryGetValue(e.Id, out long worked) || now - worked < stopped) continue; // never ran yet, or only just paused
            _flagged.Add(e.Id);
            if (_lastAlerted.TryGetValue(e.Id, out long last) && now - last < repeat) continue;
            if (!fresh.TryGetValue((e.Def.Id, status.Idle), out var list)) fresh[(e.Def.Id, status.Idle)] = list = new();
            list.Add((e, status));
        }

        foreach (var map in new IDictionary<int, long>[] { _lastWorked, _lastAlerted })
            foreach (int id in map.Keys.Where(id => world.GetEntity(id) == null).ToList()) map.Remove(id);
        _flagged.RemoveWhere(id => world.GetEntity(id) == null);

        foreach (var a in _entries)
            if (!a.Resolved && a.Kind is AlertKind.Stopped or AlertKind.Jammed && !a.Buildings.Any(_flagged.Contains))
            {
                a.Resolved = true;
                Version++;
            }

        foreach (var ((def, reason), list) in fresh.OrderBy(kv => kv.Key.Def, StringComparer.Ordinal).ThenBy(kv => kv.Key.Reason))
        {
            var alert = new Alert
            {
                Id = _nextId++,
                Tick = now,
                Kind = reason == IdleReason.Blocked ? AlertKind.Jammed : AlertKind.Stopped,
                Building = def,
                Detail = list[0].Status.Detail ?? "",
                ExampleId = list[0].E.Id,
            };
            foreach (var (e, _) in list)
            {
                alert.Buildings.Add(e.Id);
                _lastAlerted[e.Id] = now;
            }
            Add(alert);
        }
    }

    private void ObserveOrders(World world)
    {
        foreach (var c in world.Contracts.Open)
        {
            if (c.IsComplete || _warnedOrders.Contains(c.Id)) continue;
            long total = c.ExpiresAtTick - c.OfferedAtTick, left = c.ExpiresAtTick - world.Tick;
            if (total <= 0 || left > total * OrderWarningShare) continue;
            _warnedOrders.Add(c.Id);
            Add(new Alert { Id = _nextId++, Tick = world.Tick, Kind = AlertKind.OrderEnding, Order = c });
        }
        _warnedOrders.RemoveWhere(id => world.Contracts.Open.All(c => c.Id != id));
    }

    /// <summary>
    /// A target under for <see cref="TargetSeconds"/> raises one entry, resolved once the target is met again
    /// or removed. To remove: this method, its call, the two fields it uses and <see cref="AlertKind.TargetMissed"/>.
    /// </summary>
    private void ObserveTargets(World world)
    {
        long now = world.Tick, wait = TargetSeconds * (long)Simulation.TicksPerSecond;
        foreach (var r in ProductionTargets.ReadAll(world))
        {
            if (r.Status == TargetStatus.Met)
            {
                _underSince.Remove(r.Item);
                if (_missedTargets.Remove(r.Item)) ResolveTarget(r.Item);
            }
            if (r.Status != TargetStatus.Under) continue;
            if (!_underSince.TryGetValue(r.Item, out long since)) _underSince[r.Item] = since = now;
            if (now - since < wait || !_missedTargets.Add(r.Item)) continue;
            Add(new Alert
            {
                Id = _nextId++,
                Tick = now,
                Kind = AlertKind.TargetMissed,
                Item = r.Item,
                Detail = $"{ProductionTargets.Format(r.PerMinute)} of {ProductionTargets.Format(r.Target)} a minute",
            });
        }
        foreach (string item in _underSince.Keys.Where(i => !world.Targets.ContainsKey(i)).ToList()) _underSince.Remove(item);
        foreach (string item in _missedTargets.Where(i => !world.Targets.ContainsKey(i)).ToList())
        {
            _missedTargets.Remove(item);
            ResolveTarget(item);
        }
    }

    private void ResolveTarget(string item)
    {
        foreach (var a in _entries.Where(a => a.Kind == AlertKind.TargetMissed && a.Item == item && !a.Resolved))
        {
            a.Resolved = true;
            Version++;
        }
    }

    private void Add(Alert alert)
    {
        _entries.Insert(0, alert);
        if (_entries.Count > Capacity) _entries.RemoveRange(Capacity, _entries.Count - Capacity);
        Unseen++;
        Version++;
        Raised?.Invoke(alert);
    }
}
