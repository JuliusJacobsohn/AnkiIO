using Microsoft.Win32;

namespace AnkiIO;

/// <summary>Discovers a local Anki executable and reports conservative profile-presence metadata.</summary>
/// <remarks>
/// <para>
/// Detection is read-only with respect to Anki. It never starts the application, loads an add-on, opens a collection
/// database, or changes profile data. It checks candidate executable paths, reads Windows uninstall metadata when
/// applicable, enumerates only immediate profile directories, and performs existence checks for known paths.
/// </para>
/// <para>
/// Set <c>ANKI_EXECUTABLE</c> to prioritize an explicit executable, <c>ANKI_VERSION</c> to supply its version text, and
/// <c>ANKI_DATA_DIRECTORY</c> to override the conventional profile root. These variables are process-wide; applications
/// and tests that temporarily change them must serialize those changes with concurrent detection.
/// </para>
/// <para>
/// Detection answers where Anki appears to be installed, not whether AnkiIO supports its formats. Pass the returned
/// <see cref="AnkiInstallation.Version"/> to <see cref="AnkiCompatibilityRegistry.Resolve"/> for that separate check.
/// </para>
/// </remarks>
public static class AnkiInstallationDetector
{
    /// <summary>Locates the first existing Anki executable using overrides and platform conventions.</summary>
    /// <returns>A point-in-time installation snapshot, or <see langword="null"/> when no candidate executable exists.</returns>
    /// <remarks>
    /// Candidate precedence is <c>ANKI_EXECUTABLE</c>, Windows uninstall metadata where applicable, and conventional
    /// platform paths. On Windows, file product metadata is a version fallback; on other platforms an unavailable or
    /// unparseable version is represented by <c>0.0</c>. No executable code is run during detection.
    /// </remarks>
    /// <exception cref="ArgumentException">An environment override contains an invalid path.</exception>
    /// <exception cref="IOException">A candidate data directory cannot be enumerated.</exception>
    /// <exception cref="UnauthorizedAccessException">Installation or profile metadata cannot be accessed.</exception>
    public static AnkiInstallation? Detect()
    {
        var candidates = new List<(string Path, string? Version)>();
        var overridden = Environment.GetEnvironmentVariable("ANKI_EXECUTABLE");
        if (!string.IsNullOrWhiteSpace(overridden))
        {
            candidates.Add((overridden, Environment.GetEnvironmentVariable("ANKI_VERSION")));
        }

        if (OperatingSystem.IsWindows())
        {
            AddWindowsRegistryCandidates(candidates);
            candidates.Add((Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Anki", "anki.exe"), null));
            candidates.Add((Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Anki", "anki.exe"), null));
        }
        else if (OperatingSystem.IsMacOS())
        {
            candidates.Add(("/Applications/Anki.app/Contents/MacOS/anki", null));
        }
        else
        {
            candidates.Add(("/usr/bin/anki", null));
            candidates.Add(("/usr/local/bin/anki", null));
        }

        var candidate = candidates.FirstOrDefault(item => File.Exists(item.Path));
        if (candidate == default)
        {
            return null;
        }

        var text = candidate.Version;
        if (string.IsNullOrWhiteSpace(text) && OperatingSystem.IsWindows())
        {
            text = System.Diagnostics.FileVersionInfo.GetVersionInfo(candidate.Path).ProductVersion;
        }

        text = string.IsNullOrWhiteSpace(text) ? "0.0" : text.Split([' ', '+'], StringSplitOptions.RemoveEmptyEntries)[0];
        _ = Version.TryParse(text, out var version);
        var data = GetDataDirectory();
        var profileDirectories = data is null || !Directory.Exists(data) ? [] : Directory.GetDirectories(data);
        return new AnkiInstallation(
            Path.GetFullPath(candidate.Path),
            Path.GetDirectoryName(Path.GetFullPath(candidate.Path))!,
            version ?? new Version(0, 0),
            text,
            data,
            profileDirectories.Length > 0,
            profileDirectories.Any(directory => File.Exists(Path.Combine(directory, "collection.anki2")) || File.Exists(Path.Combine(directory, "collection.anki21"))),
            profileDirectories.Any(directory => Directory.Exists(Path.Combine(directory, "collection.media"))),
            data is not null && Directory.Exists(Path.Combine(data, "addons21")));
    }

    private static string? GetDataDirectory()
    {
        var overridden = Environment.GetEnvironmentVariable("ANKI_DATA_DIRECTORY");
        if (!string.IsNullOrWhiteSpace(overridden))
        {
            return Path.GetFullPath(overridden);
        }

        if (OperatingSystem.IsWindows()) return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Anki2");
        if (OperatingSystem.IsMacOS()) return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Library", "Application Support", "Anki2");
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), ".local", "share", "Anki2");
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void AddWindowsRegistryCandidates(List<(string Path, string? Version)> candidates)
    {
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var uninstall = hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall is null) continue;
            foreach (var name in uninstall.GetSubKeyNames())
            {
                using var key = uninstall.OpenSubKey(name);
                if (key is null || !string.Equals(key.GetValue("DisplayName") as string, "Anki", StringComparison.OrdinalIgnoreCase)) continue;
                var location = key.GetValue("InstallLocation") as string;
                var icon = (key.GetValue("DisplayIcon") as string)?.Split(',')[0].Trim('"');
                var path = !string.IsNullOrWhiteSpace(location) ? Path.Combine(location, "anki.exe") : icon;
                if (!string.IsNullOrWhiteSpace(path)) candidates.Add((path, key.GetValue("DisplayVersion") as string));
            }
        }
    }
}
