using System;
using System.Reflection;

namespace FactorySim.Client;

/// <summary>
/// The version of the build on screen. It is read back from the assembly the client was compiled
/// into, which the build stamps from the VERSION file at the repository root, so the number shown
/// is always the number that was built and never a string someone edited twice.
/// </summary>
public static class BuildInfo
{
    /// <summary>Three numbers: major, minor, patch. "0.1.0".</summary>
    public static string Version => Stamped.Version;

    /// <summary>The short commit the build came from, or empty for a build that did not stamp one.</summary>
    public static string Commit => Stamped.Commit;

    /// <summary>What the corner of the screen shows.</summary>
    public static string Label => $"Build {Version}";

    /// <summary>Hover text: the version, and the commit when the build knows it. This is what a bug
    /// report needs to name the exact build.</summary>
    public static string Tooltip => Commit.Length == 0
        ? $"Version {Version}"
        : $"Version {Version}, commit {Commit}";

    private static readonly (string Version, string Commit) Stamped = Read();

    private static (string Version, string Commit) Read()
    {
        // The SDK writes AssemblyInformationalVersion as "<version>+<commit>"; a build outside a git
        // checkout has no commit after the plus.
        string info = typeof(BuildInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        string[] parts = info.Split('+', 2);
        string commit = parts.Length > 1 && parts[1].Length >= 7 ? parts[1][..7] : "";
        return (parts[0], commit);
    }
}
