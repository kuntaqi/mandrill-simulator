using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using MandrillSimulator.Api;
using MandrillSimulator.Models;
using MandrillSimulator.Store;

namespace MandrillSimulator.Tests;

public class SimulatorServerTests : IDisposable
{
    private readonly MessageStore _store = new();
    private readonly ExportJobStore _jobs = new();
    private readonly SimulatorServer _server;
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly string _baseUrl;

    public SimulatorServerTests()
    {
        _server = new SimulatorServer(_store, _jobs);
        _server.Start(FreePort());
        _baseUrl = _server.BaseUrl;
    }

    public void Dispose()
    {
        _server.Stop();
        _client.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task SendCapturesTheMessageAndAnswersInMandrillsShape()
    {
        var response = await PostAsync("/messages/send.json", SendPayload("a@example.com"));

        Assert.Equal(HttpStatusCode.OK, response.status);

        using var document = JsonDocument.Parse(response.body);
        var first = document.RootElement.EnumerateArray().Single();

        Assert.Equal("a@example.com", first.GetProperty("email").GetString());
        // Lower case matters: the caller deserialises this into an enum.
        Assert.Equal("sent", first.GetProperty("status").GetString());

        var id = first.GetProperty("_id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(id));
        Assert.Equal(32, id!.Length);

        Assert.Equal(1, _store.Count);
        Assert.Equal("Test subject", _store.Snapshot()[0].Subject);
    }

    [Fact]
    public async Task DoubleSlashFromNaiveUrlConcatenationStillRoutes()
    {
        // Exactly what "baseUrl + path" produces when both carry a slash.
        var url = _baseUrl + "/messages/send.json";
        using var content = new StringContent(SendPayload("b@example.com"), Encoding.UTF8, "application/json");
        using var response = await _client.PostAsync(url, content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, _store.Count);
    }

    [Fact]
    public async Task EveryRecipientGetsItsOwnIdAsMandrillDoes()
    {
        var payload = """
        {
          "key": "k",
          "message": {
            "subject": "Two up",
            "from_email": "from@example.com",
            "to": [
              { "email": "one@example.com", "type": "to" },
              { "email": "two@example.com", "type": "cc" }
            ],
            "html": "<p>hi</p>"
          }
        }
        """;

        var response = await PostAsync("/messages/send.json", payload);
        using var document = JsonDocument.Parse(response.body);
        var results = document.RootElement.EnumerateArray().ToList();

        Assert.Equal(2, results.Count);
        Assert.Equal(2, _store.Count);
        Assert.NotEqual(results[0].GetProperty("_id").GetString(), results[1].GetProperty("_id").GetString());
    }

    [Fact]
    public async Task SearchReportsBackTheSameIdWithTheCountersTheInboxSet()
    {
        var send = await PostAsync("/messages/send.json", SendPayload("c@example.com", tag: "Commodity Insights Request"));
        using var sendDocument = JsonDocument.Parse(send.body);
        var id = sendDocument.RootElement.EnumerateArray().Single().GetProperty("_id").GetString()!;

        // What the inbox's "Simulate click" button does.
        _store.FindById(id)!.Clicks = 3;

        var search = await PostAsync("messages/search", """
        { "key": "k", "tags": [ "Commodity Insights Request" ], "limit": "50" }
        """);

        using var searchDocument = JsonDocument.Parse(search.body);
        var hit = searchDocument.RootElement.EnumerateArray().Single();

        Assert.Equal(id, hit.GetProperty("_id").GetString());
        Assert.Equal(3, hit.GetProperty("clicks").GetInt32());
        Assert.Equal("sent", hit.GetProperty("state").GetString());
    }

    [Fact]
    public async Task SearchHonoursTheTagFilter()
    {
        await PostAsync("/messages/send.json", SendPayload("d@example.com", tag: "wanted"));
        await PostAsync("/messages/send.json", SendPayload("e@example.com", tag: "ignored"));

        var search = await PostAsync("messages/search", """
        { "key": "k", "tags": [ "wanted" ] }
        """);

        using var document = JsonDocument.Parse(search.body);
        var hit = Assert.Single(document.RootElement.EnumerateArray().ToList());
        Assert.Equal("d@example.com", hit.GetProperty("email").GetString());
    }

    [Fact]
    public async Task ExportsActivityCompletesAndItsResultUrlServesCsv()
    {
        await PostAsync("/messages/send.json", SendPayload("f@example.com"));

        var activity = await PostAsync("/exports/activity", """{ "key": "k", "notify_email": "me@example.com" }""");
        using var activityDocument = JsonDocument.Parse(activity.body);
        var root = activityDocument.RootElement;

        var id = root.GetProperty("id").GetString()!;
        Assert.Equal("complete", root.GetProperty("state").GetString());

        var resultUrl = root.GetProperty("result_url").GetString()!;
        var csv = await _client.GetStringAsync(resultUrl);
        Assert.Contains("f@example.com", csv);

        var info = await PostAsync("/exports/info", $$"""{ "key": "k", "id": "{{id}}" }""");
        using var infoDocument = JsonDocument.Parse(info.body);
        Assert.Equal(id, infoDocument.RootElement.GetProperty("id").GetString());
    }

    [Fact]
    public async Task RejectsDeleteClearsTheRejectedState()
    {
        var send = await PostAsync("/messages/send.json", SendPayload("g@example.com"));
        using var sendDocument = JsonDocument.Parse(send.body);
        var id = sendDocument.RootElement.EnumerateArray().Single().GetProperty("_id").GetString()!;

        var message = _store.FindById(id)!;
        message.State = MessageState.Rejected;
        message.RejectReason = "hard-bounce";

        var response = await PostAsync("/rejects/delete.json", """{ "key": "k", "email": "g@example.com" }""");

        using var document = JsonDocument.Parse(response.body);
        Assert.True(document.RootElement.GetProperty("deleted").GetBoolean());
        Assert.True(document.RootElement.GetProperty("Status").GetBoolean());
        Assert.Equal(MessageState.Sent, _store.FindById(id)!.State);
    }

    [Fact]
    public async Task AnUnknownPathAnswersWithMandrillsErrorBody()
    {
        var response = await PostAsync("/messages/nope.json", "{}");

        Assert.Equal(HttpStatusCode.NotFound, response.status);
        using var document = JsonDocument.Parse(response.body);
        Assert.Equal("error", document.RootElement.GetProperty("status").GetString());
        Assert.Equal("Unknown_Endpoint", document.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public async Task MalformedJsonIsA500CarryingTheErrorShapeTheCallerParses()
    {
        var response = await PostAsync("/messages/send.json", "{ not json");

        Assert.Equal(HttpStatusCode.InternalServerError, response.status);
        using var document = JsonDocument.Parse(response.body);
        Assert.Equal("ValidationError", document.RootElement.GetProperty("name").GetString());
        Assert.True(document.RootElement.TryGetProperty("message", out _));
    }

    [Fact]
    public async Task FailNextRequestForcesTheCallersErrorPathExactlyOnce()
    {
        _server.FailNextRequest = true;

        var failed = await PostAsync("/messages/send.json", SendPayload("h@example.com"));
        Assert.Equal(HttpStatusCode.InternalServerError, failed.status);

        var recovered = await PostAsync("/messages/send.json", SendPayload("h@example.com"));
        Assert.Equal(HttpStatusCode.OK, recovered.status);
    }

    [Fact]
    public async Task SendWithNoRecipientsIsRejectedRatherThanStored()
    {
        var response = await PostAsync("/messages/send.json", """
        { "key": "k", "message": { "subject": "nobody", "from_email": "a@b.c", "to": [] } }
        """);

        Assert.Equal(HttpStatusCode.InternalServerError, response.status);
        Assert.Equal(0, _store.Count);
    }

    [Fact]
    public async Task RequestHandledReportsTheNormalisedRouteTheRailCountsOn()
    {
        var routes = new System.Collections.Concurrent.ConcurrentBag<string>();
        using var signal = new SemaphoreSlim(0);
        _server.RequestHandled += (_, entry) =>
        {
            routes.Add(entry.Route);
            signal.Release();
        };

        // The response is closed before the event is raised, so the client can
        // return first. Wait for the notification rather than racing it.
        await PostAsync("/messages/send.json", SendPayload("i@example.com"));
        Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(5)));

        await PostAsync("messages/search", """{ "key": "k" }""");
        Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Contains("/messages/send", routes);
        Assert.Contains("/messages/search", routes);
    }

    // The rail lists endpoints by their wire path; it finds the row to increment
    // by normalising that label the same way the server normalises the request.
    [Theory]
    [InlineData("/messages/send.json", "/messages/send")]
    [InlineData("messages/search", "/messages/search")]
    [InlineData("/exports/activity", "/exports/activity")]
    [InlineData("/exports/info", "/exports/info")]
    [InlineData("/rejects/delete.json", "/rejects/delete")]
    public void RailLabelsNormaliseOntoTheRoutesTheServerReports(string label, string route) =>
        Assert.Equal(route, SimulatorServer.NormalisePath(label));

    private async Task<(HttpStatusCode status, string body)> PostAsync(string path, string json)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _client.PostAsync(_baseUrl.TrimEnd('/') + "/" + path.TrimStart('/'), content);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static string SendPayload(string to, string tag = "sample") => $$"""
    {
      "key": "local-dev-not-a-real-key",
      "message": {
        "subject": "Test subject",
        "from_email": "from@example.com",
        "to": [ { "email": "{{to}}", "name": "Someone", "type": "to" } ],
        "tags": [ "{{tag}}" ],
        "track_opens": true,
        "track_clicks": true,
        "html": "<html><body><p>Hello</p></body></html>"
      },
      "send_at": ""
    }
    """;

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
