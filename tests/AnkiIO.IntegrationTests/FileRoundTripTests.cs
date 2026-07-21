using Xunit;

namespace AnkiIO.IntegrationTests;

public sealed class FileRoundTripTests
{
    [Fact]
    public async Task PackagePathRoundTripReleasesOutputForCleanup()
    {
        var output = Path.Combine(Path.GetTempPath(), $"AnkiIO-synthetic-{Guid.NewGuid():N}.apkg");
        try
        {
            var deck = new AnkiDeck("Synthetic");
            deck.AddBasicNote("front", "back");
            await AnkiPackageWriter.WriteAsync(deck, output);
            var package = await AnkiPackageReader.ReadAsync(output);
            Assert.Single(package.Notes);
        }
        finally
        {
            if (File.Exists(output)) File.Delete(output);
        }

        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task CancelledMediaHashingObservesCancellation()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, new byte[1024]);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new AnkiMediaCollection().AddFileAsync(path, cancellation.Token));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
