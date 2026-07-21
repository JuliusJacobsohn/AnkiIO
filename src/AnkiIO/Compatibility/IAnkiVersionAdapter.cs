namespace AnkiIO;

/// <summary>Describes collection, scheduler, and package capabilities verified for an Anki version family.</summary>
/// <remarks>
/// <para>
/// An adapter is evidence-backed capability metadata, not an alternate package reader, a database migration, or a promise
/// that every feature of the represented Anki release is supported. Resolving an adapter does not open an installation or
/// bypass the documented safety and format limits of individual AnkiIO operations.
/// </para>
/// <para>
/// Implementations should make <see cref="Supports"/> deterministic and expose stable immutable sets. Applications can
/// implement this interface for an independently tested version family and register it ahead of broader fallbacks in an
/// <see cref="AnkiCompatibilityRegistry"/>.
/// </para>
/// </remarks>
public interface IAnkiVersionAdapter
{
    /// <summary>Gets the stable, human-readable adapter name.</summary>
    /// <value>A name suitable for diagnostics and compatibility reports.</value>
    string Name { get; }

    /// <summary>Determines whether this adapter represents the supplied Anki application version.</summary>
    /// <param name="version">The non-null parsed Anki application version to evaluate.</param>
    /// <returns><see langword="true"/> only when compatibility evidence covers <paramref name="version"/>.</returns>
    bool Supports(Version version);

    /// <summary>Gets collection schema versions covered by the adapter's evidence.</summary>
    /// <value>A stable, immutable set of numeric Anki collection schema identifiers.</value>
    IReadOnlySet<int> CollectionSchemas { get; }

    /// <summary>Gets the Anki scheduler generation associated with the verified version family.</summary>
    /// <value>The scheduler generation, such as <c>3</c> for Anki's v3 scheduler.</value>
    int SchedulerVersion { get; }

    /// <summary>Gets collection and metadata entry names recognized in packages from this version family.</summary>
    /// <value>A stable, immutable, case-sensitive set of top-level ZIP entry names.</value>
    /// <remarks>
    /// Recognition metadata records representations encountered in the version family. It does not imply that every
    /// AnkiIO writer emits, or every reader fully interprets, each listed representation.
    /// </remarks>
    IReadOnlySet<string> PackageEntries { get; }
}
