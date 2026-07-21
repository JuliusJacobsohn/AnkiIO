using System.Collections.Frozen;

namespace AnkiIO;

/// <summary>Describes format capabilities verified against the Anki 26.05 release family.</summary>
/// <remarks>
/// <para>
/// Evidence covers Anki 26.05 build <c>e64c6b1a</c>, collection schema 18, the v3 scheduler, and isolated import and render
/// checks. <see cref="Supports"/> accepts patch and build revisions within major 26, minor 5; adjacent minor releases are
/// intentionally not inferred to be compatible.
/// </para>
/// <para>
/// Package entry metadata records names used by this Anki family. It is not a claim that every AnkiIO operation emits or
/// fully interprets every representation. In particular, the package writer currently emits a guarded legacy
/// <c>collection.anki2</c> representation accepted by the verified Anki import path.
/// </para>
/// <para>
/// Instances are stateless. <see cref="CollectionSchemas"/> and <see cref="PackageEntries"/> are frozen and can be shared
/// safely between readers without defensive copying.
/// </para>
/// </remarks>
public sealed class Anki2605VersionAdapter : IAnkiVersionAdapter
{
    /// <summary>Initializes the stateless Anki 26.05 capability adapter.</summary>
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
    /// <value>An immutable set containing schema <c>18</c>.</value>
    public IReadOnlySet<int> CollectionSchemas { get; } = new[] { 18 }.ToFrozenSet();

    /// <summary>Gets the verified scheduler generation.</summary>
    /// <value><c>3</c>, representing Anki's v3 scheduler.</value>
    public int SchedulerVersion => 3;

    /// <summary>Gets recognized top-level package entry names for the Anki 26.05 family.</summary>
    /// <value>
    /// An immutable, ordinal set containing <c>collection.anki2</c>, <c>collection.anki21</c>,
    /// <c>collection.anki21b</c>, <c>meta</c>, and <c>media</c>.
    /// </value>
    public IReadOnlySet<string> PackageEntries { get; } = new[] { "collection.anki2", "collection.anki21", "collection.anki21b", "meta", "media" }.ToFrozenSet(StringComparer.Ordinal);
}
