/*!
@mainpage AnkiIO API reference

AnkiIO is a .NET 10 library for creating, validating, reading, and writing Anki-compatible deck data. It offers a
format-independent object model, conventional Basic and Cloze helpers, custom note types, media, scheduling state,
deterministic JSON, a CrowdAnki-inspired interchange format, and guarded legacy APKG I/O.

@note AnkiIO was built with substantial AI assistance. Contributions and independent review are welcome.

## Start here

- @ref getting_started "Getting started" builds a useful deck and introduces the main APIs.
- @ref anki_concepts "Anki concepts" explains why notes, cards, and templates are separate.
- @ref formats_and_safety "Formats, compatibility, and safety" defines the verified scope and important boundaries.
- The **Classes** and **Class Members** navigation entries contain the complete public API generated directly from
  the source comments.

## Smallest useful program

@code{.cs}
using AnkiIO;

var deck = new AnkiDeck("German Vocabulary");
deck.AddBasicNote("Haus", "house", tags: ["german", "noun"]);
deck.AddBasicAndReversedNote("gehen", "to go", tags: ["german", "verb"]);

await AnkiPackageWriter.WriteAsync(deck, "GermanVocabulary.apkg");
@endcode

AnkiIO never needs access to a live Anki profile to create a package. Write a new output file, inspect validation
diagnostics when using advanced state, and import the resulting package through Anki.
*/

/*!
@page getting_started Getting started

## Install

Reference the `AnkiIO` package from a .NET 10 or newer application:

@code{.sh}
dotnet add package AnkiIO
@endcode

## Conventional cards

The convenience methods reuse the appropriate conventional note type throughout a deck hierarchy and create safe,
unscheduled cards:

@code{.cs}
using AnkiIO;

var deck = new AnkiDeck("German");
deck.AddBasicNote("der Apfel", "the apple", tags: ["german", "noun"]);
deck.AddBasicAndReversedNote("gehen", "to go", tags: ["german", "verb"]);

var answer = AnkiCloze.Wrap("Berlin", hint: "city");
deck.AddClozeNote($"Germany's capital is {answer}.", tags: ["german", "geography"]);
@endcode

Use AnkiDeck.AddBasicNote(), AnkiDeck.AddBasicAndReversedNote(), and AnkiDeck.AddClozeNote() unless you need a custom
model. Each method returns the created AnkiNote so fields and tags remain easy to adjust.

## Custom note type, templates, CSS, and tags

@code{.cs}
var vocabulary = new AnkiNoteType("Illustrated Vocabulary")
    .AddConfiguredField(new AnkiField("German", Font: "Arial", FontSize: 24))
    .AddConfiguredField(new AnkiField("English"))
    .AddConfiguredField(new AnkiField("Image"))
    .AddTemplate(
        "German -> English",
        "<div class='word'>{{German}}</div>{{Image}}",
        "{{FrontSide}}<hr id='answer'>{{English}}")
    .AddTemplate(
        "English -> German",
        "<div class='word'>{{English}}</div>",
        "{{FrontSide}}<hr id='answer'>{{German}}<br>{{Image}}");

vocabulary.Css = ".card{text-align:center;font-family:Arial}.word{font-size:2rem}";

deck.AddNote(vocabulary, new Dictionary<string, string>
{
    ["German"] = "die Katze",
    ["English"] = "the cat",
    ["Image"] = "<img src='cat.png'>"
}, tags: ["german", "animals"]);
@endcode

Register media once and refer to its filename from field HTML:

@code{.cs}
await deck.Media.AddFileAsync("cat.png", "assets/cat.png");
@endcode

Media registration records a SHA-256 digest. A path-backed file that changes before export is rejected rather than
silently packaging different bytes.

## Validate and export

@code{.cs}
var result = AnkiValidator.Validate(deck);
foreach (var diagnostic in result.Diagnostics)
{
    Console.WriteLine($"{diagnostic.Severity} {diagnostic.Code}: {diagnostic.Message}");
}

if (!result.IsValid)
{
    throw new InvalidOperationException("The deck is not safe to export.");
}

await AnkiPackageWriter.WriteAsync(deck, "German.apkg");
@endcode

The writer validates again, so callers cannot accidentally bypass release-blocking diagnostics. Native JSON is useful
for lossless library-to-library interchange; the CrowdAnki-inspired adapter is intentionally a smaller, lossy subset.
*/

/*!
@page anki_concepts Anki concepts

Anki separates stored information from study prompts:

- An AnkiDeck owns notes, nested decks, and registered media.
- An AnkiNote stores ordered field values, tags, a stable import GUID, and generated cards.
- An AnkiNoteType defines fields, CSS, and one or more AnkiCardTemplate objects.
- An AnkiCard is one study direction generated from a template. Scheduling and review history belong to the card,
  not to the note.

A Basic note type normally has `Front` and `Back` fields and one template, so one note creates one card. A
Basic-and-reversed type has two templates and creates two cards. A Cloze type creates one card for each distinct
positive `cN` marker found in its text.

## Hierarchies and identity

Create child decks with AnkiDeck.AddSubdeck(). Anki's displayed hierarchy uses `Parent::Child` names, while AnkiIO
stores each local segment separately and traverses the hierarchy in stable insertion order. Deck, note, card, and note
type identifiers are 64-bit values. Let AnkiIO generate them unless importing known identities.

## Scheduling

AnkiScheduling.New is the safest state for generated cards. Advanced scheduling values are preserved by native JSON
and legacy APKG I/O, but queue and card-type combinations must be consistent. Suspended and buried queues retain the
underlying card type; the meaning of `Due` depends on the active queue. Never synthesize review history merely to make
a card appear mature.

The mutable object graph is designed for single-operation construction and editing. It is not safe for concurrent
mutation unless a member explicitly documents otherwise.
*/

/*!
@page formats_and_safety Formats, compatibility, and safety

## Verified compatibility

The release target is Anki 26.05 (build `e64c6b1a`), collection schema 18, and the v3 scheduler. AnkiIO-generated
legacy `collection.anki2` APKG packages were imported through an isolated Anki 26.05 backend test. Basic and explicit
scheduling state, nested content, and media were verified without opening a normal profile for writes.

| Capability | Support boundary |
|---|---|
| Native JSON v1 | Deterministic semantic round trips for the modeled graph; media bytes are not embedded |
| Legacy APKG read/write | Supported for modeled fields and accepted by the verified Anki 26.05 target |
| CrowdAnki-inspired JSON | Partial interchange; scheduling and several CrowdAnki-specific concepts are omitted |
| Modern `collection.anki21b` | Detected and rejected; not read or emitted |
| Live schema-18 collection writes | Unsupported |
| Scheduling/review history | Common scheduling fields supported; review-log and FSRS state are partial |
| Deck configuration | Safe defaults emitted; arbitrary presets are not preserved |

An IAnkiVersionAdapter describes evidence-backed capabilities. A matching version number alone is not proof that a
new Anki release uses identical collection, scheduler, or package formats.

## Hostile package handling

Treat every package as hostile. AnkiPackageLimits caps entry count, individual and aggregate uncompressed sizes,
collection size, and compression ratio. The reader rejects duplicate entries, unsafe paths, malformed media maps,
and unsupported collection representations before semantic import.

AnkiIO does not sanitize HTML, execute card JavaScript, render media, or provide malware scanning. Apply normal OS
isolation and content scanning when processing untrusted packages.

## Never write a live profile

Package APIs operate on caller-selected files and streams. Do not point temporary tooling at a normal profile database
or `collection.media` directory. Local compatibility tests are opt-in and use uniquely generated workspaces:

@code{.sh}
$env:ANKI_LOCAL_COMPATIBILITY = "1"
dotnet test --filter Category=LocalAnkiCompatibility
@endcode

Repeat the isolated acceptance suite after every Anki update before broadening the documented compatibility range.
*/

/*!
@namespace AnkiIO

Public deck, note, card, scheduling, validation, media, JSON, compatibility, and guarded package APIs.
*/
