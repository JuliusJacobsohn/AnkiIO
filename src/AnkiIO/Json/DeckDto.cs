using System.Text.Json;
using System.Text.Json.Serialization;

namespace AnkiIO;

internal sealed class DeckDto
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public SortedDictionary<string, string>? Metadata { get; set; } = new(StringComparer.Ordinal);

    public List<NoteDto>? Notes { get; set; } = [];

    public List<DeckDto>? Subdecks { get; set; } = [];

    [JsonExtensionData]
    public SortedDictionary<string, JsonElement>? ExtensionData { get; set; }
}
