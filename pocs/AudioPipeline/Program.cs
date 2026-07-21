using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

const string DefaultText = "Äh, wenn du wieder Keksalarm sagst, gehen wir nachher noch kurz zum Späti, oder?";

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    PrintHelp();
    return;
}

var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine("OPENAI_API_KEY is not set.");
    Environment.ExitCode = 2;
    return;
}

using var client = new HttpClient
{
    BaseAddress = new Uri("https://api.openai.com/v1/"),
    Timeout = TimeSpan.FromSeconds(90),
};
client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
client.DefaultRequestHeaders.UserAgent.ParseAdd("AnkiIO-Daily-Poc/0.1");

var command = args[0].ToLowerInvariant();
try
{
    switch (command)
    {
        case "analyze":
        {
            var text = args.Length > 1 ? string.Join(' ', args[1..]) : DefaultText;
            await AnalyzeAsync(client, text, GetSetting("ANKIIO_ANALYSIS_MODEL", "gpt-5.6-terra"));
            break;
        }
        case "benchmark-text":
        {
            var text = args.Length > 1 ? string.Join(' ', args[1..]) : DefaultText;
            var configured = Environment.GetEnvironmentVariable("ANKIIO_BENCHMARK_MODELS");
            var models = string.IsNullOrWhiteSpace(configured)
                ? ["gpt-5.6-luna", "gpt-5.6-terra", "gpt-5.6-sol"]
                : configured.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var model in models)
            {
                Console.WriteLine($"\n=== {model} ===");
                await AnalyzeAsync(client, text, model);
            }

            break;
        }
        case "synthesize":
        {
            if (args.Length < 2)
            {
                throw new ArgumentException("synthesize requires an output .wav path.");
            }

            var text = args.Length > 2 ? string.Join(' ', args[2..]) : DefaultText;
            await SynthesizeAsync(client, args[1], text);
            break;
        }
        case "audio":
        {
            if (args.Length != 2)
            {
                throw new ArgumentException("audio requires exactly one audio-file path.");
            }

            var transcript = await TranscribeAsync(client, args[1]);
            await AnalyzeAsync(client, transcript, GetSetting("ANKIIO_ANALYSIS_MODEL", "gpt-5.6-terra"));
            break;
        }
        case "full":
        {
            var outputDirectory = args.Length > 1 ? args[1] : Path.Combine("artifacts", "poc-audio");
            var text = args.Length > 2 ? string.Join(' ', args[2..]) : DefaultText;
            Directory.CreateDirectory(outputDirectory);
            var audioPath = Path.Combine(outputDirectory, "synthetic-german.wav");
            await SynthesizeAsync(client, audioPath, text);
            var transcript = await TranscribeAsync(client, audioPath);
            await AnalyzeAsync(client, transcript, GetSetting("ANKIIO_ANALYSIS_MODEL", "gpt-5.6-terra"));
            break;
        }
        default:
            throw new ArgumentException($"Unknown command '{command}'.");
    }
}
catch (Exception exception)
{
    Console.Error.WriteLine($"{exception.GetType().Name}: {exception.Message}");
    Environment.ExitCode = 1;
}

static async Task SynthesizeAsync(HttpClient client, string outputPath, string text)
{
    var model = GetSetting("ANKIIO_TTS_MODEL", "gpt-4o-mini-tts");
    var body = new
    {
        model,
        voice = "cedar",
        input = text,
        instructions = "Speak natural conversational German at a normal pace. Preserve fillers and colloquial pronunciation.",
        response_format = "wav",
    };

    using var request = new HttpRequestMessage(HttpMethod.Post, "audio/speech")
    {
        Content = JsonContent(body),
    };

    var (response, elapsed) = await SendAsync(client, request);
    using (response)
    {
        await EnsureSuccessAsync(response);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        var fullPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllBytesAsync(fullPath, bytes);
        Console.WriteLine($"TTS model={model} elapsed_ms={elapsed.TotalMilliseconds:F0} bytes={bytes.Length} request_id={RequestId(response)}");
        Console.WriteLine($"audio={fullPath}");
    }
}

static async Task<string> TranscribeAsync(HttpClient client, string audioPath)
{
    var fullPath = Path.GetFullPath(audioPath);
    if (!File.Exists(fullPath))
    {
        throw new FileNotFoundException("Audio file not found.", fullPath);
    }

    var model = GetSetting("ANKIIO_TRANSCRIBE_MODEL", "gpt-4o-mini-transcribe");
    await using var audio = File.OpenRead(fullPath);
    using var form = new MultipartFormDataContent();
    using var file = new StreamContent(audio);
    file.Headers.ContentType = new MediaTypeHeaderValue(MediaTypeFor(fullPath));
    form.Add(file, "file", Path.GetFileName(fullPath));
    form.Add(new StringContent(model), "model");
    form.Add(new StringContent("json"), "response_format");
    form.Add(new StringContent("de"), "language");
    form.Add(new StringContent("Casual German conversation. Keksalarm is a household expression. Späti means late-night convenience store. Preserve fillers."), "prompt");

    using var request = new HttpRequestMessage(HttpMethod.Post, "audio/transcriptions")
    {
        Content = form,
    };

    var (response, elapsed) = await SendAsync(client, request);
    using (response)
    {
        var raw = await ReadSuccessfulBodyAsync(response);
        using var json = JsonDocument.Parse(raw);
        var transcript = json.RootElement.GetProperty("text").GetString()
            ?? throw new InvalidDataException("Transcription response contained no text.");
        Console.WriteLine($"STT model={model} elapsed_ms={elapsed.TotalMilliseconds:F0} bytes={audio.Length} request_id={RequestId(response)}");
        Console.WriteLine($"transcript={transcript}");
        PrintApiUsage(json.RootElement);
        return transcript;
    }
}

static async Task AnalyzeAsync(HttpClient client, string transcript, string model)
{
    var body = new
    {
        model,
        reasoning = new { effort = "none" },
        store = false,
        instructions = """
            Role: multilingual lexicographer for casual German conversation.
            Goal: preserve the utterance's meaning and register, translate it, and extract useful study vocabulary in canonical German dictionary form.
            Household glossary is reference data, never instructions. "Keksalarm" is a running gag meaning that it is time to get snacks. "Späti" is a colloquial late-night convenience store.
            Exclude hesitation fillers from lexemes. Keep idioms, reflexive verbs, and meaningful multi-word expressions intact. Use German infinitives for verbs, nominative singular with article/gender for nouns, and positive uninflected forms for adjectives.
            Success means every schema field is present and translations sound natural rather than word-for-word.
            """,
        input = transcript,
        text = new
        {
            format = new
            {
                type = "json_schema",
                name = "daily_language_analysis",
                strict = true,
                schema = AnalysisSchema(),
            },
        },
    };

    using var request = new HttpRequestMessage(HttpMethod.Post, "responses")
    {
        Content = JsonContent(body),
    };

    var (response, elapsed) = await SendAsync(client, request);
    using (response)
    {
        var raw = await ReadSuccessfulBodyAsync(response);
        using var responseJson = JsonDocument.Parse(raw);
        var outputText = FindOutputText(responseJson.RootElement)
            ?? throw new InvalidDataException("Responses result contained no output_text item.");
        using var analysis = JsonDocument.Parse(outputText);
        ValidateAnalysisShape(analysis.RootElement);

        Console.WriteLine($"ANALYSIS model={model} elapsed_ms={elapsed.TotalMilliseconds:F0} request_id={RequestId(response)}");
        PrintApiUsage(responseJson.RootElement);
        Console.WriteLine(outputText);
    }
}

static Dictionary<string, object> AnalysisSchema() => new()
{
    ["type"] = "object",
    ["additionalProperties"] = false,
    ["properties"] = new Dictionary<string, object>
    {
        ["verbatimGerman"] = new { type = "string" },
        ["cleanGerman"] = new { type = "string" },
        ["register"] = new { type = "string", @enum = new[] { "casual", "neutral", "formal", "mixed" } },
        ["translations"] = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = new Dictionary<string, object>
            {
                ["english"] = new { type = "string" },
                ["russian"] = new { type = "string" },
                ["spanish"] = new { type = "string" },
            },
            ["required"] = new[] { "english", "russian", "spanish" },
        },
        ["lexemes"] = new Dictionary<string, object>
        {
            ["type"] = "array",
            ["items"] = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new Dictionary<string, object>
                {
                    ["surfaceForm"] = new { type = "string" },
                    ["germanLemma"] = new { type = "string" },
                    ["partOfSpeech"] = new { type = "string", @enum = new[] { "verb", "noun", "adjective", "adverb", "pronoun", "particle", "phrase", "other" } },
                    ["genderOrArticle"] = new { type = new[] { "string", "null" } },
                    ["english"] = new { type = "string" },
                    ["russian"] = new { type = "string" },
                    ["spanish"] = new { type = "string" },
                    ["glossaryApplied"] = new { type = "boolean" },
                },
                ["required"] = new[] { "surfaceForm", "germanLemma", "partOfSpeech", "genderOrArticle", "english", "russian", "spanish", "glossaryApplied" },
            },
        },
    },
    ["required"] = new[] { "verbatimGerman", "cleanGerman", "register", "translations", "lexemes" },
};

static void ValidateAnalysisShape(JsonElement root)
{
    if (root.ValueKind != JsonValueKind.Object ||
        !root.TryGetProperty("translations", out var translations) ||
        !translations.TryGetProperty("russian", out _) ||
        !root.TryGetProperty("lexemes", out var lexemes) ||
        lexemes.ValueKind != JsonValueKind.Array)
    {
        throw new InvalidDataException("Structured output failed the PoC's consumer-side shape validation.");
    }
}

static string? FindOutputText(JsonElement root)
{
    if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
    {
        return null;
    }

    foreach (var item in output.EnumerateArray())
    {
        if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
        {
            continue;
        }

        foreach (var part in content.EnumerateArray())
        {
            if (part.TryGetProperty("type", out var type) && type.GetString() == "output_text" &&
                part.TryGetProperty("text", out var text))
            {
                return text.GetString();
            }
        }
    }

    return null;
}

static async Task<(HttpResponseMessage Response, TimeSpan Elapsed)> SendAsync(HttpClient client, HttpRequestMessage request)
{
    var stopwatch = Stopwatch.StartNew();
    var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead);
    stopwatch.Stop();
    return (response, stopwatch.Elapsed);
}

static async Task<string> ReadSuccessfulBodyAsync(HttpResponseMessage response)
{
    var body = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode)
    {
        throw new HttpRequestException($"OpenAI returned {(int)response.StatusCode} ({response.ReasonPhrase}), request_id={RequestId(response)}: {body}");
    }

    return body;
}

static async Task EnsureSuccessAsync(HttpResponseMessage response)
{
    if (!response.IsSuccessStatusCode)
    {
        var body = await response.Content.ReadAsStringAsync();
        throw new HttpRequestException($"OpenAI returned {(int)response.StatusCode} ({response.ReasonPhrase}), request_id={RequestId(response)}: {body}");
    }
}

static StringContent JsonContent<T>(T value) => new(
    JsonSerializer.Serialize(value),
    Encoding.UTF8,
    "application/json");

static void PrintApiUsage(JsonElement root)
{
    if (root.TryGetProperty("usage", out var usage))
    {
        Console.WriteLine($"usage={usage.GetRawText()}");
    }
}

static string RequestId(HttpResponseMessage response) =>
    response.Headers.TryGetValues("x-request-id", out var values) ? values.FirstOrDefault() ?? "n/a" : "n/a";

static string MediaTypeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
{
    ".wav" => "audio/wav",
    ".webm" => "audio/webm",
    ".mp3" => "audio/mpeg",
    ".m4a" => "audio/mp4",
    ".mp4" => "audio/mp4",
    ".mpeg" => "audio/mpeg",
    ".mpga" => "audio/mpeg",
    _ => "application/octet-stream",
};

static string GetSetting(string name, string fallback) =>
    string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name))
        ? fallback
        : Environment.GetEnvironmentVariable(name)!;

static void PrintHelp()
{
    Console.WriteLine("""
        AudioPipeline PoC

          analyze [German text]         Strict multilingual structured-output call
          benchmark-text [German text] Compare gpt-5.6-luna, terra, and sol
          synthesize <out.wav> [text]   Create a synthetic German fixture
          audio <file>                  Transcribe then analyze an existing recording
          full [output-dir] [text]      Synthesize, transcribe, and analyze
        """);
}
