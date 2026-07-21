using Xunit;

namespace AnkiIO.CompatibilityTests;

public sealed class LocalAnkiTests
{
    [Fact]
    [Trait("Category", "LocalAnkiCompatibility")]
    public void DetectsAnkiOnlyWhenExplicitlyEnabled()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("ANKI_LOCAL_COMPATIBILITY"), "1", StringComparison.Ordinal))
        {
            return;
        }

        var installation = AnkiInstallationDetector.Detect();
        Assert.NotNull(installation);
        Assert.True(Path.IsPathFullyQualified(installation.ExecutablePath));
        Assert.True(installation.Version > new Version(0, 0));
    }
}
