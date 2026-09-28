using Godot;

namespace FactorySim.Client;

/// <summary>
/// Title screen → playing ⇄ paused. On the title screen the demo factory runs behind the menu
/// (never saved) with the camera slowly circling; playing hands the screen to the HUD and the
/// build tools; the pause menu freezes the whole scene tree except the menus and the music.
/// </summary>
public partial class GameFlow : Node, IMenuActions
{
    public SimHost Host = null!;
    public Hud Hud = null!;
    public BuildController Tools = null!;
    public CameraRig Camera = null!;
    public WorldView View = null!;
    public MenuLayer Menus = null!;
    public AudioManager Audio = null!;
    public GameSettings Settings = null!;

    /// <summary>False for scripted runs, which never write the player's settings.</summary>
    public bool PersistSettings = true;

    private bool _playing;
    private double _saveSettingsIn = -1;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Tools.EscapeIdle += Pause;
        Hud.OpenSettings = () => Menus.OpenSettings();
        Hud.QuitToMenu = QuitToMenu;
        var root = GetTree().Root;
        root.SizeChanged += () => Settings.ApplyScale(root);
    }

    public void ShowTitle()
    {
        _playing = false;
        GetTree().Paused = false;
        Host.StartBackdrop();
        Hud.Visible = false;
        Hud.ProcessMode = ProcessModeEnum.Disabled;
        Tools.Enabled = false;
        Camera.Interactive = false;
        Camera.AutoOrbit = true;
        Camera.SetView(40, -38, 26, Main.FocusPoint(Host.Sim.World));
        Audio.WorldSounds = false;
        Audio.PlayMenuMusic();
        Menus.ShowTitle();
    }

    private void EnterGame()
    {
        _playing = true;
        GetTree().Paused = false;
        Menus.HideAll();
        Hud.Visible = true;
        Hud.ProcessMode = ProcessModeEnum.Inherit;
        Tools.Enabled = true;
        Camera.Interactive = true;
        Camera.AutoOrbit = false;
        Audio.WorldSounds = true;
        Audio.PlayGameMusic();
    }

    public void NewGame(int slot, string name, bool withDemo)
    {
        Host.NewGame(withDemo, slot, name);
        EnterGame();
        if (!Settings.TutorialDone && !withDemo) Hud.StartTutorial();
    }

    public void LoadGame(int slot)
    {
        if (Host.TryLoad(slot)) EnterGame();
    }

    public void Pause()
    {
        if (Menus.TitleVisible) return;
        if (Menus.CloseTopWindow()) return; // Esc closes Settings opened from the Game window first
        GetTree().Paused = true;
        Menus.ShowPause(true);
    }

    public void Resume()
    {
        GetTree().Paused = false;
        Menus.ShowPause(false);
    }

    public void SaveGame() => Host.Save();

    public void QuitToMenu()
    {
        if (_playing) Host.Save(quiet: true);
        ShowTitle();
    }

    public void QuitGame()
    {
        if (_playing) Host.Save(quiet: true);
        if (PersistSettings) Settings.Save();
        GetTree().Quit();
    }

    public void SettingsChanged()
    {
        Apply();
        if (PersistSettings) _saveSettingsIn = 0.5; // once a slider stops moving
    }

    public override void _Process(double delta)
    {
        if (_saveSettingsIn >= 0 && (_saveSettingsIn -= delta) < 0) Settings.Save();
    }

    public void Apply()
    {
        Settings.ApplyDisplay(GetTree().Root);
        Audio.ApplyVolumes(Settings);
        View.ApplyQuality(Settings.Quality);
        Host.AutosaveSeconds = Settings.AutosaveSeconds;
    }

    public override void _UnhandledInput(InputEvent ev)
    {
        if (ev is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }) return;
        if (Menus.CloseTopWindow()) GetViewport().SetInputAsHandled();
        else if (Menus.PauseVisible)
        {
            Resume();
            GetViewport().SetInputAsHandled();
        }
    }
}
