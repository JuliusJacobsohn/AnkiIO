using Microsoft.Win32;

namespace AnkiIO;

/// <summary>Describes one installed Anki application without exposing profile content.</summary>
public sealed record AnkiInstallation(
    string ExecutablePath,
    string InstallationDirectory,
    Version Version,
    string VersionText,
    string? DataDirectory,
    bool ProfilesPresent,
    bool CollectionsPresent,
    bool MediaDirectoriesPresent,
    bool AddonsDirectoryPresent);

/// <summary>Locates Anki without starting it or opening a collection.</summary>
public static class AnkiInstallationDetector
{
    /// <summary>Attempts to detect Anki using environment overrides, platform conventions, and Windows uninstall metadata.</summary>
    /// <returns>An installation descriptor, or <see langword="null"/> when Anki is not found.</returns>
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

/// <summary>Defines capabilities associated with a supported Anki version family.</summary>
public interface IAnkiVersionAdapter
{
    /// <summary>Gets the adapter's stable name.</summary>
    string Name { get; }

    /// <summary>Gets whether this adapter can safely handle an installed version.</summary>
    bool Supports(Version version);

    /// <summary>Gets the collection schema versions understood by this adapter.</summary>
    IReadOnlySet<int> CollectionSchemas { get; }

    /// <summary>Gets the supported scheduler version.</summary>
    int SchedulerVersion { get; }

    /// <summary>Gets supported package collection entry names.</summary>
    IReadOnlySet<string> PackageEntries { get; }
}

/// <summary>Selects version adapters and fails explicitly for unknown installations.</summary>
public sealed class AnkiCompatibilityRegistry
{
    private readonly List<IAnkiVersionAdapter> adapters = [];

    /// <summary>Registers an adapter at the end of selection precedence.</summary>
    public AnkiCompatibilityRegistry Add(IAnkiVersionAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        adapters.Add(adapter);
        return this;
    }

    /// <summary>Returns the first adapter supporting the version.</summary>
    /// <exception cref="NotSupportedException">No registered adapter supports the version.</exception>
    public IAnkiVersionAdapter Resolve(Version version) => adapters.FirstOrDefault(adapter => adapter.Supports(version)) ?? throw new NotSupportedException($"Anki {version} is not supported. Register an IAnkiVersionAdapter after validating its collection, scheduler, and package formats.");

    /// <summary>Creates the built-in registry containing only versions with recorded compatibility evidence.</summary>
    public static AnkiCompatibilityRegistry CreateDefault() => new AnkiCompatibilityRegistry().Add(new Anki2605VersionAdapter());
}

/// <summary>Describes verified format capabilities for Anki 26.05.</summary>
public sealed class Anki2605VersionAdapter : IAnkiVersionAdapter
{
    /// <inheritdoc />
    public string Name => "Anki 26.05";

    /// <inheritdoc />
    public bool Supports(Version version) => version.Major == 26 && version.Minor == 5;

    /// <inheritdoc />
    public IReadOnlySet<int> CollectionSchemas { get; } = new HashSet<int> { 18 };

    /// <inheritdoc />
    public int SchedulerVersion => 3;

    /// <inheritdoc />
    public IReadOnlySet<string> PackageEntries { get; } = new HashSet<string>(StringComparer.Ordinal) { "collection.anki2", "collection.anki21", "collection.anki21b", "meta", "media" };
}
