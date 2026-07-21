namespace AnkiIO;

internal sealed record AnkiPackageWritePlan(
    IReadOnlyList<AnkiDeck> Roots,
    IReadOnlyList<AnkiMediaFile> Media);
