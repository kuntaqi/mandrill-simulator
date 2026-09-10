using MandrillSimulator.Api;

namespace MandrillSimulator.Tests;

public class PathNormalisationTests
{
    // A caller that concatenates "http://host/" with "/messages/send.json" puts a
    // double slash on the wire. That is the shape this simulator most has to survive.
    [Theory]
    [InlineData("//messages/send.json", "/messages/send")]
    [InlineData("/messages/send.json", "/messages/send")]
    [InlineData("/messages/send", "/messages/send")]
    [InlineData("messages/search", "/messages/search")]
    [InlineData("/messages/search", "/messages/search")]
    [InlineData("//exports/activity", "/exports/activity")]
    [InlineData("/api/1.0//messages/send.json", "/messages/send")]
    [InlineData("/api/1.0/messages/search", "/messages/search")]
    [InlineData("/MESSAGES/SEND.JSON", "/messages/send")]
    [InlineData("/rejects/delete.json/", "/rejects/delete")]
    public void CollapsesEveryFormCallersActuallySend(string raw, string expected) =>
        Assert.Equal(expected, SimulatorServer.NormalisePath(raw));

    [Fact]
    public void LeavesRootAlone() => Assert.Equal("/", SimulatorServer.NormalisePath("/"));
}
