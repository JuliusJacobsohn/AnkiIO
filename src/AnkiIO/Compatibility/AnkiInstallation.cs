namespace AnkiIO;

/// <summary>Provides an immutable snapshot of an Anki application discovered on the local computer.</summary>
/// <param name="ExecutablePath">The absolute path to the discovered Anki executable.</param>
/// <param name="InstallationDirectory">The absolute directory that contains <paramref name="ExecutablePath"/>.</param>
/// <param name="Version">The parsed application version, or <c>0.0</c> when available text could not be parsed.</param>
/// <param name="VersionText">The normalized version text obtained from an override or installation metadata.</param>
/// <param name="DataDirectory">The conventional or overridden Anki data directory; it need not exist.</param>
/// <param name="ProfilesPresent">Whether the data directory had at least one immediate child directory.</param>
/// <param name="CollectionsPresent">Whether an immediate profile directory had a recognized collection filename.</param>
/// <param name="MediaDirectoriesPresent">Whether an immediate profile directory had a <c>collection.media</c> directory.</param>
/// <param name="AddonsDirectoryPresent">Whether an <c>addons21</c> directory existed below the data directory.</param>
/// <remarks>
/// <para>
/// This is discovery metadata, not a live installation handle. Presence flags are point-in-time hints and do not prove
/// that a profile is readable, that its schema is supported, or that modifying it would be safe. The detector does not
/// retain file or registry handles after constructing the snapshot.
/// </para>
/// <para>
/// Use <see cref="AnkiCompatibilityRegistry.Resolve"/> with <see cref="Version"/> when explicit format capability metadata
/// is needed. A successful detection and a successful adapter resolution are deliberately separate decisions.
/// </para>
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
    /// <value>The absolute path observed during detection; the file may subsequently move or disappear.</value>
    public string ExecutablePath { get; init; } = ExecutablePath;

    /// <summary>Gets the directory containing the discovered Anki executable.</summary>
    /// <value>The absolute installation directory observed during detection.</value>
    public string InstallationDirectory { get; init; } = InstallationDirectory;

    /// <summary>Gets the parsed Anki application version.</summary>
    /// <value>The parsed version, or <c>0.0</c> when detection could not parse the available version text.</value>
    public Version Version { get; init; } = Version;

    /// <summary>Gets the normalized version text observed during detection.</summary>
    /// <value>The first token before a space or build-metadata <c>+</c>, or <c>0.0</c> when none was available.</value>
    public string VersionText { get; init; } = VersionText;

    /// <summary>Gets the conventional or explicitly overridden Anki data directory.</summary>
    /// <value>The absolute candidate path, or <see langword="null"/> when no candidate path is available.</value>
    public string? DataDirectory { get; init; } = DataDirectory;

    /// <summary>Gets whether the data directory contained at least one immediate child directory when detected.</summary>
    /// <value>A presence hint; arbitrary child directories may satisfy it and contents are not recursively inspected.</value>
    public bool ProfilesPresent { get; init; } = ProfilesPresent;

    /// <summary>Gets whether an immediate profile directory contained a recognized collection filename.</summary>
    /// <value>A filename-presence hint for <c>collection.anki2</c> or <c>collection.anki21</c>; the database is not opened.</value>
    public bool CollectionsPresent { get; init; } = CollectionsPresent;

    /// <summary>Gets whether an immediate profile directory contained a <c>collection.media</c> directory.</summary>
    /// <value>A directory-presence hint; media names and payloads are not enumerated or read.</value>
    public bool MediaDirectoriesPresent { get; init; } = MediaDirectoriesPresent;

    /// <summary>Gets whether an <c>addons21</c> directory existed directly below the data directory.</summary>
    /// <value>A directory-presence hint; add-on contents are not inspected or executed.</value>
    public bool AddonsDirectoryPresent { get; init; } = AddonsDirectoryPresent;
}
