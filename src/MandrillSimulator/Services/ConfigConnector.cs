using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using MandrillSimulator.Models;

namespace MandrillSimulator.Services;

// Points an arbitrary project at this simulator by setting one key in one JSON
// config file. Nothing here knows or cares which codebase it is editing: the
// file and the key path both come from the caller.
public class ConfigConnector
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public async Task<ConnectedProject> ConnectAsync(string configPath, string keyPath, string baseUrl)
    {
        if (!File.Exists(configPath))
            throw new FileNotFoundException("Config file not found.", configPath);

        var root = await ReadRootAsync(configPath).ConfigureAwait(false);
        var segments = SplitKeyPath(keyPath);

        var existing = ReadExisting(root, segments);
        var backupPath = configPath + ".mandrillsim.bak";
        if (!File.Exists(backupPath))
            File.Copy(configPath, backupPath);

        SetValue(root, segments, baseUrl);
        await WriteRootAsync(configPath, root).ConfigureAwait(false);

        return new ConnectedProject
        {
            ConfigPath = configPath,
            KeyPath = keyPath,
            OriginalValue = existing.value,
            OriginalKeyExisted = existing.existed
        };
    }

    public async Task DisconnectAsync(ConnectedProject project)
    {
        if (!File.Exists(project.ConfigPath)) return;

        var root = await ReadRootAsync(project.ConfigPath).ConfigureAwait(false);
        var segments = SplitKeyPath(project.KeyPath);

        if (project.OriginalKeyExisted)
            SetValue(root, segments, project.OriginalValue);
        else
            RemoveValue(root, segments);

        await WriteRootAsync(project.ConfigPath, root).ConfigureAwait(false);
    }

    public async Task<string?> ReadCurrentValueAsync(string configPath, string keyPath)
    {
        if (!File.Exists(configPath)) return null;

        var root = await ReadRootAsync(configPath).ConfigureAwait(false);
        return ReadExisting(root, SplitKeyPath(keyPath)).value;
    }

    // Accepts both "A:B" (the .NET configuration convention) and "A.B".
    private static string[] SplitKeyPath(string keyPath) =>
        keyPath.Split([':', '.'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static async Task<JsonObject> ReadRootAsync(string path)
    {
        var text = await File.ReadAllTextAsync(path).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(text)) return new JsonObject();

        var node = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });

        return node as JsonObject
               ?? throw new InvalidDataException("The config file's root is not a JSON object.");
    }

    private static async Task WriteRootAsync(string path, JsonObject root) =>
        await File.WriteAllTextAsync(path, root.ToJsonString(WriteOptions)).ConfigureAwait(false);

    private static (bool existed, string? value) ReadExisting(JsonObject root, string[] segments)
    {
        JsonNode? current = root;
        foreach (var segment in segments)
        {
            if (current is not JsonObject obj || !obj.TryGetPropertyValue(segment, out current))
                return (false, null);
        }

        return (true, current?.GetValue<string>());
    }

    private static void SetValue(JsonObject root, string[] segments, string? value)
    {
        var parent = EnsureParent(root, segments);
        parent[segments[^1]] = value is null ? null : JsonValue.Create(value);
    }

    private static void RemoveValue(JsonObject root, string[] segments)
    {
        JsonObject? parent = root;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (parent is null || !parent.TryGetPropertyValue(segments[i], out var next)) return;
            parent = next as JsonObject;
        }

        parent?.Remove(segments[^1]);
    }

    private static JsonObject EnsureParent(JsonObject root, string[] segments)
    {
        var current = root;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (!current.TryGetPropertyValue(segments[i], out var next) || next is not JsonObject child)
            {
                child = new JsonObject();
                current[segments[i]] = child;
            }

            current = child;
        }

        return current;
    }
}
