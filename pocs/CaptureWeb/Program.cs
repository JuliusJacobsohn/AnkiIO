using System.Security.Cryptography;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails();

var app = builder.Build();
app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseDefaultFiles();
app.UseStaticFiles();

const long maxBytes = 25 * 1024 * 1024;

app.MapPost("/api/recordings", async (HttpRequest request, IWebHostEnvironment environment, CancellationToken cancellationToken) =>
{
    if (!request.HasFormContentType)
    {
        return Results.BadRequest(new { error = "multipart/form-data required" });
    }

    var form = await request.ReadFormAsync(cancellationToken);
    var audio = form.Files.GetFile("audio");
    if (audio is null || audio.Length == 0)
    {
        return Results.BadRequest(new { error = "non-empty audio field required" });
    }

    if (audio.Length > maxBytes)
    {
        return Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge, title: "Recording exceeds 25 MB PoC limit.");
    }

    var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "audio/webm", "audio/mp4", "audio/mpeg", "audio/wav", "audio/x-wav",
    };
    var mediaType = audio.ContentType.Split(';', 2)[0].Trim();
    if (!allowed.Contains(mediaType))
    {
        return Results.BadRequest(new { error = $"unsupported media type: {audio.ContentType}" });
    }

    var extension = mediaType switch
    {
        "audio/webm" => ".webm",
        "audio/mp4" => ".m4a",
        "audio/mpeg" => ".mp3",
        _ => ".wav",
    };

    var directory = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "..", "..", "artifacts", "capture-poc"));
    Directory.CreateDirectory(directory);
    var id = Guid.NewGuid();
    var finalPath = Path.Combine(directory, $"{id:N}{extension}");
    var temporaryPath = finalPath + ".partial";

    try
    {
        await using (var input = audio.OpenReadStream())
        await using (var output = File.Create(temporaryPath))
        {
            await input.CopyToAsync(output, cancellationToken);
        }

        File.Move(temporaryPath, finalPath);
        await using var stored = File.OpenRead(finalPath);
        var checksum = Convert.ToHexString(await SHA256.HashDataAsync(stored, cancellationToken)).ToLowerInvariant();

        return Results.Ok(new
        {
            id,
            audio.FileName,
            mediaType,
            audio.Length,
            sha256 = checksum,
            receivedAt = DateTimeOffset.UtcNow,
        });
    }
    finally
    {
        if (File.Exists(temporaryPath))
        {
            File.Delete(temporaryPath);
        }
    }
}).DisableAntiforgery();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok", now = DateTimeOffset.UtcNow }));
app.MapFallbackToFile("index.html");

app.Run();
