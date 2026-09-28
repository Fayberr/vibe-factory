using System.Text.Json;
using FactorySim.Client;
using FactorySim.Persistence;
using FactorySim.Samples;

namespace FactorySim.Tests;

/// <summary>
/// The client's crash-safe file writes. A save interrupted by a crash, kill or power cut must
/// never cost the factory: either the old save survives, or the damaged one falls back to its backup.
/// </summary>
public sealed class SafeFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vf-safefile-" + Guid.NewGuid().ToString("N"));
    private string SlotFile => Path.Combine(_dir, "saves", "slot1.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static Simulation DemoAt(int ticks)
    {
        var sim = TestUtil.NewSim(sandbox: false, money: 10_000);
        DemoLayout.Build(sim);
        sim.Step(ticks);
        return sim;
    }

    private static LoadResult Load(string json) => SaveSystem.Deserialize(json, TestUtil.Content);

    [Fact]
    public void Write_creates_the_folder_and_leaves_no_temporary_file()
    {
        SafeFile.Write(SlotFile, "first");

        Assert.Equal("first", File.ReadAllText(SlotFile));
        Assert.False(File.Exists(SafeFile.TempPath(SlotFile)));
        Assert.False(File.Exists(SafeFile.BackupPath(SlotFile)));
    }

    [Fact]
    public void Each_write_keeps_the_previous_contents_as_the_backup()
    {
        SafeFile.Write(SlotFile, "first");
        SafeFile.Write(SlotFile, "second");
        SafeFile.Write(SlotFile, "third");

        Assert.Equal("third", File.ReadAllText(SlotFile));
        Assert.Equal("second", File.ReadAllText(SafeFile.BackupPath(SlotFile)));
    }

    [Fact]
    public void Write_without_backup_replaces_in_place()
    {
        SafeFile.Write(SlotFile, "first", keepBackup: false);
        SafeFile.Write(SlotFile, "second", keepBackup: false);

        Assert.Equal("second", File.ReadAllText(SlotFile));
        Assert.False(File.Exists(SafeFile.BackupPath(SlotFile)));
    }

    [Fact]
    public void A_save_cut_off_mid_write_is_rejected_by_the_loader()
    {
        // The recovery relies on this: a truncated or empty save must fail to parse, not load half a factory.
        string json = SaveSystem.Serialize(DemoAt(300));
        Assert.ThrowsAny<JsonException>(() => Load(json[..(json.Length / 2)]));
        Assert.ThrowsAny<JsonException>(() => Load(""));
    }

    [Fact]
    public void A_crash_while_writing_leaves_the_last_save_intact()
    {
        SafeFile.Write(SlotFile, SaveSystem.Serialize(DemoAt(300)));

        // The game dies halfway through the next save: only the temporary file is damaged.
        string next = SaveSystem.Serialize(DemoAt(600));
        File.WriteAllText(SafeFile.TempPath(SlotFile), next[..(next.Length / 3)]);

        var read = SafeFile.Read(SlotFile, Load)!;
        Assert.False(read.FromBackup);
        Assert.Equal(300, read.Value.Simulation.World.Tick);

        // The next save goes through normally and clears the leftover.
        SafeFile.Write(SlotFile, next);
        Assert.Equal(600, SafeFile.Read(SlotFile, Load)!.Value.Simulation.World.Tick);
        Assert.False(File.Exists(SafeFile.TempPath(SlotFile)));
    }

    [Fact]
    public void A_damaged_save_falls_back_to_the_one_before_it()
    {
        SafeFile.Write(SlotFile, SaveSystem.Serialize(DemoAt(300)));
        SafeFile.Write(SlotFile, SaveSystem.Serialize(DemoAt(600)));

        // The same damage the old direct write caused: the file was emptied and only partly rewritten.
        string saved = File.ReadAllText(SlotFile);
        File.WriteAllText(SlotFile, saved[..(saved.Length / 2)]);

        var read = SafeFile.Read(SlotFile, Load)!;
        Assert.True(read.FromBackup);
        Assert.IsAssignableFrom<JsonException>(read.PrimaryError);
        Assert.Equal(300, read.Value.Simulation.World.Tick);

        SafeFile.RestoreBackup(SlotFile);
        var restored = SafeFile.Read(SlotFile, Load)!;
        Assert.False(restored.FromBackup);
        Assert.Equal(300, restored.Value.Simulation.World.Tick);
    }

    [Fact]
    public void A_missing_save_with_a_backup_still_loads()
    {
        SafeFile.Write(SlotFile, "first");
        SafeFile.Write(SlotFile, "second");
        File.Delete(SlotFile);

        Assert.True(SafeFile.Exists(SlotFile));
        var read = SafeFile.Read(SlotFile, text => text)!;
        Assert.True(read.FromBackup);
        Assert.Equal("first", read.Value);
    }

    [Fact]
    public void Errors_outside_the_filter_do_not_fall_back()
    {
        // A save from a newer game version must not quietly load the older backup instead.
        SafeFile.Write(SlotFile, "old");
        SafeFile.Write(SlotFile, "newer");

        Assert.Throws<NotSupportedException>(() => SafeFile.Read<string>(
            SlotFile,
            text => text == "newer" ? throw new NotSupportedException("newer version") : text,
            fallBackOn: ex => ex is not NotSupportedException));
    }

    [Fact]
    public void When_both_copies_are_damaged_the_read_fails_with_the_files_own_error()
    {
        SafeFile.Write(SlotFile, "a");
        SafeFile.Write(SlotFile, "b");

        var ex = Assert.Throws<InvalidDataException>(() =>
            SafeFile.Read<string>(SlotFile, text => throw new FormatException($"bad {text}")));
        Assert.Equal("bad b", ex.Message);
    }

    [Fact]
    public void Nothing_to_read_returns_null_and_delete_removes_every_copy()
    {
        Assert.False(SafeFile.Exists(SlotFile));
        Assert.Null(SafeFile.Read(SlotFile, text => text));

        SafeFile.Write(SlotFile, "first");
        SafeFile.Write(SlotFile, "second");
        File.WriteAllText(SafeFile.TempPath(SlotFile), "leftover");
        SafeFile.Delete(SlotFile);

        Assert.False(SafeFile.Exists(SlotFile));
        Assert.False(File.Exists(SafeFile.TempPath(SlotFile)));
    }
}
