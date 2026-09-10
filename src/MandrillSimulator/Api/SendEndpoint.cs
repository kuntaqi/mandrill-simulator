using System.Text.Json;
using MandrillSimulator.Models;
using MandrillSimulator.Store;

namespace MandrillSimulator.Api;

public class SendEndpoint : IMandrillEndpoint
{
    private readonly MessageStore _store;

    public SendEndpoint(MessageStore store)
    {
        _store = store;
    }

    public string Route => "/messages/send";

    public Task<object?> HandleAsync(ApiRequest request, CancellationToken cancellationToken)
    {
        if (!request.Payload.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object)
            throw new MandrillApiException("ValidationError", "No message for sending.");

        var attachments = ReadAttachments(message, "attachments", embedded: false)
            .Concat(ReadAttachments(message, "images", embedded: true))
            .ToList();

        var recipients = ReadRecipients(message);
        if (recipients.Count == 0)
            throw new MandrillApiException("ValidationError", "No recipients supplied.");

        var cc = recipients.Where(r => r.Type == "cc").Select(r => r.Email).ToList();
        var results = new List<object>();

        // Real Mandrill answers with one entry per recipient, each with its own _id.
        foreach (var recipient in recipients)
        {
            var captured = new CapturedMessage
            {
                Id = NewMessageId(),
                Subject = message.GetString("subject") ?? "(no subject)",
                FromEmail = message.GetString("from_email") ?? string.Empty,
                FromName = message.GetString("from_name") ?? string.Empty,
                ToEmail = recipient.Email,
                ToName = recipient.Name,
                CcEmails = cc,
                Tags = message.GetStringArray("tags"),
                Html = message.GetString("html") ?? string.Empty,
                Attachments = attachments,
                TrackOpens = message.GetBool("track_opens"),
                TrackClicks = message.GetBool("track_clicks"),
                RawRequestJson = request.RawBody
            };

            _store.Add(captured);

            results.Add(new SendResult
            {
                email = captured.ToEmail,
                _id = captured.Id,
                status = MessageStates.ToWire(captured.State),
                reject_reason = captured.RejectReason
            });
        }

        var responseJson = MandrillJson.Serialize(results);
        foreach (var captured in results.Cast<SendResult>().Select(r => _store.FindById(r._id)).OfType<CapturedMessage>())
        {
            captured.RawResponseJson = responseJson;
        }

        return Task.FromResult<object?>(results);
    }

    private static string NewMessageId() => Guid.NewGuid().ToString("N");

    private static List<Recipient> ReadRecipients(JsonElement message)
    {
        var recipients = new List<Recipient>();
        if (!message.TryGetProperty("to", out var to) || to.ValueKind != JsonValueKind.Array)
            return recipients;

        foreach (var entry in to.EnumerateArray())
        {
            var email = entry.GetString("email");
            if (string.IsNullOrWhiteSpace(email)) continue;

            recipients.Add(new Recipient(
                email,
                entry.GetString("name") ?? string.Empty,
                (entry.GetString("type") ?? "to").ToLowerInvariant()));
        }

        return recipients;
    }

    private static List<CapturedAttachment> ReadAttachments(JsonElement message, string property, bool embedded)
    {
        var attachments = new List<CapturedAttachment>();
        if (!message.TryGetProperty(property, out var array) || array.ValueKind != JsonValueKind.Array)
            return attachments;

        foreach (var entry in array.EnumerateArray())
        {
            var name = entry.GetString("name");
            if (string.IsNullOrWhiteSpace(name)) continue;

            attachments.Add(new CapturedAttachment
            {
                Name = name,
                Type = entry.GetString("type") ?? "application/octet-stream",
                Content = DecodeBase64(entry.GetString("content")),
                IsEmbeddedImage = embedded
            });
        }

        return attachments;
    }

    private static byte[] DecodeBase64(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return [];
        try
        {
            return Convert.FromBase64String(content);
        }
        catch (FormatException)
        {
            return [];
        }
    }

    private record Recipient(string Email, string Name, string Type);
}
