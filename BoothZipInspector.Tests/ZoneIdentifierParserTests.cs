using BoothZipInspector;
using Xunit;

namespace BoothZipInspector.Tests;

public class ZoneIdentifierParserTests
{
    [Fact]
    public void ParsesZoneIdReferrerAndHost()
    {
        var content = "[ZoneTransfer]\r\nZoneId=3\r\nReferrerUrl=https://booth.pm/ja/items/1234567\r\nHostUrl=https://booth.pm/\r\n";

        var info = ZoneIdentifierParser.Parse(content);

        Assert.True(info.Found);
        Assert.Equal("3", info.ZoneId);
        Assert.Equal("https://booth.pm/ja/items/1234567", info.ReferrerUrl);
        Assert.Equal("https://booth.pm/", info.HostUrl);
        Assert.Equal("1234567", info.BoothItemId);
    }

    [Fact]
    public void FallsBackToHostUrlWhenReferrerHasNoItemId()
    {
        var content = "[ZoneTransfer]\r\nZoneId=3\r\nReferrerUrl=https://example.com/\r\nHostUrl=https://shop-name.booth.pm/items/7654321\r\n";

        var info = ZoneIdentifierParser.Parse(content);

        Assert.Equal("7654321", info.BoothItemId);
    }

    [Fact]
    public void ReturnsNullItemIdWhenNeitherUrlContainsOne()
    {
        var content = "[ZoneTransfer]\r\nZoneId=3\r\nReferrerUrl=https://example.com/\r\n";

        var info = ZoneIdentifierParser.Parse(content);

        Assert.Null(info.BoothItemId);
    }
}
