using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using MandrillSimulator.Models;
using MandrillSimulator.Store;

namespace MandrillSimulator.Api;

public class SimulatorServer
{
    private readonly ExportJobStore _jobs;
    private readonly Dictionary<string, IMandrillEndpoint> _endpoints;

    private HttpListener? _listener;
    private CancellationTokenSource? _cancellation;
    private Task? _loop;

    public SimulatorServer(MessageStore messages, ExportJobStore jobs)
    {
        _jobs = jobs;

        var endpoints = new IMandrillEndpoint[]
        {
            new SendEndpoint(messages),
            new SearchEndpoint(messages),
            new ExportsActivityEndpoint(messages, jobs, () => BaseUrl),
            new ExportsInfoEndpoint(jobs),
            new RejectsDeleteEndpoint(messages)
        };

        _endpoints = endpoints.ToDictionary(e => e.Route, StringComparer.OrdinalIgnoreCase);
    }

    public int Port { get; private set; } = 8025;

    public bool IsRunning => _listener?.IsListening == true;

    public string BaseUrl => $"http://localhost:{Port}/";

    // Set from the UI to exercise the caller's error path, which expects a 500
    // carrying Mandrill's error body.
    public bool FailNextRequest { get; set; }

    public event EventHandler<RequestLogEntry>? RequestHandled;
    public event EventHandler<string>? Faulted;

    public void Start(int port)
    {
        if (IsRunning) Stop();

        Port = port;

        // Both forms so a caller configured with 127.0.0.1 works too. Loopback
        // only: a wildcard binding would expose captured mail on the network.
        // Adding 127.0.0.1 needs a URL ACL on some machines, so fall back to
        // localhost alone rather than refusing to start.
        if (!TryStart([$"http://localhost:{port}/", $"http://127.0.0.1:{port}/"]))
        {
            _listener = null;
            if (!TryStart([$"http://localhost:{port}/"]))
                throw new InvalidOperationException(
                    $"Could not listen on port {port}. Another process may be using it.");
        }

        _cancellation = new CancellationTokenSource();
        _loop = Task.Run(() => AcceptLoopAsync(_cancellation.Token));
    }

    private bool TryStart(IEnumerable<string> prefixes)
    {
        var listener = new HttpListener();
        foreach (var prefix in prefixes) listener.Prefixes.Add(prefix);

        try
        {
            listener.Start();
            _listener = listener;
            return true;
        }
        catch (HttpListenerException)
        {
            listener.Close();
            return false;
        }
    }

    public void Stop()
    {
        _cancellation?.Cancel();

        if (_listener is { IsListening: true }) _listener.Stop();
        _listener?.Close();
        _listener = null;

        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // Listener disposal races the accept loop; nothing useful to report.
        }

        _loop = null;
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener is { IsListening: true })
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (HttpListenerException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            _ = Task.Run(() => HandleContextAsync(context, cancellationToken), cancellationToken);
        }
    }

    private async Task HandleContextAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        // Read everything needed for the log line up front: once the response is
        // closed, touching context.Request throws, and this method is
        // fire-and-forget so the throw would vanish silently.
        var rawPath = context.Request.Url?.AbsolutePath ?? "/";
        var method = context.Request.HttpMethod;
        var route = NormalisePath(rawPath);
        var statusCode = 200;

        try
        {
            if (route.StartsWith("/exports/download/", StringComparison.OrdinalIgnoreCase))
            {
                statusCode = await WriteExportCsvAsync(context, route).ConfigureAwait(false);
                return;
            }

            var body = await ReadBodyAsync(context.Request).ConfigureAwait(false);

            if (FailNextRequest)
            {
                FailNextRequest = false;
                statusCode = await WriteErrorAsync(context, "Simulated_Failure",
                    "Simulated failure requested from the simulator UI.").ConfigureAwait(false);
                return;
            }

            if (!_endpoints.TryGetValue(route, out var endpoint))
            {
                statusCode = await WriteErrorAsync(context, "Unknown_Endpoint",
                    $"No endpoint at '{rawPath}'.", HttpStatusCode.NotFound).ConfigureAwait(false);
                return;
            }

            var payload = ParsePayload(body);
            var result = await endpoint.HandleAsync(new ApiRequest(payload, body, route), cancellationToken)
                .ConfigureAwait(false);

            await WriteJsonAsync(context, HttpStatusCode.OK, MandrillJson.Serialize(result ?? new { }))
                .ConfigureAwait(false);
        }
        catch (MandrillApiException ex)
        {
            statusCode = await WriteErrorAsync(context, ex.Name, ex.Message).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            statusCode = await WriteErrorAsync(context, "Simulator_Error", ex.Message).ConfigureAwait(false);
            Faulted?.Invoke(this, ex.Message);
        }
        finally
        {
            stopwatch.Stop();
            RequestHandled?.Invoke(this, new RequestLogEntry(
                DateTimeOffset.Now,
                method,
                rawPath,
                route,
                statusCode,
                stopwatch.ElapsedMilliseconds));
        }
    }

    // Callers build their URL by plain string concatenation, so a base ending in
    // "/" and a path starting with "/" arrive here as "//messages/send.json".
    // Some also mirror the real host's "/api/1.0/" prefix. Fold all of that away.
    public static string NormalisePath(string rawPath)
    {
        var path = rawPath.Trim();
        while (path.Contains("//", StringComparison.Ordinal))
        {
            path = path.Replace("//", "/", StringComparison.Ordinal);
        }

        if (!path.StartsWith('/')) path = "/" + path;
        path = path.ToLowerInvariant();

        if (path.StartsWith("/api/1.0", StringComparison.Ordinal))
            path = path.Substring("/api/1.0".Length);

        // Trailing slash first, or a path like "/rejects/delete.json/" never
        // matches the .json suffix and falls through to a 404.
        if (path.Length > 1) path = path.TrimEnd('/');

        if (path.EndsWith(".json", StringComparison.Ordinal))
            path = path.Substring(0, path.Length - ".json".Length);

        return path.Length == 0 ? "/" : path;
    }

    private static async Task<string> ReadBodyAsync(HttpListenerRequest request)
    {
        using var reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8);
        return await reader.ReadToEndAsync().ConfigureAwait(false);
    }

    private static JsonElement ParsePayload(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return default;

        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new MandrillApiException("ValidationError", $"Request body is not valid JSON: {ex.Message}");
        }
    }

    private async Task<int> WriteExportCsvAsync(HttpListenerContext context, string route)
    {
        var id = route.Substring("/exports/download/".Length)
            .Replace(".csv", string.Empty, StringComparison.OrdinalIgnoreCase);

        var job = _jobs.Find(id);
        if (job is null)
        {
            return await WriteErrorAsync(context, "Unknown_Export", $"No export '{id}'.", HttpStatusCode.NotFound)
                .ConfigureAwait(false);
        }

        var bytes = Encoding.UTF8.GetBytes(job.Csv);
        context.Response.StatusCode = 200;
        context.Response.ContentType = "text/csv";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        context.Response.Close();
        return 200;
    }

    private static async Task<int> WriteErrorAsync(
        HttpListenerContext context,
        string name,
        string message,
        HttpStatusCode status = HttpStatusCode.InternalServerError)
    {
        var body = MandrillJson.Serialize(new
        {
            status = "error",
            code = -1,
            name,
            message
        });

        await WriteJsonAsync(context, status, body).ConfigureAwait(false);
        return (int)status;
    }

    private static async Task WriteJsonAsync(HttpListenerContext context, HttpStatusCode status, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        context.Response.StatusCode = (int)status;
        context.Response.ContentType = "application/json";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        context.Response.Close();
    }
}
