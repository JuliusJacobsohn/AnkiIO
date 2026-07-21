using Xunit;

namespace AnkiIO.UnitTests;

public sealed class CompatibilityTests
{
    [Fact]
    public void RegistryFailsActionablyForUnsupportedVersion()
    {
        var exception = Assert.Throws<NotSupportedException>(() => new AnkiCompatibilityRegistry().Resolve(new Version(99, 1)));
        Assert.Contains("Register an IAnkiVersionAdapter", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EnvironmentOverrideCanBeDetectedWithoutLaunchingAnki()
    {
        var path = Path.GetTempFileName();
        var priorExecutable = Environment.GetEnvironmentVariable("ANKI_EXECUTABLE");
        var priorVersion = Environment.GetEnvironmentVariable("ANKI_VERSION");
        try
        {
            Environment.SetEnvironmentVariable("ANKI_EXECUTABLE", path);
            Environment.SetEnvironmentVariable("ANKI_VERSION", "25.9.4");

            var result = AnkiInstallationDetector.Detect();

            Assert.NotNull(result);
            Assert.Equal(new Version(25, 9, 4), result.Version);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANKI_EXECUTABLE", priorExecutable);
            Environment.SetEnvironmentVariable("ANKI_VERSION", priorVersion);
            File.Delete(path);
        }
    }
}
