namespace AnkiIO.UnitTests;

using Xunit;

public sealed class AssemblyMarkerTests
{
    [Fact]
    public void AssemblyMarkerIsPublic() => Assert.True(typeof(AssemblyMarker).IsPublic);
}
