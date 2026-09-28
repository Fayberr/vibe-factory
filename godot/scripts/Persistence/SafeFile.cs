using System;
using System.IO;
using System.Text;

namespace FactorySim.Client;

/// <summary>What <see cref="SafeFile.Read{T}"/> returns.</summary>
/// <param name="Value">The parsed contents.</param>
/// <param name="FromBackup">True when the file itself could not be used and its backup was read instead.</param>
/// <param name="PrimaryError">Why the file itself was rejected, when <paramref name="FromBackup"/> is true.</param>
public sealed record SafeReadResult<T>(T Value, bool FromBackup, Exception? PrimaryError);

/// <summary>
/// Crash-safe text files. Opening a file for writing empties it first, so a crash, kill or power
/// cut in the middle of a save used to leave a truncated file and lose the factory. Here a write
/// goes to a temporary file, is flushed to disk, and only then replaces the real file, keeping the
/// previous version as a backup. Readers fall back to that backup when the file is damaged.
/// </summary>
/// <remarks>
/// Plain System.IO with operating-system paths (use <c>ProjectSettings.GlobalizePath</c> for
/// <c>user://</c>). No Godot types, so the test project compiles this file directly.
/// </remarks>
public static class SafeFile
{
    public static string BackupPath(string path) => path + ".bak";
    public static string TempPath(string path) => path + ".tmp";

    /// <summary>Whether the file or its backup exists.</summary>
    public static bool Exists(string path) => File.Exists(path) || File.Exists(BackupPath(path));

    /// <summary>
    /// Replaces the file's contents in one step: readers see either the old contents or the new ones,
    /// never a mix or a truncated file. With <paramref name="keepBackup"/>, the old contents are kept
    /// next to it as <see cref="BackupPath"/>.
    /// </summary>
    public static void Write(string path, string text, bool keepBackup = true)
    {
        string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (dir != null) Directory.CreateDirectory(dir);

        string tmp = TempPath(path);
        using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            byte[] bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(flushToDisk: true);
        }

        if (!File.Exists(path))
        {
            File.Move(tmp, path);
            return;
        }
        if (!keepBackup)
        {
            File.Move(tmp, path, overwrite: true);
            return;
        }
        try
        {
            File.Replace(tmp, path, BackupPath(path), ignoreMetadataErrors: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Some file systems refuse a replace (network drives, locked backups). Fall back to two
            // steps; the old file stays in place until the complete new one moves over it.
            File.Copy(path, BackupPath(path), overwrite: true);
            File.Move(tmp, path, overwrite: true);
        }
    }

    /// <summary>
    /// Reads and parses the file, or its backup when the file is missing or <paramref name="parse"/>
    /// throws. <paramref name="fallBackOn"/> limits which errors count as damage (default: all):
    /// a save from a newer game version, for example, should fail rather than quietly load an older
    /// backup that the next save would then write over it.
    /// Returns null when neither exists; rethrows the file's own error when neither can be used.
    /// </summary>
    public static SafeReadResult<T>? Read<T>(string path, Func<string, T> parse, Func<Exception, bool>? fallBackOn = null)
    {
        Exception? primary = null;
        if (File.Exists(path))
        {
            try
            {
                return new SafeReadResult<T>(parse(File.ReadAllText(path)), false, null);
            }
            catch (Exception ex) when (fallBackOn?.Invoke(ex) ?? true)
            {
                primary = ex;
            }
        }

        string backup = BackupPath(path);
        if (File.Exists(backup))
        {
            try
            {
                return new SafeReadResult<T>(parse(File.ReadAllText(backup)), true, primary);
            }
            catch (Exception) when (primary != null)
            {
                // Both are unusable: report the file's own problem, not the backup's.
            }
        }

        if (primary != null) throw new InvalidDataException(primary.Message, primary);
        return null;
    }

    /// <summary>Makes the backup the file again, after <see cref="Read{T}"/> had to fall back to it.</summary>
    public static void RestoreBackup(string path)
    {
        string tmp = TempPath(path);
        File.Copy(BackupPath(path), tmp, overwrite: true);
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Deletes the file, its backup and any temporary file left by an interrupted write.</summary>
    public static void Delete(string path)
    {
        foreach (string p in new[] { path, BackupPath(path), TempPath(path) })
            if (File.Exists(p)) File.Delete(p);
    }
}
