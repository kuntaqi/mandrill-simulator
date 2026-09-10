using System.Collections.ObjectModel;
using System.Text;
using Avalonia.Threading;
using MandrillSimulator.Api;
using MandrillSimulator.Models;
using MandrillSimulator.Services;
using MandrillSimulator.Store;

namespace MandrillSimulator.ViewModels;

public class MainViewModel : ObservableObject
{
    private readonly MessageStore _store = new();
    private readonly ExportJobStore _jobs = new();
    private readonly SettingsService _settingsService = new();
    private readonly ConfigConnector _connector = new();
    private readonly SampleMessageSender _sampleSender = new();
    private readonly SimulatorServer _server;
    private readonly List<CapturedMessage> _all = [];
    private readonly DispatcherTimer _uptimeTimer;

    private SimulatorSettings _settings = new();
    private CapturedMessage? _selectedMessage;
    private FilterEntry _selectedStateFilter;
    private FilterEntry? _selectedTagFilter;
    private DateTimeOffset? _startedAt;
    private string _searchText = string.Empty;
    private string _lastRequestText = "No requests yet";
    private string _statusMessage = string.Empty;
    private string _uptime = string.Empty;
    private string _bounceReasonDraft = string.Empty;
    private int _port = 8025;
    private bool _isRunning;
    private bool _isDarkTheme;
    private bool _autoSelectNewest = true;
    private bool _newestFirst = true;
    private bool _showSidebar = true;
    private bool _showMessageList = true;

    public MainViewModel()
    {
        _server = new SimulatorServer(_store, _jobs);
        _server.RequestHandled += OnRequestHandled;
        _server.Faulted += (_, message) => OnUi(() => StatusMessage = message);

        _store.MessageAdded += (_, message) => OnUi(() => AddMessage(message));
        _store.Cleared += (_, _) => OnUi(ResetMessages);

        StateFilters =
        [
            new FilterEntry("All messages", FilterKind.All),
            .. MessageStates.All.Select(s => new FilterEntry(Humanise(s), FilterKind.State, MessageStates.ToWire(s)))
        ];
        _selectedStateFilter = StateFilters[0];

        TagFilters = [];

        EndpointHits =
        [
            new EndpointHit("/messages/send.json"),
            new EndpointHit("messages/search"),
            new EndpointHit("/exports/activity"),
            new EndpointHit("/exports/info"),
            new EndpointHit("/rejects/delete.json")
        ];

        _uptimeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _uptimeTimer.Tick += (_, _) => Raise(nameof(UptimeText));

        StartServerCommand = new RelayCommand(_ => StartServer(), _ => !IsRunning);
        StopServerCommand = new RelayCommand(_ => StopServer(), _ => IsRunning);
        ToggleServerCommand = new RelayCommand(_ =>
        {
            if (IsRunning) StopServer();
            else StartServer();
        });
        ClearAllCommand = new RelayCommand(_ => _store.Clear());
        RefreshCommand = new RelayCommand(_ => ApplyFilter());
        ToggleSortCommand = new RelayCommand(_ =>
        {
            NewestFirst = !NewestFirst;
            ApplyFilter();
        });
        SimulateOpenCommand = new RelayCommand(_ => Bump(m => m.Opens++), _ => HasSelection);
        SimulateClickCommand = new RelayCommand(_ => Bump(m => m.Clicks++), _ => HasSelection);
        ResetCountersCommand = new RelayCommand(_ => Bump(m =>
        {
            m.Opens = 0;
            m.Clicks = 0;
        }), _ => HasSelection);
        SetStateCommand = new RelayCommand(SetState, _ => HasSelection);
        ApplyBounceReasonCommand = new RelayCommand(_ => ApplyBounceReason(), _ => HasSelection);
        SendSampleCommand = new RelayCommand(async _ => await SendSampleAsync(), _ => IsRunning);
        ResendCommand = new RelayCommand(async _ => await ResendAsync(), _ => HasSelection && IsRunning);
        FailNextRequestCommand = new RelayCommand(_ =>
        {
            _server.FailNextRequest = true;
            StatusMessage = "The next API call will be answered with a 500.";
        });
        ConnectProjectCommand = new RelayCommand(async parameter => await ConnectAsync(parameter));
        DisconnectProjectCommand = new RelayCommand(async parameter => await DisconnectAsync(parameter));
        SaveAttachmentCommand = new RelayCommand(p =>
        {
            if (p is CapturedAttachment attachment) SaveAttachmentRequested?.Invoke(this, attachment);
        });
        ExportSessionCommand = new RelayCommand(_ => ExportSessionRequested?.Invoke(this, BuildSessionExport()));
    }

    public ObservableCollection<CapturedMessage> Messages { get; } = [];
    public ObservableCollection<FilterEntry> StateFilters { get; }
    public ObservableCollection<FilterEntry> TagFilters { get; }
    public ObservableCollection<EndpointHit> EndpointHits { get; }
    public ObservableCollection<ConnectedProject> ConnectedProjects { get; } = [];

    public RelayCommand StartServerCommand { get; }
    public RelayCommand StopServerCommand { get; }
    public RelayCommand ToggleServerCommand { get; }
    public RelayCommand ClearAllCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand ToggleSortCommand { get; }
    public RelayCommand SimulateOpenCommand { get; }
    public RelayCommand SimulateClickCommand { get; }
    public RelayCommand ResetCountersCommand { get; }
    public RelayCommand SetStateCommand { get; }
    public RelayCommand ApplyBounceReasonCommand { get; }
    public RelayCommand SendSampleCommand { get; }
    public RelayCommand ResendCommand { get; }
    public RelayCommand FailNextRequestCommand { get; }
    public RelayCommand ConnectProjectCommand { get; }
    public RelayCommand DisconnectProjectCommand { get; }
    public RelayCommand SaveAttachmentCommand { get; }
    public RelayCommand ExportSessionCommand { get; }

    public event EventHandler<CapturedMessage?>? SelectionChanged;
    public event EventHandler<bool>? ThemeChanged;
    public event EventHandler<CapturedAttachment>? SaveAttachmentRequested;
    public event EventHandler<string>? ExportSessionRequested;

    public IReadOnlyList<string> AvailableStates { get; } =
        MessageStates.All.Select(MessageStates.ToWire).ToList();

    public string ListeningUrl => _server.BaseUrl;

    public string ShortUrl => $"http://localhost:{Port}";

    public string VersionText => "v0.2.0 · .NET 10";

    public string StoreText => "Store: in-memory (this session)";

    public int MessageCount => _all.Count;

    public string MessageCountText => _all.Count == 1 ? "1 message" : $"{_all.Count} messages";

    public string CapturedText =>
        $"{_all.Count} captured · {(NewestFirst ? "newest first" : "oldest first")}";

    public bool HasMessages => _all.Count > 0;

    public string ListeningStatusText => IsRunning
        ? $"Listening on http://127.0.0.1:{Port}"
        : "Server stopped";

    public string UptimeText
    {
        get
        {
            if (!IsRunning || _startedAt is null) return string.Empty;
            var elapsed = DateTimeOffset.Now - _startedAt.Value;
            return elapsed.TotalHours >= 1
                ? $"up {(int)elapsed.TotalHours}h {elapsed.Minutes:00}m"
                : $"up {elapsed.Minutes}m {elapsed.Seconds:00}s";
        }
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (!Set(ref _isRunning, value)) return;
            Raise(nameof(ServerStatusText));
            Raise(nameof(ListeningStatusText));
            Raise(nameof(UptimeText));
            StartServerCommand.RaiseCanExecuteChanged();
            StopServerCommand.RaiseCanExecuteChanged();
            SendSampleCommand.RaiseCanExecuteChanged();
            ResendCommand.RaiseCanExecuteChanged();
        }
    }

    public string ServerStatusText => IsRunning ? "Listening" : "Stopped";

    public int Port
    {
        get => _port;
        set
        {
            if (!Set(ref _port, value)) return;
            Raise(nameof(ListeningUrl));
            Raise(nameof(ShortUrl));
            Raise(nameof(ListeningStatusText));
        }
    }

    public bool IsDarkTheme
    {
        get => _isDarkTheme;
        set
        {
            if (!Set(ref _isDarkTheme, value)) return;
            _settings.DarkTheme = value;
            ThemeChanged?.Invoke(this, value);
        }
    }

    public bool AutoSelectNewest
    {
        get => _autoSelectNewest;
        set
        {
            if (!Set(ref _autoSelectNewest, value)) return;
            _settings.AutoSelectNewest = value;
        }
    }

    public bool NewestFirst
    {
        get => _newestFirst;
        set
        {
            if (Set(ref _newestFirst, value)) Raise(nameof(CapturedText));
        }
    }

    public bool ShowSidebar
    {
        get => _showSidebar;
        set => Set(ref _showSidebar, value);
    }

    public bool ShowMessageList
    {
        get => _showMessageList;
        set => Set(ref _showMessageList, value);
    }

    public CapturedMessage? SelectedMessage
    {
        get => _selectedMessage;
        set
        {
            if (!Set(ref _selectedMessage, value)) return;
            Raise(nameof(HasSelection));
            Raise(nameof(SelectedAttachments));
            Raise(nameof(SelectedEmbeddedImages));
            Raise(nameof(AttachmentCountText));
            Raise(nameof(ReceivedText));
            Raise(nameof(SimulatorSubtitle));
            Raise(nameof(HasFailureDetail));
            Raise(nameof(FailureDetail));
            SimulateOpenCommand.RaiseCanExecuteChanged();
            SimulateClickCommand.RaiseCanExecuteChanged();
            ResetCountersCommand.RaiseCanExecuteChanged();
            SetStateCommand.RaiseCanExecuteChanged();
            ResendCommand.RaiseCanExecuteChanged();
            ApplyBounceReasonCommand.RaiseCanExecuteChanged();
            SelectionChanged?.Invoke(this, value);
        }
    }

    public bool HasSelection => SelectedMessage is not null;

    public string ReceivedText => SelectedMessage is null
        ? string.Empty
        : $"received {SelectedMessage.ReceivedAt:d MMM yyyy, HH:mm:ss}";

    public string SimulatorSubtitle => SelectedMessage is null
        ? string.Empty
        : $"· sets what messages/search reports back for _id {SelectedMessage.Id}";

    public bool HasFailureDetail => SelectedMessage is not null
                                    && (!string.IsNullOrWhiteSpace(SelectedMessage.RejectReason)
                                        || !string.IsNullOrWhiteSpace(SelectedMessage.BounceDescription));

    public string FailureDetail => SelectedMessage is null
        ? string.Empty
        : !string.IsNullOrWhiteSpace(SelectedMessage.RejectReason)
            ? $"reject_reason: {SelectedMessage.RejectReason}"
            : $"bounce_description: {SelectedMessage.BounceDescription}";

    public IReadOnlyList<CapturedAttachment> SelectedAttachments =>
        SelectedMessage?.Attachments.Where(a => !a.IsEmbeddedImage).ToList() ?? [];

    public IReadOnlyList<CapturedAttachment> SelectedEmbeddedImages =>
        SelectedMessage?.Attachments.Where(a => a.IsEmbeddedImage).ToList() ?? [];

    public string AttachmentCountText => SelectedAttachments.Count > 0
        ? SelectedAttachments.Count.ToString()
        : string.Empty;

    public FilterEntry SelectedStateFilter
    {
        get => _selectedStateFilter;
        set
        {
            if (value is null || !Set(ref _selectedStateFilter, value)) return;
            _selectedTagFilter = null;
            Raise(nameof(SelectedTagFilter));
            ApplyFilter();
        }
    }

    public FilterEntry? SelectedTagFilter
    {
        get => _selectedTagFilter;
        set
        {
            if (!Set(ref _selectedTagFilter, value)) return;
            if (value is not null)
            {
                _selectedStateFilter = StateFilters[0];
                Raise(nameof(SelectedStateFilter));
            }

            ApplyFilter();
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (Set(ref _searchText, value)) ApplyFilter();
        }
    }

    public string BounceReasonDraft
    {
        get => _bounceReasonDraft;
        set => Set(ref _bounceReasonDraft, value);
    }

    public string LastRequestText
    {
        get => _lastRequestText;
        private set => Set(ref _lastRequestText, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => Set(ref _statusMessage, value);
    }

    public async Task InitialiseAsync()
    {
        _settings = await _settingsService.LoadAsync();
        Port = _settings.Port;
        IsDarkTheme = _settings.DarkTheme;
        AutoSelectNewest = _settings.AutoSelectNewest;

        foreach (var project in _settings.RecentProjects) ConnectedProjects.Add(project);

        if (_settings.AutoStart) StartServer();
    }

    public async Task ShutdownAsync()
    {
        _settings.Port = Port;
        _settings.RecentProjects = ConnectedProjects.ToList();
        await _settingsService.SaveAsync(_settings);
        _uptimeTimer.Stop();
        _server.Stop();
    }

    public void StartServer()
    {
        try
        {
            _server.Start(Port);
            _startedAt = DateTimeOffset.Now;
            IsRunning = true;
            _uptimeTimer.Start();
            StatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            IsRunning = false;
            StatusMessage = $"Could not listen on port {Port}: {ex.Message}";
        }
    }

    public void StopServer()
    {
        _server.Stop();
        _uptimeTimer.Stop();
        _startedAt = null;
        IsRunning = false;
    }

    private void AddMessage(CapturedMessage message)
    {
        _all.Add(message);
        RefreshCounts();
        ApplyFilter();

        if (AutoSelectNewest || SelectedMessage is null) SelectedMessage = message;
    }

    private void ResetMessages()
    {
        _all.Clear();
        Messages.Clear();
        SelectedMessage = null;
        RefreshCounts();
    }

    private void RefreshCounts()
    {
        Raise(nameof(MessageCount));
        Raise(nameof(MessageCountText));
        Raise(nameof(CapturedText));
        Raise(nameof(HasMessages));

        foreach (var filter in StateFilters)
        {
            filter.Count = filter.Kind == FilterKind.All
                ? _all.Count
                : _all.Count(m => m.StateWire == filter.Value);
        }

        var tags = _all
            .SelectMany(m => m.Tags.Count > 0 ? m.Tags : ["(untagged)"])
            .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var keepSelected = SelectedTagFilter?.Value;
        TagFilters.Clear();
        foreach (var tag in tags)
        {
            var entry = new FilterEntry(tag.Key, FilterKind.Tag, tag.Key) { Count = tag.Count() };
            TagFilters.Add(entry);
            if (string.Equals(keepSelected, tag.Key, StringComparison.OrdinalIgnoreCase))
                _selectedTagFilter = entry;
        }

        Raise(nameof(SelectedTagFilter));
    }

    private void ApplyFilter()
    {
        var previous = SelectedMessage;

        IEnumerable<CapturedMessage> query = _all;

        if (SelectedTagFilter is { Value: { } tag })
        {
            query = query.Where(m => (m.Tags.Count > 0 ? m.Tags : ["(untagged)"])
                .Contains(tag, StringComparer.OrdinalIgnoreCase));
        }
        else if (SelectedStateFilter.Kind == FilterKind.State)
        {
            query = query.Where(m => m.StateWire == SelectedStateFilter.Value);
        }

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var needle = SearchText.Trim();
            query = query.Where(m =>
                m.ToEmail.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || m.Subject.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || m.Id.Contains(needle, StringComparison.OrdinalIgnoreCase));
        }

        query = NewestFirst
            ? query.OrderByDescending(m => m.ReceivedAt)
            : query.OrderBy(m => m.ReceivedAt);

        Messages.Clear();
        foreach (var message in query) Messages.Add(message);

        if (previous is not null && Messages.Contains(previous)) SelectedMessage = previous;
        else if (!Messages.Contains(SelectedMessage!)) SelectedMessage = Messages.FirstOrDefault();
    }

    private void Bump(Action<CapturedMessage> change)
    {
        if (SelectedMessage is null) return;
        change(SelectedMessage);
        StatusMessage =
            $"messages/search now reports opens {SelectedMessage.Opens}, clicks {SelectedMessage.Clicks} for this message.";
    }

    private void SetState(object? parameter)
    {
        if (SelectedMessage is null || parameter is not string wire) return;
        if (SelectedMessage.StateWire == wire) return;

        var state = MessageStates.FromWire(wire);
        SelectedMessage.State = state;

        if (state is MessageState.Rejected or MessageState.Invalid)
        {
            SelectedMessage.RejectReason ??= "invalid-sender";
            SelectedMessage.BounceDescription = null;
        }
        else if (state is MessageState.Bounced or MessageState.SoftBounced)
        {
            SelectedMessage.BounceDescription ??= "smtp; 550 mailbox unavailable";
            SelectedMessage.RejectReason = null;
        }
        else
        {
            SelectedMessage.RejectReason = null;
            SelectedMessage.BounceDescription = null;
        }

        Raise(nameof(HasFailureDetail));
        Raise(nameof(FailureDetail));
        RefreshCounts();
        ApplyFilter();
    }

    private void ApplyBounceReason()
    {
        if (SelectedMessage is null || string.IsNullOrWhiteSpace(BounceReasonDraft)) return;

        if (SelectedMessage.State is MessageState.Rejected or MessageState.Invalid)
            SelectedMessage.RejectReason = BounceReasonDraft.Trim();
        else
            SelectedMessage.BounceDescription = BounceReasonDraft.Trim();

        Raise(nameof(HasFailureDetail));
        Raise(nameof(FailureDetail));
        StatusMessage = "Failure detail updated.";
    }

    private async Task SendSampleAsync()
    {
        try
        {
            await _sampleSender.SendAsync(_server.BaseUrl);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Sample send failed: {ex.Message}";
        }
    }

    private async Task ResendAsync()
    {
        if (SelectedMessage is null || string.IsNullOrWhiteSpace(SelectedMessage.RawRequestJson)) return;

        try
        {
            await _sampleSender.SendRawAsync(_server.BaseUrl, SelectedMessage.RawRequestJson);
            StatusMessage = "Replayed the captured request against the simulator.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Resend failed: {ex.Message}";
        }
    }

    private string BuildSessionExport()
    {
        var export = new StringBuilder();
        export.AppendLine("[");

        for (var i = 0; i < _all.Count; i++)
        {
            var message = _all[i];
            export.AppendLine("  {");
            export.AppendLine($"    \"_id\": \"{message.Id}\",");
            export.AppendLine($"    \"received\": \"{message.ReceivedAt:yyyy-MM-dd HH:mm:ss}\",");
            export.AppendLine($"    \"state\": \"{message.StateWire}\",");
            export.AppendLine($"    \"to\": \"{message.ToEmail}\",");
            export.AppendLine($"    \"subject\": {System.Text.Json.JsonSerializer.Serialize(message.Subject)},");
            export.AppendLine($"    \"opens\": {message.Opens},");
            export.AppendLine($"    \"clicks\": {message.Clicks},");
            export.AppendLine($"    \"request\": {message.RawRequestJson}");
            export.AppendLine(i == _all.Count - 1 ? "  }" : "  },");
        }

        export.AppendLine("]");
        return export.ToString();
    }

    private async Task ConnectAsync(object? parameter)
    {
        if (parameter is not ValueTuple<string, string> target) return;

        var (configPath, keyPath) = target;
        try
        {
            var project = await _connector.ConnectAsync(configPath, keyPath, _server.BaseUrl);
            var existing = ConnectedProjects.FirstOrDefault(p =>
                string.Equals(p.ConfigPath, configPath, StringComparison.OrdinalIgnoreCase));

            if (existing is not null) ConnectedProjects.Remove(existing);
            ConnectedProjects.Add(project);

            StatusMessage = $"{Path.GetFileName(configPath)} now points at {_server.BaseUrl}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not connect that project: {ex.Message}";
        }
    }

    private async Task DisconnectAsync(object? parameter)
    {
        if (parameter is not ConnectedProject project) return;

        try
        {
            await _connector.DisconnectAsync(project);
            ConnectedProjects.Remove(project);
            StatusMessage = $"{Path.GetFileName(project.ConfigPath)} restored to its previous value.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not restore that project: {ex.Message}";
        }
    }

    private void OnRequestHandled(object? sender, RequestLogEntry entry) => OnUi(() =>
    {
        LastRequestText =
            $"Last request: {entry.Method} {entry.RawPath} · {entry.StatusCode} · {entry.At:HH:mm:ss}";

        var hit = EndpointHits.FirstOrDefault(h => SimulatorServer.NormalisePath(h.Path) == entry.Route);
        if (hit is not null) hit.Hits++;
    });

    private static string Humanise(MessageState state) =>
        state == MessageState.SoftBounced ? "Soft-bounced" : state.ToString();

    private static void OnUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }
}
