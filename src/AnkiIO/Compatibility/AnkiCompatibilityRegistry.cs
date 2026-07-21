namespace AnkiIO;

/// <summary>Registers version adapters in precedence order and resolves verified capabilities for an Anki version.</summary>
/// <remarks>
/// <para>
/// Resolution returns the first adapter whose <see cref="IAnkiVersionAdapter.Supports"/> method succeeds. Register narrow
/// or application-specific adapters before broad fallbacks. <see cref="CreateDefault"/> places built-in adapters first,
/// so adapters appended to that result do not override a built-in match; create an empty registry to choose custom-first
/// precedence explicitly.
/// </para>
/// <para>
/// Registries are mutable configuration objects and are not safe for concurrent registration and resolution. Adapters
/// may overlap and may be added more than once; ordering, rather than deduplication, defines the outcome. Each default
/// registry and each built-in adapter instance is independent, while its capability sets remain immutable.
/// </para>
/// <code>
/// var installation = AnkiInstallationDetector.Detect();
/// if (installation is not null)
/// {
///     var capabilities = AnkiCompatibilityRegistry.CreateDefault().Resolve(installation.Version);
///     Console.WriteLine($"{capabilities.Name}: scheduler {capabilities.SchedulerVersion}");
/// }
/// </code>
/// </remarks>
public sealed class AnkiCompatibilityRegistry
{
    private readonly List<IAnkiVersionAdapter> adapters = [];

    /// <summary>Initializes an empty compatibility registry.</summary>
    /// <remarks>
    /// Add adapters from highest to lowest precedence. Use <see cref="CreateDefault"/> when built-in, evidence-backed
    /// adapters should be registered automatically.
    /// </remarks>
    public AnkiCompatibilityRegistry()
    {
    }

    /// <summary>Registers an adapter at the end of the resolution precedence.</summary>
    /// <param name="adapter">The compatibility adapter to register.</param>
    /// <returns>This registry, enabling fluent ordered registration.</returns>
    /// <remarks>
    /// Duplicate instances and overlapping ranges are permitted. If several adapters support a version,
    /// <see cref="Resolve"/> returns the one registered earliest.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="adapter"/> is <see langword="null"/>.</exception>
    public AnkiCompatibilityRegistry Add(IAnkiVersionAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        adapters.Add(adapter);
        return this;
    }

    /// <summary>Returns the highest-precedence registered adapter supporting an Anki application version.</summary>
    /// <param name="version">The parsed Anki application version to resolve.</param>
    /// <returns>The first registered adapter that reports compatibility with <paramref name="version"/>.</returns>
    /// <remarks>
    /// This is a metadata lookup only. It neither detects Anki nor opens a package or collection. An unsupported result is
    /// explicit rather than silently selecting the nearest release family.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="version"/> is <see langword="null"/>.</exception>
    /// <exception cref="NotSupportedException">No registered adapter supports <paramref name="version"/>.</exception>
    public IAnkiVersionAdapter Resolve(Version version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return adapters.FirstOrDefault(adapter => adapter.Supports(version)) ?? throw new NotSupportedException($"Anki {version} is not supported. Register an IAnkiVersionAdapter after validating its collection, scheduler, and package formats.");
    }

    /// <summary>Creates a new registry populated with AnkiIO's built-in, evidence-backed adapters.</summary>
    /// <returns>A mutable registry containing a new <see cref="Anki2605VersionAdapter"/>.</returns>
    /// <remarks>
    /// Each call returns an independent registry. Additions to one result do not affect later results. Appended adapters
    /// have lower precedence than the built-ins already present.
    /// </remarks>
    public static AnkiCompatibilityRegistry CreateDefault() => new AnkiCompatibilityRegistry().Add(new Anki2605VersionAdapter());
}
