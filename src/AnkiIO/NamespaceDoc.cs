using System.Runtime.CompilerServices;

namespace AnkiIO;

/// <summary>
/// Provides format-independent deck, note, card, scheduling, validation, media, JSON, compatibility, and guarded
/// legacy-package APIs for Anki-compatible data.
/// </summary>
/// <remarks>
/// AnkiIO targets .NET 10 and is tested primarily against Anki 26.05. It does not write live profiles or the modern
/// <c>collection.anki21b</c> representation. Start with <see cref="AnkiDeck.AddBasicNote"/> for conventional cards,
/// <see cref="AnkiPackageWriter"/> for isolated package output, and <see cref="AnkiValidator"/> before advanced writes.
/// Public mutable object graphs are not safe for concurrent mutation unless a member explicitly says otherwise.
/// </remarks>
[CompilerGenerated]
internal sealed class NamespaceDoc;
