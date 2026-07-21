using System.Collections.Frozen;
using Microsoft.Win32;

namespace AnkiIO;

/// <summary>
/// Provides an immutable, read-only snapshot of an Anki application discovered on the local computer.
/// </summary>
/// <param name="ExecutablePath">The absolute path to the discovered Anki executable.</param>
/// <param name="InstallationDirectory">The absolute directory that contains <paramref name="ExecutablePath"/>.</param>
/// <param name="Version">
/// The parsed application version, or <c>0.0</c> when the detector could not parse the available version text.
/// </param>
/// <param name="VersionText">
/// The normalized version text obtained from an environment override or platform installation metadata.
/// </param>
/// <param name="DataDirectory">
/// The conventional or overridden Anki data directory. The directory is not guaranteed to exist.
/// </param>
/// <param name="ProfilesPresent">
/// <see langword="true"/> when the data directory contained at least one immediate child directory at detection time.
/// </param>
/// <param name="CollectionsPresent">
/// <see langword="true"/> when a discovered profile directory contained a recognized collection database at detection time.
/// </param>
/// <param name="MediaDirectoriesPresent">
/// <see langword="true"/> when a discovered profile directory contained a <c>collection.media</c> directory at detection time.
/// </param>
/// <param name="AddonsDirectoryPresent">
/// <see langword="true"/> when an <c>addons21</c> directory existed directly below the data directory at detection time.
/// </param>
/// <remarks>
/// Detection does not start Anki, open a collection database, enumerate profile contents beyond immediate paths, or mutate
/// any Anki data. Presence flags are point-in-time hints and must not be treated as proof that a profile is readable,
/// compatible, or safe to modify. Use <see cref="AnkiCompatibilityRegistry"/> separately to select a version adapter.
/// </remarks>
public sealed record AnkiInstallation(
    string ExecutablePath,
    string InstallationDirectory,
    Version Version,
    string VersionText,
    string? DataDirectory,
    bool ProfilesPresent,
    bool CollectionsPresent,
    bool MediaDirectoriesPresent,
    bool AddonsDirectoryPresent)
{
    /// <summary>Gets the discovered Anki executable path.</summary>
    /// <value>The absolute path supplied to the positional constructor.</value>
    public string ExecutablePath { get; init; } = ExecutablePath;

    /// <summary>Gets the directory containing the discovered Anki executable.</summary>
    /// <value>The absolute installation directory supplied to the positional constructor.</value>
    public string InstallationDirectory { get; init; } = InstallationDirectory;

    /// <summary>Gets the parsed Anki application version.</summary>
    /// <value>The parsed version, or <c>0.0</c> when detection could not parse the available version text.</value>
    public Version Version { get; init; } = Version;

    /// <summary>Gets the normalized Anki version text observed during detection.</summary>
    /// <value>The normalized version text supplied to the positional constructor.</value>
    public string VersionText { get; init; } = VersionText;

    /// <summary>Gets the conventional or explicitly overridden Anki data directory.</summary>
    /// <value>The candidate data-directory path, or <see langword="null"/> when no path is available.</value>
    public string? DataDirectory { get; init; } = DataDirectory;

    /// <summary>Gets whether the data directory contained at least one immediate child directory when detected.</summary>
    /// <value>A point-in-time presence hint; it does not prove that a valid or readable Anki profile exists.</value>
    public bool ProfilesPresent { get; init; } = ProfilesPresent;

    /// <summary>Gets whether a discovered profile directory contained a recognized collection filename when detected.</summary>
    /// <value>A point-in-time presence hint; it does not prove that the collection format is supported or readable.</value>
    public bool CollectionsPresent { get; init; } = CollectionsPresent;

    /// <summary>Gets whether a discovered profile directory contained a <c>collection.media</c> directory when detected.</summary>
    /// <value>A point-in-time presence hint; media contents are not inspected.</value>
    public bool MediaDirectoriesPresent { get; init; } = MediaDirectoriesPresent;

    /// <summary>Gets whether an <c>addons21</c> directory existed directly below the data directory when detected.</summary>
    /// <value>A point-in-time presence hint; add-on contents are not inspected.</value>
    public bool AddonsDirectoryPresent { get; init; } = AddonsDirectoryPresent;
}

/// <summary>Discovers a local Anki executable and reports conservative profile-presence metadata.</summary>
/// <remarks>
/// The detector is side-effect free with respect to Anki: it never launches the application and never opens a collection
/// database. Set <c>ANKI_EXECUTABLE</c> to prioritize an explicit executable, <c>ANKI_VERSION</c> to provide its version,
/// and <c>ANKI_DATA_DIRECTORY</c> to override the conventional profile root. Environment overrides are process-wide, so
/// callers that change them should coordinate concurrent detection themselves.
/// </remarks>
public static class AnkiInstallationDetector
{
    /// <summary>
    /// Attempts to locate the first existing Anki executable using environment overrides and platform-specific conventions.
    /// </summary>
    /// <returns>
    /// A snapshot describing the first executable found; otherwise, <see langword="null"/> when no candidate exists.
    /// </returns>
    /// <remarks>
    /// Candidate precedence is <c>ANKI_EXECUTABLE</c>, Windows uninstall metadata when applicable, and then conventional
    /// installation paths. On Windows, product metadata supplies the version when <c>ANKI_VERSION</c> is absent; on other
    /// platforms an unknown version is represented by <c>0.0</c>. A successful result describes installation presence only
    /// and does not imply that the version or its file formats are supported by AnkiIO.
    /// </remarks>
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

/// <summary>Describes collection, scheduler, and package capabilities verified for an Anki version family.</summary>
/// <remarks>
/// An adapter is capability metadata used for explicit compatibility selection. It does not open an installed collection
/// or bypass the safety and format limitations of individual readers and writers. Implementations should be deterministic
/// and should return stable, read-only capability sets.
/// </remarks>
public interface IAnkiVersionAdapter
{
    /// <summary>Gets the stable, human-readable adapter name.</summary>
    /// <value>A name suitable for diagnostics and compatibility reports.</value>
    string Name { get; }

    /// <summary>Determines whether this adapter represents the supplied Anki application version.</summary>
    /// <param name="version">The parsed Anki application version to evaluate.</param>
    /// <returns><see langword="true"/> when the adapter has recorded compatibility for <paramref name="version"/>.</returns>
    bool Supports(Version version);

    /// <summary>Gets the collection schema versions covered by the adapter's compatibility evidence.</summary>
    /// <value>A read-only set of numeric Anki collection schema identifiers.</value>
    IReadOnlySet<int> CollectionSchemas { get; }

    /// <summary>Gets the Anki scheduler generation associated with the adapter.</summary>
    /// <value>The numeric scheduler generation, such as <c>3</c> for Anki's v3 scheduler.</value>
    int SchedulerVersion { get; }

    /// <summary>Gets collection and metadata entry names recognized in packages from this version family.</summary>
    /// <value>A case-sensitive, read-only set of ZIP entry names without directory prefixes.</value>
    IReadOnlySet<string> PackageEntries { get; }
}

/// <summary>Registers version adapters in precedence order and resolves a verified adapter for an Anki version.</summary>
/// <remarks>
/// Resolution uses the first matching adapter, so callers should register narrow or preferred adapters before broad
/// fallbacks. Registration mutates the registry; do not call <see cref="Add"/> concurrently with <see cref="Resolve"/>
/// unless access is synchronized by the caller.
/// </remarks>
public sealed class AnkiCompatibilityRegistry
{
    private readonly List<IAnkiVersionAdapter> adapters = [];

    /// <summary>Initializes an empty compatibility registry.</summary>
    /// <remarks>
    /// Adapters are selected in subsequent <see cref="Add"/> order. Use <see cref="CreateDefault"/> when the built-in
    /// evidence-backed adapters should be registered automatically.
    /// </remarks>
    public AnkiCompatibilityRegistry()
    {
    }

    /// <summary>Registers an adapter at the end of the resolution precedence.</summary>
    /// <param name="adapter">The compatibility adapter to register.</param>
    /// <returns>This registry, enabling fluent registration.</returns>
    /// <remarks>
    /// Duplicate adapter instances and overlapping version ranges are permitted. When ranges overlap,
    /// <see cref="Resolve"/> returns the adapter registered first.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="adapter"/> is <see langword="null"/>.</exception>
    public AnkiCompatibilityRegistry Add(IAnkiVersionAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        adapters.Add(adapter);
        return this;
    }

    /// <summary>Returns the first registered adapter that supports an Anki application version.</summary>
    /// <param name="version">The parsed Anki application version to resolve.</param>
    /// <returns>The first registered adapter whose <see cref="IAnkiVersionAdapter.Supports"/> method returns <see langword="true"/>.</returns>
    /// <exception cref="NotSupportedException">No registered adapter supports <paramref name="version"/>.</exception>
    public IAnkiVersionAdapter Resolve(Version version) => adapters.FirstOrDefault(adapter => adapter.Supports(version)) ?? throw new NotSupportedException($"Anki {version} is not supported. Register an IAnkiVersionAdapter after validating its collection, scheduler, and package formats.");

    /// <summary>Creates a new registry populated with AnkiIO's built-in, evidence-backed adapters.</summary>
    /// <returns>A mutable registry containing an <see cref="Anki2605VersionAdapter"/>.</returns>
    /// <remarks>Each call returns an independent registry. Adding an adapter to one result does not affect later results.</remarks>
    public static AnkiCompatibilityRegistry CreateDefault() => new AnkiCompatibilityRegistry().Add(new Anki2605VersionAdapter());
}

/// <summary>Describes the format capabilities verified against Anki 26.05.</summary>
/// <remarks>
/// Evidence covers Anki 26.05 build <c>e64c6b1a</c>, collection schema 18, and the v3 scheduler. Package entry metadata
/// records names used by that Anki family; it is not a claim that every AnkiIO writer emits every listed representation.
/// In particular, the current package writer emits a guarded legacy <c>collection.anki2</c> representation accepted by
/// an isolated Anki 26.05 import test.
/// </remarks>
public sealed class Anki2605VersionAdapter : IAnkiVersionAdapter
{
    /// <summary>Initializes the stateless Anki 26.05 capability adapter.</summary>
    /// <remarks>Instances contain the same immutable capability values and own no external resources.</remarks>
    public Anki2605VersionAdapter()
    {
    }

    /// <summary>Gets the stable adapter name.</summary>
    /// <value><c>Anki 26.05</c>.</value>
    public string Name => "Anki 26.05";

    /// <summary>Determines whether a version belongs to the verified Anki 26.05 family.</summary>
    /// <param name="version">The parsed Anki application version.</param>
    /// <returns><see langword="true"/> only when the major version is 26 and the minor version is 5.</returns>
    public bool Supports(Version version) => version.Major == 26 && version.Minor == 5;

    /// <summary>Gets the verified Anki collection schema identifiers.</summary>
    /// <value>A read-only set containing schema <c>18</c>.</value>
    public IReadOnlySet<int> CollectionSchemas { get; } = new[] { 18 }.ToFrozenSet();

    /// <summary>Gets the verified scheduler generation.</summary>
    /// <value><c>3</c>, representing Anki's v3 scheduler.</value>
    public int SchedulerVersion => 3;

    /// <summary>Gets recognized top-level package entry names for the Anki 26.05 family.</summary>
    /// <value>
    /// A case-sensitive set containing <c>collection.anki2</c>, <c>collection.anki21</c>,
    /// <c>collection.anki21b</c>, <c>meta</c>, and <c>media</c>.
    /// </value>
    public IReadOnlySet<string> PackageEntries { get; } = new[] { "collection.anki2", "collection.anki21", "collection.anki21b", "meta", "media" }.ToFrozenSet(StringComparer.Ordinal);
}
