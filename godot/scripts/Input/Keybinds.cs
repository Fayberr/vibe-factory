using System.Collections.Generic;
using System.Linq;
using Godot;

namespace FactorySim.Client;

/// <summary>
/// Rebindable keys. Every single-key action reads its key from here; Settings → Controls changes
/// them and GameSettings.Keys stores the ones that differ from the defaults. Fixed on purpose:
/// Esc, the hotbar numbers, Ctrl shortcuts, Delete/Backspace, arrow keys (pan) and PageUp/PageDown
/// (build height), so a game can always be steered back.
/// </summary>
public static class Keybinds
{
    public sealed record Action(string Id, string Label, Key Default, string Group);

    public static readonly Action[] All =
    {
        new("pan_forward", "Pan forward", Key.W, "Camera"),
        new("pan_back", "Pan back", Key.S, "Camera"),
        new("pan_left", "Pan left", Key.A, "Camera"),
        new("pan_right", "Pan right", Key.D, "Camera"),
        new("reset_view", "Reset camera angle", Key.Home, "Camera"),

        new("rotate", "Rotate (with Shift: back)", Key.R, "Building"),
        new("pick", "Pick hovered building", Key.F, "Building"),
        new("height_up", "Build height up", Key.E, "Building"),
        new("height_down", "Build height down", Key.Q, "Building"),
        new("hide_above", "Hide above build height", Key.Tab, "Building"),
        new("upgrade", "Upgrade", Key.U, "Building"),
        new("delete_tool", "Delete tool", Key.X, "Building"),
        new("move", "Move selection", Key.M, "Building"),
        new("copy", "Copy and paste", Key.C, "Building"),
        new("select_tool", "Select tool", Key.V, "Building"),

        new("build_menu", "Build menu", Key.B, "Windows"),
        new("progress", "Progress", Key.P, "Windows"),
        new("orders", "Orders", Key.O, "Windows"),
        new("research", "Research", Key.L, "Windows"),
        new("stats", "Statistics", Key.I, "Windows"),
        new("bottlenecks", "Bottlenecks", Key.K, "Windows"),
        new("diagnostics", "Mark waiting buildings", Key.J, "Windows"),
        new("planner", "Planner", Key.H, "Windows"),
        new("history", "History", Key.T, "Windows"),
        new("game_menu", "Game menu", Key.G, "Windows"),
        new("help", "Help", Key.F1, "Windows"),
        new("pause", "Pause the factory", Key.Space, "Windows"),
    };

    private static readonly Dictionary<string, Key> Keys = All.ToDictionary(a => a.Id, a => a.Default);

    /// <summary>Raised after any binding changes (labels and hints refresh).</summary>
    public static event System.Action? Changed;

    public static Key Get(string id) => Keys.TryGetValue(id, out var k) ? k : Key.None;

    /// <summary>Whether the key event is the one bound to <paramref name="id"/>.</summary>
    public static bool Is(InputEventKey ev, string id) => ev.Keycode != Key.None && ev.Keycode == Get(id);

    /// <summary>Whether the key bound to <paramref name="id"/> is held down.</summary>
    public static bool Held(string id) => Get(id) is var k && k != Key.None && Input.IsKeyPressed(k);

    /// <summary>The key's name for hints and tooltips ("R", "Tab", "F1").</summary>
    public static string Name(string id) => NameOf(Get(id));

    public static string NameOf(Key key) => key == Key.None ? "(none)" : OS.GetKeycodeString(key);

    /// <summary>Keys that can't be bound: they have fixed jobs.</summary>
    public static bool Reserved(Key key) => key is Key.Escape or Key.Delete or Key.Backspace or Key.Enter or Key.KpEnter
        or Key.Shift or Key.Ctrl or Key.Alt or Key.Meta or Key.Capslock
        or Key.Up or Key.Down or Key.Left or Key.Right or Key.Pageup or Key.Pagedown
        or (>= Key.Key0 and <= Key.Key9);

    /// <summary>
    /// Binds <paramref name="key"/> to <paramref name="id"/>. An action that had this key takes the
    /// old key of <paramref name="id"/> in exchange, so no key does two things. False if reserved.
    /// </summary>
    public static bool Set(string id, Key key)
    {
        if (Reserved(key) || !Keys.ContainsKey(id)) return false;
        var old = Keys[id];
        foreach (var other in Keys.Where(p => p.Key != id && p.Value == key).Select(p => p.Key).ToList())
            Keys[other] = old;
        Keys[id] = key;
        Changed?.Invoke();
        return true;
    }

    public static void ResetAll()
    {
        foreach (var a in All) Keys[a.Id] = a.Default;
        Changed?.Invoke();
    }

    /// <summary>Loads stored bindings (action → key name); unknown or reserved entries are ignored.</summary>
    public static void Load(Dictionary<string, string>? stored)
    {
        foreach (var a in All) Keys[a.Id] = a.Default;
        if (stored != null)
            foreach (var (id, name) in stored)
            {
                var key = OS.FindKeycodeFromString(name);
                if (Keys.ContainsKey(id) && key != Key.None && !Reserved(key)) Keys[id] = key;
            }
        // A stored set could leave two actions on one key; the later one goes back to its default.
        var used = new HashSet<Key>();
        foreach (var a in All)
            if (!used.Add(Keys[a.Id])) Keys[a.Id] = used.Add(a.Default) ? a.Default : Key.None;
        Changed?.Invoke();
    }

    /// <summary>The bindings that differ from the defaults, for saving.</summary>
    public static Dictionary<string, string> Stored() =>
        All.Where(a => Keys[a.Id] != a.Default).ToDictionary(a => a.Id, a => NameOf(Keys[a.Id]));
}
