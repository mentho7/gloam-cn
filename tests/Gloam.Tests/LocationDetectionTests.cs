using Gloam.Core;
using Xunit;

namespace Gloam.Tests;

public class LocationDetectionTests
{
    [Fact]
    public void ParsesWhitespaceAndExponentNotation()
    {
        var result = LocationDetectionService.ParseCoordinates("{\"latitude\": 4.05e1, \"longitude\": -73.25}");
        Assert.Equal((40.5, -73.25), result!.Value);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"error\":true,\"latitude\":1,\"longitude\":2}")]
    [InlineData("{\"latitude\":91,\"longitude\":2}")]
    [InlineData("{\"latitude\":1,\"longitude\":181}")]
    [InlineData("{\"latitude\":null,\"longitude\":2}")]
    [InlineData("{\"latitude\":1,\"longitude\":\"2\"}")]
    public void InvalidResponseCannotPartiallyUpdateLocation(string json)
        => Assert.Null(LocationDetectionService.ParseCoordinates(json));
}
