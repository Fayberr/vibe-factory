using System.Collections.Generic;
using Godot;

namespace FactorySim.Client;

/// <summary>
/// Sound and music. Every sound is a recorded clip from godot/audio (credits in
/// audio/CREDITS.md): effects from Kenney's CC0 packs, music by Kevin MacLeod (CC BY 4.0).
///  • Three buses under Master: Music, Effects (world, positional) and Interface.
///  • A named sound is every file sfx/{name}_N.ogg; one is picked at random and its pitch
///    varied a little so repeats don't sound mechanical.
///  • Per-sound cool-downs keep busy factories from turning into noise (a belt drag of 30
///    tiles makes a few taps, not 30).
///  • Music plays a playlist with crossfades: calm piano on the menu, lounge in the game.
/// </summary>
public partial class AudioManager : Node
{
    public const string MusicBus = "Music";
    public const string EffectsBus = "Effects";
    public const string InterfaceBus = "Interface";

    private sealed record Spec(string Bus, float VolumeDb, float PitchJitter, double CoolDown);

    private static readonly Dictionary<string, Spec> Specs = new()
    {
        ["place_light"] = new(EffectsBus, -6, 0.08f, 0.05),
        ["place_heavy"] = new(EffectsBus, -3, 0.06f, 0.06),
        ["remove"] = new(EffectsBus, -4, 0.08f, 0.06),
        ["rotate"] = new(EffectsBus, -8, 0.05f, 0.03),
        ["upgrade"] = new(EffectsBus, -6, 0.03f, 0.08),
        ["sell"] = new(EffectsBus, -12, 0.1f, 0.35),
        ["tier_unlocked"] = new(InterfaceBus, -2, 0, 1),
        ["contract_done"] = new(InterfaceBus, -3, 0, 0.5),
        ["milestone"] = new(InterfaceBus, -4, 0, 0.5),
        ["step_done"] = new(InterfaceBus, -8, 0, 0.3),
        ["click"] = new(InterfaceBus, -10, 0.04f, 0.03),
        ["open"] = new(InterfaceBus, -14, 0.03f, 0.05),
        ["close"] = new(InterfaceBus, -14, 0.03f, 0.05),
        ["error"] = new(InterfaceBus, -10, 0, 0.2),
        ["toggle"] = new(InterfaceBus, -12, 0.03f, 0.05),
        ["select"] = new(InterfaceBus, -14, 0.05f, 0.05),
    };

    private static readonly string[] MenuPlaylist = { "dreamer" };
    private static readonly string[] GamePlaylist = { "airport_lounge", "chill_wave", "lobby_time" };

    private readonly Dictionary<string, AudioStream[]> _clips = new();
    private readonly Dictionary<string, double> _lastPlayed = new();
    private readonly List<AudioStreamPlayer> _flat = new();
    private readonly List<AudioStreamPlayer3D> _spatial = new();
    private readonly RandomNumberGenerator _rng = new();
    private AudioStreamPlayer _musicA = null!, _musicB = null!;
    private string[] _playlist = GamePlaylist;
    private int _track = -1;
    private double _now;

    public override void _Ready()
    {
        EnsureBus(MusicBus);
        EnsureBus(EffectsBus);
        EnsureBus(InterfaceBus);
        for (int i = 0; i < 8; i++)
        {
            var p = new AudioStreamPlayer { Bus = InterfaceBus };
            AddChild(p);
            _flat.Add(p);
        }
        for (int i = 0; i < 16; i++)
        {
            var p = new AudioStreamPlayer3D
            {
                Bus = EffectsBus,
                UnitSize = 14,
                MaxDistance = 90,
                AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance,
                PanningStrength = 0.6f,
            };
            AddChild(p);
            _spatial.Add(p);
        }
        _musicA = new AudioStreamPlayer { Bus = MusicBus, VolumeDb = -80 };
        _musicB = new AudioStreamPlayer { Bus = MusicBus, VolumeDb = -80 };
        AddChild(_musicA);
        AddChild(_musicB);
        _musicA.Finished += NextTrack;
        _musicB.Finished += NextTrack;
    }

    public static AudioManager? Instance { get; private set; }

    /// <summary>World sounds (placing, selling…) are muted behind the title screen.</summary>
    public bool WorldSounds { get; set; } = true;

    public override void _EnterTree() => Instance = this;

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    public override void _Process(double delta) => _now += delta;

    /// <summary>Plays the game's sounds for simulation events and failed actions, and clicks for every button.</summary>
    public void Hook(SimHost host)
    {
        host.Failed += () => Play("error");
        host.EventRaised += ev =>
        {
            if (!WorldSounds) return;
            var world = host.Sim.World;
            switch (ev)
            {
                case EntityPlaced p when world.Content.Buildings.TryGetValue(p.DefId, out var def):
                    bool light = world.Content.Behaviors.Get(def.Behavior) is Behaviors.ConveyorBehavior or Behaviors.RouterBehavior;
                    Play(light ? "place_light" : "place_heavy", GridMapping.CellFloor(p.Pos));
                    break;
                case EntityRemoved r:
                    Play("remove", GridMapping.CellFloor(r.Pos));
                    break;
                case EntityReoriented o:
                    Play("rotate", GridMapping.CellFloor(o.Pos));
                    break;
                case EntityLevelChanged l when world.GetEntity(l.EntityId) is { } up:
                    Play("upgrade", GridMapping.CellFloor(up.Pos));
                    break;
                case ItemSold s when world.GetEntity(s.EntityId) is { } seller:
                    Play("sell", GridMapping.CellFloor(seller.Pos));
                    break;
                case TierUnlocked:
                    Play("tier_unlocked");
                    break;
                case ContractCompleted:
                    Play("contract_done");
                    break;
                case MilestoneReached:
                    Play("milestone");
                    break;
            }
        };
        GetTree().NodeAdded += node =>
        {
            if (node is CheckButton or CheckBox) ((BaseButton)node).Toggled += _ => Play("toggle");
            else if (node is BaseButton b) b.Pressed += () => Play("click");
        };
    }

    private static void EnsureBus(string name)
    {
        if (AudioServer.GetBusIndex(name) >= 0) return;
        int index = AudioServer.BusCount;
        AudioServer.AddBus(index);
        AudioServer.SetBusName(index, name);
        AudioServer.SetBusSend(index, "Master");
    }

    /// <summary>Sets the four volume sliders (0..1, linear).</summary>
    public void ApplyVolumes(GameSettings s)
    {
        SetVolume("Master", s.MasterVolume);
        SetVolume(MusicBus, s.MusicVolume);
        SetVolume(EffectsBus, s.EffectsVolume);
        SetVolume(InterfaceBus, s.InterfaceVolume);
    }

    private static void SetVolume(string bus, float linear)
    {
        int i = AudioServer.GetBusIndex(bus);
        if (i < 0) return;
        AudioServer.SetBusMute(i, linear <= 0.001f);
        AudioServer.SetBusVolumeDb(i, Mathf.LinearToDb(Mathf.Max(linear, 0.001f)));
    }

    private AudioStream[] Clips(string name)
    {
        if (_clips.TryGetValue(name, out var clips)) return clips;
        var list = new List<AudioStream>();
        for (int i = 0; ResourceLoader.Exists($"res://audio/sfx/{name}_{i}.ogg"); i++)
            if (GD.Load<AudioStream>($"res://audio/sfx/{name}_{i}.ogg") is { } stream) list.Add(stream);
        return _clips[name] = list.ToArray();
    }

    /// <summary>Plays a named sound; with <paramref name="at"/> it comes from that spot in the world.</summary>
    public void Play(string name, Vector3? at = null, float volumeDb = 0)
    {
        if (!Specs.TryGetValue(name, out var spec)) return;
        if (_lastPlayed.TryGetValue(name, out var last) && _now - last < spec.CoolDown) return;
        var clips = Clips(name);
        if (clips.Length == 0) return;
        _lastPlayed[name] = _now;
        var stream = clips[_rng.RandiRange(0, clips.Length - 1)];
        float pitch = 1 + _rng.RandfRange(-spec.PitchJitter, spec.PitchJitter);

        if (at is { } position && spec.Bus == EffectsBus)
        {
            var p = Free(_spatial);
            p.Stream = stream;
            p.GlobalPosition = position;
            p.VolumeDb = spec.VolumeDb + volumeDb;
            p.PitchScale = pitch;
            p.Play();
            return;
        }
        var flat = Free(_flat);
        flat.Bus = spec.Bus;
        flat.Stream = stream;
        flat.VolumeDb = spec.VolumeDb + volumeDb;
        flat.PitchScale = pitch;
        flat.Play();
    }

    /// <summary>Named sounds that have no clip in audio/sfx (for tests; should be empty).</summary>
    public List<string> MissingSounds()
    {
        var missing = new List<string>();
        foreach (var name in Specs.Keys)
            if (Clips(name).Length == 0) missing.Add(name);
        return missing;
    }

    /// <summary>The music track playing now, or null.</summary>
    public string? Track => _musicA.Playing || _musicB.Playing ? _playlist[_track] : null;

    /// <summary>An idle player, or the one that started longest ago.</summary>
    private static T Free<T>(List<T> pool) where T : Node
    {
        foreach (var p in pool)
            if (p is AudioStreamPlayer { Playing: false } or AudioStreamPlayer3D { Playing: false }) return p;
        var oldest = pool[0];
        pool.RemoveAt(0);
        pool.Add(oldest);
        return oldest;
    }

    // ---- Music -----------------------------------------------------------------

    public void PlayMenuMusic() => SwitchPlaylist(MenuPlaylist);

    public void PlayGameMusic() => SwitchPlaylist(GamePlaylist);

    private void SwitchPlaylist(string[] playlist)
    {
        if (_playlist == playlist && (_musicA.Playing || _musicB.Playing)) return;
        _playlist = playlist;
        _track = playlist.Length > 1 ? _rng.RandiRange(0, playlist.Length - 1) - 1 : -1;
        NextTrack();
    }

    /// <summary>Crossfades to the next track of the playlist.</summary>
    private void NextTrack()
    {
        if (_playlist.Length == 0) return;
        _track = (_track + 1) % _playlist.Length;
        var stream = GD.Load<AudioStream>($"res://audio/music/{_playlist[_track]}.ogg");
        if (stream == null) return;
        var (incoming, outgoing) = _musicA.Playing ? (_musicB, _musicA) : (_musicA, _musicB);
        incoming.Stream = stream;
        incoming.VolumeDb = -40;
        incoming.Play();
        var tween = CreateTween().SetParallel();
        tween.TweenProperty(incoming, "volume_db", 0f, 2.5);
        if (outgoing.Playing)
        {
            tween.TweenProperty(outgoing, "volume_db", -40f, 2.5);
            tween.Chain().TweenCallback(Callable.From(outgoing.Stop));
        }
    }
}
