using System.Security.Cryptography;
using Xunit;

namespace AnkiIO.UnitTests;

public sealed class MediaCompatibilityTests
{
    [Fact]
    public async Task ByteRegistrationOwnsDefensiveCopyAndOpensIndependentReadOnlyStreams()
    {
        var source = new byte[] { 1, 2, 3, 4 };
        var media = new AnkiMediaCollection();

        var descriptor = media.AddBytes("payload.bin", source);
        source[0] = 99;

        Assert.Equal(4, descriptor.Length);
        Assert.Equal(Convert.ToHexString(SHA256.HashData([1, 2, 3, 4])).ToLowerInvariant(), descriptor.Sha256);
        await using var first = await descriptor.OpenReadAsync();
        await using var second = await descriptor.OpenReadAsync();
        Assert.False(first.CanWrite);
        Assert.False(second.CanWrite);
        Assert.NotSame(first, second);
        Assert.Equal(1, first.ReadByte());
        Assert.Equal(1, second.ReadByte());
        Assert.Equal([2, 3, 4], await ReadRemainingAsync(first));
        Assert.Equal([2, 3, 4], await ReadRemainingAsync(second));
    }

    [Fact]
    public async Task EmptyPayloadIsAValidReusableMediaFile()
    {
        var media = new AnkiMediaCollection();

        var descriptor = media.AddBytes("empty.dat", []);

        Assert.Equal(0, descriptor.Length);
        Assert.Equal(Convert.ToHexString(SHA256.HashData([])).ToLowerInvariant(), descriptor.Sha256);
        await using var stream = await descriptor.OpenReadAsync();
        Assert.Equal(-1, stream.ReadByte());
    }

    [Fact]
    public void IdempotentByteRegistrationReturnsStoredDescriptorAndCollisionPreservesIt()
    {
        var media = new AnkiMediaCollection();
        var stored = media.AddBytes("same.bin", [1, 2, 3]);

        var duplicate = media.AddBytes("same.bin", [1, 2, 3]);
        var collision = Assert.Throws<InvalidOperationException>(() => media.AddBytes("same.bin", [3, 2, 1]));

        Assert.Same(stored, duplicate);
        Assert.Same(stored, Assert.Single(media.Files));
        Assert.Contains("colliding content", collision.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FileRegistrationHashesStreamsAndReturnsStoredDescriptorWhenRepeated()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "asset.bin");
            await File.WriteAllBytesAsync(path, [4, 5, 6, 7]);
            var media = new AnkiMediaCollection();

            var stored = await media.AddFileAsync(path);
            var duplicate = await media.AddFileAsync(path);

            Assert.Same(stored, duplicate);
            Assert.Equal("asset.bin", stored.FileName);
            Assert.Equal(4, stored.Length);
            Assert.Equal(Convert.ToHexString(SHA256.HashData([4, 5, 6, 7])).ToLowerInvariant(), stored.Sha256);
            await using var stream = await stored.OpenReadAsync();
            Assert.Equal([4, 5, 6, 7], await ReadRemainingAsync(stream));
            Assert.Same(stored, Assert.Single(media.Files));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task FileRegistrationRejectsChangedContentUnderExistingName()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "mutable.bin");
            await File.WriteAllBytesAsync(path, [1]);
            var media = new AnkiMediaCollection();
            var stored = await media.AddFileAsync(path);
            await File.WriteAllBytesAsync(path, [2]);

            await Assert.ThrowsAsync<InvalidOperationException>(() => media.AddFileAsync(path));

            Assert.Same(stored, Assert.Single(media.Files));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CancellationPreventsFileRegistrationAndStreamOpening()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "cancel.bin");
            await File.WriteAllBytesAsync(path, new byte[4096]);
            var media = new AnkiMediaCollection();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => media.AddFileAsync(path, cancellation.Token));
            Assert.Empty(media.Files);

            var descriptor = media.AddBytes("memory.bin", [1]);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await descriptor.OpenReadAsync(cancellation.Token);
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void FilesAreOrdinallyOrderedCaseSensitiveSnapshotsAndRemovalIsExact()
    {
        var media = new AnkiMediaCollection();
        media.AddBytes("z.bin", [1]);
        media.AddBytes("a.bin", [2]);
        media.AddBytes("A.bin", [3]);
        var snapshot = media.Files;

        media.AddBytes("m.bin", [4]);

        Assert.Equal(["A.bin", "a.bin", "z.bin"], snapshot.Select(file => file.FileName));
        Assert.Equal(["A.bin", "a.bin", "m.bin", "z.bin"], media.Files.Select(file => file.FileName));
        Assert.True(media.Remove("a.bin"));
        Assert.False(media.Remove("a.bin"));
        Assert.Contains(media.Files, file => file.FileName == "A.bin");
        Assert.Throws<ArgumentNullException>(() => media.Remove(null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("../asset.png")]
    [InlineData("folder/asset.png")]
    [InlineData("folder\\asset.png")]
    [InlineData("asset<1>.png")]
    [InlineData("asset:1.png")]
    [InlineData("asset\"1.png")]
    [InlineData("asset|1.png")]
    [InlineData("asset?1.png")]
    [InlineData("asset*1.png")]
    [InlineData("asset.png.")]
    [InlineData("asset.png ")]
    [InlineData("NUL")]
    [InlineData("nul.txt")]
    [InlineData("COM1")]
    [InlineData("com9.wav")]
    [InlineData("LPT1")]
    [InlineData("lpt9.png")]
    [InlineData("line\nbreak.png")]
    public void MediaNamesUnsafeOnAnySupportedPlatformAreRejected(string fileName)
    {
        var exception = Assert.Throws<ArgumentException>(() => new AnkiMediaCollection().AddBytes(fileName, [1]));

        Assert.Equal("fileName", exception.ParamName);
    }

    [Theory]
    [InlineData(".hidden")]
    [InlineData("two words.svg")]
    [InlineData("Grüße-東京.png")]
    [InlineData("COM0.txt")]
    [InlineData("COM10.txt")]
    [InlineData("LPT10.txt")]
    public void PortableMediaNamesRemainAccepted(string fileName)
    {
        var descriptor = new AnkiMediaCollection().AddBytes(fileName, [1]);

        Assert.Equal(fileName, descriptor.FileName);
    }

    [Fact]
    public void BuiltInAdapterReportsOnlyItsVerifiedVersionFamilyAndCapabilities()
    {
        var adapter = new Anki2605VersionAdapter();

        Assert.Equal("Anki 26.05", adapter.Name);
        Assert.True(adapter.Supports(new Version(26, 5)));
        Assert.True(adapter.Supports(new Version(26, 5, 99)));
        Assert.False(adapter.Supports(new Version(26, 4, 99)));
        Assert.False(adapter.Supports(new Version(26, 6)));
        Assert.False(adapter.Supports(new Version(25, 5)));
        Assert.False(adapter.Supports(new Version(27, 5)));
        Assert.Equal(3, adapter.SchedulerVersion);
        Assert.True(adapter.CollectionSchemas.SetEquals([18]));
        Assert.True(adapter.PackageEntries.SetEquals(["collection.anki2", "collection.anki21", "collection.anki21b", "meta", "media"]));
        Assert.DoesNotContain("Media", adapter.PackageEntries);
    }

    [Fact]
    public void BuiltInCapabilitySetsAreImmutable()
    {
        var adapter = new Anki2605VersionAdapter();
        var schemas = Assert.IsAssignableFrom<ISet<int>>(adapter.CollectionSchemas);
        var entries = Assert.IsAssignableFrom<ISet<string>>(adapter.PackageEntries);

        Assert.True(schemas.IsReadOnly);
        Assert.True(entries.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => schemas.Add(19));
        Assert.Throws<NotSupportedException>(() => entries.Remove("media"));
        Assert.True(adapter.CollectionSchemas.SetEquals([18]));
        Assert.Contains("media", adapter.PackageEntries);
    }

    [Fact]
    public void InstallationSnapshotRetainsAllPointInTimeMetadata()
    {
        var version = new Version(26, 5, 1);
        var installation = new AnkiInstallation(
            @"C:\Program Files\Anki\anki.exe",
            @"C:\Program Files\Anki",
            version,
            "26.5.1",
            @"C:\Users\Example\AppData\Roaming\Anki2",
            ProfilesPresent: true,
            CollectionsPresent: true,
            MediaDirectoriesPresent: true,
            AddonsDirectoryPresent: false);

        Assert.Equal(@"C:\Program Files\Anki\anki.exe", installation.ExecutablePath);
        Assert.Equal(@"C:\Program Files\Anki", installation.InstallationDirectory);
        Assert.Same(version, installation.Version);
        Assert.Equal("26.5.1", installation.VersionText);
        Assert.Equal(@"C:\Users\Example\AppData\Roaming\Anki2", installation.DataDirectory);
        Assert.True(installation.ProfilesPresent);
        Assert.True(installation.CollectionsPresent);
        Assert.True(installation.MediaDirectoriesPresent);
        Assert.False(installation.AddonsDirectoryPresent);
    }

    [Fact]
    public void RegistryUsesRegistrationOrderAndFluentAdd()
    {
        var broad = new TestAdapter("broad", _ => true);
        var narrow = new TestAdapter("narrow", version => version.Major == 26);
        var registry = new AnkiCompatibilityRegistry();

        var returned = registry.Add(broad).Add(narrow);

        Assert.Same(registry, returned);
        Assert.Same(broad, registry.Resolve(new Version(26, 5)));
    }

    [Fact]
    public void DefaultAndCustomRegistriesHaveIndependentExplicitPrecedence()
    {
        var custom = new TestAdapter("custom", version => version.Major == 26 && version.Minor == 5);
        var defaults = AnkiCompatibilityRegistry.CreateDefault();
        defaults.Add(custom);
        var customFirst = new AnkiCompatibilityRegistry()
            .Add(custom)
            .Add(new Anki2605VersionAdapter());

        Assert.IsType<Anki2605VersionAdapter>(defaults.Resolve(new Version(26, 5)));
        Assert.Same(custom, customFirst.Resolve(new Version(26, 5)));
        Assert.NotSame(
            AnkiCompatibilityRegistry.CreateDefault().Resolve(new Version(26, 5)),
            AnkiCompatibilityRegistry.CreateDefault().Resolve(new Version(26, 5)));
    }

    [Fact]
    public void RegistryRejectsNullsAndUnsupportedVersionsWithoutCallingAdaptersForNull()
    {
        var registry = new AnkiCompatibilityRegistry();

        Assert.Throws<ArgumentNullException>(() => registry.Add(null!));
        Assert.Throws<ArgumentNullException>(() => registry.Resolve(null!));
        var exception = Assert.Throws<NotSupportedException>(() => registry.Resolve(new Version(26, 6)));
        Assert.Contains("Anki 26.6 is not supported", exception.Message, StringComparison.Ordinal);
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"AnkiIO-media-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task<byte[]> ReadRemainingAsync(Stream stream)
    {
        await using var destination = new MemoryStream();
        await stream.CopyToAsync(destination);
        return destination.ToArray();
    }

    private sealed class TestAdapter(string name, Func<Version, bool> supports) : IAnkiVersionAdapter
    {
        public string Name { get; } = name;

        public bool Supports(Version version) => supports(version);

        public IReadOnlySet<int> CollectionSchemas { get; } = new HashSet<int>();

        public int SchedulerVersion => 0;

        public IReadOnlySet<string> PackageEntries { get; } = new HashSet<string>(StringComparer.Ordinal);
    }
}
