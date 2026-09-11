using Velopack;
using Velopack.Sources;

namespace MandrillSimulator.Services;

// Updates only mean anything for a copy installed by the setup bundle; a build
// run straight out of bin/ has no Velopack metadata to compare against.
public class UpdateService
{
    public async Task<string> CheckAndApplyAsync(string? feedUrl, string? token)
    {
        if (string.IsNullOrWhiteSpace(feedUrl))
            return "No update feed is configured.";

        try
        {
            var manager = new UpdateManager(BuildSource(feedUrl, token));

            if (!manager.IsInstalled)
                return "This copy was not installed by the setup bundle, so it cannot update itself.";

            var update = await manager.CheckForUpdatesAsync();
            if (update is null) return "Already on the latest version.";

            await manager.DownloadUpdatesAsync(update);
            manager.ApplyUpdatesAndRestart(update.TargetFullRelease);
            return "Restarting into the new version…";
        }
        catch (Exception ex)
        {
            // A private repository answers 404 without a token, which would
            // otherwise look like "no updates" rather than "cannot see the feed".
            return $"Could not check for updates: {ex.Message}";
        }
    }

    private static IUpdateSource BuildSource(string feedUrl, string? token) =>
        feedUrl.Contains("github.com", StringComparison.OrdinalIgnoreCase)
            ? new GithubSource(feedUrl, token, prerelease: false)
            : new SimpleWebSource(feedUrl);
}
