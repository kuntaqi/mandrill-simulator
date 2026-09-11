using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MandrillSimulator.Models;

public class CapturedMessage : INotifyPropertyChanged
{
    private int _opens;
    private int _clicks;
    private MessageState _state = MessageState.Sent;
    private string? _rejectReason;
    private string? _bounceDescription;

    // Mandrill message ids are 32 lowercase hex characters. Callers typically
    // persist this value against their own records and match on it later via
    // messages/search, so it must stay stable for the message's whole life.
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public DateTimeOffset ReceivedAt { get; init; } = DateTimeOffset.Now;
    public string Subject { get; init; } = string.Empty;
    public string FromEmail { get; init; } = string.Empty;
    public string FromName { get; init; } = string.Empty;
    public string ToEmail { get; init; } = string.Empty;
    public string ToName { get; init; } = string.Empty;
    public IReadOnlyList<string> CcEmails { get; init; } = [];
    public IReadOnlyList<string> Tags { get; init; } = [];
    public string Html { get; init; } = string.Empty;
    public IReadOnlyList<CapturedAttachment> Attachments { get; init; } = [];
    public string RawRequestJson { get; init; } = string.Empty;
    public string RawResponseJson { get; set; } = string.Empty;
    public bool TrackOpens { get; init; }
    public bool TrackClicks { get; init; }

    public long UnixTimestamp => ReceivedAt.ToUnixTimeSeconds();

    public int Opens
    {
        get => _opens;
        set => Set(ref _opens, value);
    }

    public int Clicks
    {
        get => _clicks;
        set => Set(ref _clicks, value);
    }

    public MessageState State
    {
        get => _state;
        set
        {
            if (Set(ref _state, value)) OnPropertyChanged(nameof(StateWire));
        }
    }

    public string? RejectReason
    {
        get => _rejectReason;
        set => Set(ref _rejectReason, value);
    }

    public string? BounceDescription
    {
        get => _bounceDescription;
        set => Set(ref _bounceDescription, value);
    }

    public string StateWire => MessageStates.ToWire(State);

    public string DisplayTags => Tags.Count > 0 ? string.Join(", ", Tags) : "(untagged)";

    // Flat key/value view of the wire fields, for the Headers tab.
    public IReadOnlyList<KeyValuePair<string, string>> HeaderRows =>
    [
        new("_id", Id),
        new("status", StateWire),
        new("reject_reason", RejectReason ?? "—"),
        new("bounce_description", BounceDescription ?? "—"),
        new("from_email", FromEmail),
        new("to", ToEmail),
        new("cc", CcEmails.Count > 0 ? string.Join(", ", CcEmails) : "—"),
        new("tags", DisplayTags),
        new("opens / clicks", $"{Opens} / {Clicks}"),
        new("track_opens / track_clicks", $"{TrackOpens} / {TrackClicks}"),
        new("attachments", Attachments.Count.ToString()),
        new("received", ReceivedAt.ToString("yyyy-MM-dd HH:mm:ss")),
        new("ts (unix)", UnixTimestamp.ToString())
    ];

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged(string? name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
