using System.IO;
using System.Text.Json;
using MandrillSimulator.Models;

namespace MandrillSimulator.Services;

public class SettingsService
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MandrillSimulator",
        "settings.json");

    public async Task<SimulatorSettings> LoadAsync()
    {
        try
        {
            if (!File.Exists(_path)) return new SimulatorSettings();

            var json = await File.ReadAllTextAsync(_path).ConfigureAwait(false);
            return JsonSerializer.Deserialize<SimulatorSettings>(json, Options) ?? new SimulatorSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new SimulatorSettings();
        }
    }

    public async Task SaveAsync(SimulatorSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            await File.WriteAllTextAsync(_path, JsonSerializer.Serialize(settings, Options)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Settings are a convenience; losing them must never take the app down.
        }
    }
}
