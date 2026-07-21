using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace AnkiIO;

internal static class LegacyCollectionDatabase
{
    private const char FieldSeparator = '\u001f';

    public static async Task WriteAsync(string databasePath, IReadOnlyList<AnkiDeck> roots, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, SchemaSql, cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var decks = roots.SelectMany(root => root.Traverse()).ToArray();
        var names = BuildFullNames(roots);
        var noteTypes = decks.SelectMany(deck => deck.Notes).Select(note => note.NoteType).GroupBy(type => type.Id).Select(group => group.First()).ToArray();
        var nowMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var collection = connection.CreateCommand();
        collection.Transaction = (SqliteTransaction)transaction;
        collection.CommandText = "INSERT INTO col VALUES (1,$crt,$mod,$scm,11,0,0,0,$conf,$models,$decks,$dconf,'{}')";
        collection.Parameters.AddWithValue("$crt", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        collection.Parameters.AddWithValue("$mod", nowMilliseconds);
        collection.Parameters.AddWithValue("$scm", nowMilliseconds);
        collection.Parameters.AddWithValue("$conf", DefaultCollectionConfiguration);
        collection.Parameters.AddWithValue("$models", BuildModelsJson(noteTypes, nowMilliseconds));
        collection.Parameters.AddWithValue("$decks", BuildDecksJson(decks, names, nowMilliseconds));
        collection.Parameters.AddWithValue("$dconf", DefaultDeckConfiguration);
        await collection.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        foreach (var deck in decks)
        {
            foreach (var note in deck.Notes)
            {
                await InsertNoteAsync(connection, (SqliteTransaction)transaction, note, nowMilliseconds, cancellationToken).ConfigureAwait(false);
                foreach (var card in note.Cards)
                {
                    await InsertCardAsync(connection, (SqliteTransaction)transaction, card, nowMilliseconds, cancellationToken).ConfigureAwait(false);
                    foreach (var review in card.ReviewHistory)
                    {
                        await InsertReviewAsync(connection, (SqliteTransaction)transaction, card.Id, review, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<IReadOnlyList<AnkiDeck>> ReadAsync(string databasePath, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var col = connection.CreateCommand();
        col.CommandText = "SELECT ver, models, decks FROM col LIMIT 1";
        await using var reader = await col.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidDataException("The package collection has no col row.");
        }

        var version = reader.GetInt32(0);
        if (version is not 11 and not 18)
        {
            throw new NotSupportedException($"Collection schema {version} is unsupported; this adapter supports legacy JSON metadata schema 11 packages.");
        }

        var modelsJson = reader.GetString(1);
        var decksJson = reader.GetString(2);
        if (string.IsNullOrWhiteSpace(modelsJson) || string.IsNullOrWhiteSpace(decksJson) || modelsJson[0] != '{' || decksJson[0] != '{')
        {
            throw new NotSupportedException("The collection uses protobuf metadata. Modern collection.anki21b reading requires a future schema-18 adapter.");
        }

        await reader.DisposeAsync().ConfigureAwait(false);
        var types = ParseModels(modelsJson);
        var (roots, deckById) = ParseDecks(decksJson);
        var notes = new Dictionary<long, AnkiNote>();

        var noteCommand = connection.CreateCommand();
        noteCommand.CommandText = "SELECT id,guid,mid,tags,flds FROM notes ORDER BY id";
        await using (var noteReader = await noteCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await noteReader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var id = noteReader.GetInt64(0);
                var typeId = noteReader.GetInt64(2);
                if (!types.TryGetValue(typeId, out var type))
                {
                    throw new InvalidDataException($"Note {id} references missing note type {typeId}.");
                }

                var values = noteReader.GetString(4).Split(FieldSeparator);
                if (values.Length != type.Fields.Count)
                {
                    throw new InvalidDataException($"Note {id} field count does not match note type {typeId}.");
                }

                var fields = type.Fields.Select((field, index) => (field.Name, values[index])).ToDictionary(pair => pair.Name, pair => pair.Item2, StringComparer.Ordinal);
                var tags = noteReader.GetString(3).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                notes.Add(id, new AnkiNote(type, fields, tags, id, noteReader.GetString(1)));
            }
        }

        var cardsByNote = new Dictionary<long, List<AnkiCard>>();
        var cardCommand = connection.CreateCommand();
        cardCommand.CommandText = "SELECT id,nid,did,ord,type,queue,due,ivl,factor,reps,lapses,left,odue,odid,flags,data FROM cards ORDER BY id";
        await using (var cardReader = await cardCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await cardReader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var noteId = cardReader.GetInt64(1);
                var scheduling = new AnkiScheduling
                {
                    Type = (AnkiCardType)cardReader.GetInt32(4),
                    Queue = (AnkiCardQueue)cardReader.GetInt32(5),
                    Due = cardReader.GetInt64(6),
                    Interval = cardReader.GetInt32(7),
                    EaseFactor = cardReader.GetInt32(8),
                    Repetitions = cardReader.GetInt32(9),
                    Lapses = cardReader.GetInt32(10),
                    RemainingSteps = cardReader.GetInt32(11),
                    OriginalDue = cardReader.GetInt64(12),
                    OriginalDeckId = cardReader.GetInt64(13),
                    CustomData = cardReader.GetString(15),
                };
                var card = new AnkiCard(cardReader.GetInt64(0), noteId, cardReader.GetInt64(2), cardReader.GetInt32(3), scheduling) { Flag = cardReader.GetInt32(14) & 7 };
                cardsByNote.GetOrAdd(noteId).Add(card);
            }
        }

        foreach (var pair in notes)
        {
            var cards = cardsByNote.GetValueOrDefault(pair.Key) ?? [];
            pair.Value.RestoreCards(cards);
            var targetDeckId = cards.FirstOrDefault()?.DeckId ?? deckById.Keys.First();
            if (!deckById.TryGetValue(targetDeckId, out var target))
            {
                throw new InvalidDataException($"Note {pair.Key} card references missing deck {targetDeckId}.");
            }

            target.AddExistingNote(pair.Value);
        }

        return roots;
    }

    private static async Task InsertNoteAsync(SqliteConnection connection, SqliteTransaction transaction, AnkiNote note, long now, CancellationToken cancellationToken)
    {
        var values = string.Join(FieldSeparator, note.NoteType.Fields.Select(field => note.Fields[field.Name]));
        var first = note.NoteType.Fields.Count == 0 ? string.Empty : note.Fields[note.NoteType.Fields[0].Name];
#pragma warning disable CA5350 // Anki's legacy schema mandates the first 32 bits of SHA-1 as a lookup checksum, not for security.
        var checksum = Convert.ToInt64(Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(first)))[..8], 16);
#pragma warning restore CA5350
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO notes VALUES ($id,$guid,$mid,$mod,-1,$tags,$flds,$sfld,$csum,0,'')";
        command.Parameters.AddWithValue("$id", note.Id);
        command.Parameters.AddWithValue("$guid", note.Guid);
        command.Parameters.AddWithValue("$mid", note.NoteType.Id);
        command.Parameters.AddWithValue("$mod", now / 1000);
        command.Parameters.AddWithValue("$tags", note.Tags.Count == 0 ? string.Empty : " " + string.Join(' ', note.Tags) + " ");
        command.Parameters.AddWithValue("$flds", values);
        command.Parameters.AddWithValue("$sfld", first);
        command.Parameters.AddWithValue("$csum", checksum);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertCardAsync(SqliteConnection connection, SqliteTransaction transaction, AnkiCard card, long now, CancellationToken cancellationToken)
    {
        var value = card.Scheduling;
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO cards VALUES ($id,$nid,$did,$ord,$mod,-1,$type,$queue,$due,$ivl,$factor,$reps,$lapses,$left,$odue,$odid,$flags,$data)";
        command.Parameters.AddWithValue("$id", card.Id);
        command.Parameters.AddWithValue("$nid", card.NoteId);
        command.Parameters.AddWithValue("$did", card.DeckId);
        command.Parameters.AddWithValue("$ord", card.TemplateOrdinal);
        command.Parameters.AddWithValue("$mod", now / 1000);
        command.Parameters.AddWithValue("$type", (int)value.Type);
        command.Parameters.AddWithValue("$queue", (int)value.Queue);
        command.Parameters.AddWithValue("$due", value.Due);
        command.Parameters.AddWithValue("$ivl", value.Interval);
        command.Parameters.AddWithValue("$factor", value.EaseFactor);
        command.Parameters.AddWithValue("$reps", value.Repetitions);
        command.Parameters.AddWithValue("$lapses", value.Lapses);
        command.Parameters.AddWithValue("$left", value.RemainingSteps);
        command.Parameters.AddWithValue("$odue", value.OriginalDue);
        command.Parameters.AddWithValue("$odid", value.OriginalDeckId);
        command.Parameters.AddWithValue("$flags", card.Flag);
        command.Parameters.AddWithValue("$data", value.CustomData);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertReviewAsync(SqliteConnection connection, SqliteTransaction transaction, long cardId, AnkiReviewLog review, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO revlog VALUES ($id,$cid,-1,$ease,$ivl,$last,$factor,$time,$type)";
        command.Parameters.AddWithValue("$id", review.Id);
        command.Parameters.AddWithValue("$cid", cardId);
        command.Parameters.AddWithValue("$ease", review.Ease);
        command.Parameters.AddWithValue("$ivl", review.Interval);
        command.Parameters.AddWithValue("$last", review.PreviousInterval);
        command.Parameters.AddWithValue("$factor", review.EaseFactor);
        command.Parameters.AddWithValue("$time", (long)review.AnswerTime.TotalMilliseconds);
        command.Parameters.AddWithValue("$type", review.ReviewType);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static Dictionary<long, string> BuildFullNames(IEnumerable<AnkiDeck> roots)
    {
        var result = new Dictionary<long, string>();
        void Walk(AnkiDeck deck, string prefix)
        {
            var name = prefix.Length == 0 ? deck.Name : prefix + "::" + deck.Name;
            result.Add(deck.Id, name);
            foreach (var child in deck.Subdecks)
            {
                Walk(child, name);
            }
        }

        foreach (var root in roots)
        {
            Walk(root, string.Empty);
        }

        return result;
    }

    private static string BuildModelsJson(IEnumerable<AnkiNoteType> types, long now)
    {
        var root = new JsonObject();
        foreach (var type in types.OrderBy(type => type.Id))
        {
            root[type.Id.ToString(CultureInfo.InvariantCulture)] = new JsonObject
            {
                ["id"] = type.Id,
                ["name"] = type.Name,
                ["type"] = (int)type.Kind,
                ["mod"] = now / 1000,
                ["usn"] = -1,
                ["sortf"] = 0,
                ["did"] = null,
                ["css"] = type.Css,
                ["latexPre"] = string.Empty,
                ["latexPost"] = string.Empty,
                ["latexsvg"] = false,
                ["flds"] = new JsonArray(type.Fields.Select((field, index) => new JsonObject { ["name"] = field.Name, ["ord"] = index, ["sticky"] = field.IsSticky, ["rtl"] = field.IsRightToLeft, ["font"] = field.Font, ["size"] = field.FontSize, ["media"] = new JsonArray() }).ToArray()),
                ["tmpls"] = new JsonArray(type.Templates.Select((template, index) => new JsonObject { ["name"] = template.Name, ["ord"] = index, ["qfmt"] = template.QuestionFormat, ["afmt"] = template.AnswerFormat, ["bqfmt"] = template.BrowserQuestionFormat ?? string.Empty, ["bafmt"] = template.BrowserAnswerFormat ?? string.Empty, ["did"] = null }).ToArray()),
                ["req"] = new JsonArray(type.Templates.Select((_, index) => new JsonArray(index, "any", new JsonArray(0))).ToArray()),
            };
        }

        return root.ToJsonString();
    }

    private static string BuildDecksJson(IEnumerable<AnkiDeck> decks, IReadOnlyDictionary<long, string> names, long now)
    {
        var root = new JsonObject();
        foreach (var deck in decks.OrderBy(deck => deck.Id))
        {
            root[deck.Id.ToString(CultureInfo.InvariantCulture)] = new JsonObject { ["id"] = deck.Id, ["name"] = names[deck.Id], ["mod"] = now / 1000, ["usn"] = -1, ["desc"] = deck.Description, ["dyn"] = 0, ["conf"] = 1, ["collapsed"] = false, ["browserCollapsed"] = false, ["extendNew"] = 0, ["extendRev"] = 0, ["newToday"] = new JsonArray(0, 0), ["revToday"] = new JsonArray(0, 0), ["lrnToday"] = new JsonArray(0, 0), ["timeToday"] = new JsonArray(0, 0) };
        }

        return root.ToJsonString();
    }

    private static Dictionary<long, AnkiNoteType> ParseModels(string json)
    {
        var result = new Dictionary<long, AnkiNoteType>();
        foreach (var pair in JsonNode.Parse(json)?.AsObject() ?? throw new InvalidDataException("Invalid models JSON."))
        {
            var value = pair.Value?.AsObject() ?? throw new InvalidDataException("Null note type.");
            var id = value["id"]?.GetValue<long>() ?? long.Parse(pair.Key, CultureInfo.InvariantCulture);
            var type = new AnkiNoteType(value["name"]?.GetValue<string>() ?? "Unnamed", (AnkiNoteTypeKind)(value["type"]?.GetValue<int>() ?? 0), id) { Css = value["css"]?.GetValue<string>() ?? string.Empty };
            foreach (var fieldNode in value["flds"]?.AsArray() ?? [])
            {
                var field = fieldNode?.AsObject() ?? throw new InvalidDataException("Null field.");
                type.AddConfiguredField(new AnkiField(
                    field["name"]?.GetValue<string>() ?? throw new InvalidDataException("Unnamed field."),
                    IsRightToLeft: field["rtl"]?.GetValue<bool>() ?? false,
                    IsSticky: field["sticky"]?.GetValue<bool>() ?? false,
                    Font: field["font"]?.GetValue<string>() ?? "Arial",
                    FontSize: field["size"]?.GetValue<int>() ?? 20));
            }

            foreach (var templateNode in value["tmpls"]?.AsArray() ?? [])
            {
                var template = templateNode?.AsObject() ?? throw new InvalidDataException("Null template.");
                var browserQuestion = template["bqfmt"]?.GetValue<string>();
                var browserAnswer = template["bafmt"]?.GetValue<string>();
                type.AddConfiguredTemplate(new AnkiCardTemplate(
                    template["name"]?.GetValue<string>() ?? "Card",
                    template["qfmt"]?.GetValue<string>() ?? string.Empty,
                    template["afmt"]?.GetValue<string>() ?? string.Empty,
                    string.IsNullOrEmpty(browserQuestion) ? null : browserQuestion,
                    string.IsNullOrEmpty(browserAnswer) ? null : browserAnswer));
            }

            result.Add(id, type);
        }

        return result;
    }

    private static (IReadOnlyList<AnkiDeck> Roots, Dictionary<long, AnkiDeck> ById) ParseDecks(string json)
    {
        var values = (JsonNode.Parse(json)?.AsObject() ?? throw new InvalidDataException("Invalid decks JSON.")).Select(pair => pair.Value?.AsObject() ?? throw new InvalidDataException("Null deck.")).Select(value => new { Id = value["id"]!.GetValue<long>(), Name = value["name"]!.GetValue<string>(), Description = value["desc"]?.GetValue<string>() ?? string.Empty }).OrderBy(value => value.Name.Count(character => character == ':')).ThenBy(value => value.Name, StringComparer.Ordinal).ToArray();
        var byName = new Dictionary<string, AnkiDeck>(StringComparer.Ordinal);
        var byId = new Dictionary<long, AnkiDeck>();
        var roots = new List<AnkiDeck>();
        foreach (var value in values)
        {
            var parts = value.Name.Split("::", StringSplitOptions.None);
            var deck = new AnkiDeck(parts[^1], value.Id) { Description = value.Description };
            byName.Add(value.Name, deck);
            byId.Add(value.Id, deck);
            var parentName = string.Join("::", parts[..^1]);
            if (parentName.Length == 0) roots.Add(deck); else if (byName.TryGetValue(parentName, out var parent)) parent.AddExistingSubdeck(deck); else throw new InvalidDataException($"Deck '{value.Name}' has no parent '{parentName}'.");
        }

        return (roots, byId);
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private const string SchemaSql = """
        PRAGMA journal_mode=DELETE;
        CREATE TABLE col (id integer primary key, crt integer not null, mod integer not null, scm integer not null, ver integer not null, dty integer not null, usn integer not null, ls integer not null, conf text not null, models text not null, decks text not null, dconf text not null, tags text not null);
        CREATE TABLE notes (id integer primary key, guid text not null, mid integer not null, mod integer not null, usn integer not null, tags text not null, flds text not null, sfld integer not null, csum integer not null, flags integer not null, data text not null);
        CREATE TABLE cards (id integer primary key, nid integer not null, did integer not null, ord integer not null, mod integer not null, usn integer not null, type integer not null, queue integer not null, due integer not null, ivl integer not null, factor integer not null, reps integer not null, lapses integer not null, left integer not null, odue integer not null, odid integer not null, flags integer not null, data text not null);
        CREATE TABLE revlog (id integer primary key, cid integer not null, usn integer not null, ease integer not null, ivl integer not null, lastIvl integer not null, factor integer not null, time integer not null, type integer not null);
        CREATE TABLE graves (usn integer not null, oid integer not null, type integer not null);
        CREATE INDEX ix_notes_usn ON notes (usn); CREATE INDEX ix_cards_usn ON cards (usn); CREATE INDEX ix_revlog_usn ON revlog (usn); CREATE INDEX ix_cards_nid ON cards (nid); CREATE INDEX ix_cards_sched ON cards (did, queue, due); CREATE INDEX ix_revlog_cid ON revlog (cid); CREATE INDEX ix_notes_csum ON notes (csum);
        """;

    private const string DefaultCollectionConfiguration = "{\"activeDecks\":[1],\"curDeck\":1,\"newSpread\":0,\"collapseTime\":1200,\"timeLim\":0,\"estTimes\":true,\"dueCounts\":true,\"curModel\":null,\"nextPos\":1,\"sortType\":\"noteFld\",\"sortBackwards\":false,\"addToCur\":true,\"dayLearnFirst\":false," + "\"schedVer\":2}";
    private const string DefaultDeckConfiguration = "{\"1\":{\"id\":1,\"name\":\"Default\",\"mod\":0,\"usn\":0,\"maxTaken\":60,\"autoplay\":true,\"timer\":0,\"replayq\":true,\"new\":{\"delays\":[1,10],\"ints\":[1,4],\"initialFactor\":2500,\"separate\":true,\"order\":1,\"perDay\":20,\"bury\":true},\"rev\":{\"perDay\":200,\"ease4\":1.3,\"fuzz\":0.05,\"ivlFct\":1,\"maxIvl\":36500,\"bury\":true,\"hardFactor\":1.2},\"lapse\":{\"delays\":[10],\"mult\":0,\"minInt\":1,\"leechFails\":8,\"leechAction\":0}}}";

    private static List<TValue> GetOrAdd<TKey, TValue>(this Dictionary<TKey, List<TValue>> dictionary, TKey key) where TKey : notnull
    {
        if (!dictionary.TryGetValue(key, out var list)) dictionary.Add(key, list = []);
        return list;
    }
}
