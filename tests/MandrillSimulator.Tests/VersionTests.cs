using System.Reflection;
using MandrillSimulator.ViewModels;

namespace MandrillSimulator.Tests;

// The release workflow stamps the assembly version from the tag. Nothing stops
// someone putting the number back into a literal, which would then quietly
// disagree with the installer the moment a version is tagged — so assert that
// what the status bar shows is whatever the build was stamped with.
public class VersionTests
{
    [Fact]
    public async Task TheStatusBarVersionComesFromTheAssembly()
    {
        var stamped = typeof(MainViewModel).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?.Split('+')[0];

        Assert.False(string.IsNullOrWhiteSpace(stamped));

        var shown = await MenuToggleTests.Session
            .Dispatch(() => new MainViewModel().VersionText, CancellationToken.None);

        Assert.StartsWith($"v{stamped} ", shown);
        Assert.Contains(".NET", shown);
    }
}
