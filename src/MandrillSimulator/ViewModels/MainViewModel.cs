using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Data;
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

    private SimulatorSettings _settings = new();
    private CapturedMessage? _selectedMessage;
    private FilterEntry _selectedFilter;
    private string _searchText = string.Empty;
    private string _lastRequestText = "No requests yet";
    private string _statusMessage = string.Empty;
    private int _port = 8025;
    private bool _isRunning;
    private bool _isDarkTheme;

    public MainViewModel()
    {
        _server = new SimulatorServer(_store, _jobs);
        _server.RequestHandled += OnRequestHandled;
        _server.Faulted += (_, message) => OnUi(() => StatusMessage = message);

        _store.MessageAdded += (_, message) => OnUi(() => AddMessage(message));
        _store.Cleared += (_, _) => OnUi(ResetMessages);

        Filters =
        [
            new FilterEntry("All messages", FilterKind.All),
            .. MessageStates.All.Select(s => new FilterEntry(Humanise(s), FilterKind.State, MessageStates.ToWire(s)))
        ];
        _selectedFilter = Filters[0];

        TagFilters = [];

        EndpointHits =
        [
            new EndpointHit("/messages/send.json"),
            new EndpointHit("messages/search"),
            new EndpointHit("/exports/activity"),
            new EndpointHit("/exports/info"),
            new EndpointHit("/rejects/delete.json")
        ];

        MessagesView = CollectionViewSource.GetDefaultView(Messages);
        MessagesView.Filter = FilterMessage;
        MessagesView.SortDescriptions.Add(new SortDescription(nameof(CapturedMessage.ReceivedAt),
            ListSortDirection.Descending));

        StartServerCommand = new RelayCommand(_ => StartServer(), _ => !IsRunning);
        StopServerCommand = new RelayCommand(_ => StopServer(), _ => IsRunning);
        ClearAllCommand = new RelayCommand(_ => _store.Clear());
        SimulateOpenCommand = new RelayCommand(_ => Bump(m => m.Opens++), _ => SelectedMessage is not null);
        SimulateClickCommand = new RelayCommand(_ => Bump(m => m.Clicks++), _ => SelectedMessage is not null);
        ResetCountersCommand = new RelayCommand(_ => Bump(m =>
        {
            m.Opens = 0;
            m.Clicks = 0;
        }), _ => SelectedMessage is not null);
        SetStateCommand = new RelayCommand(SetState, _ => SelectedMessage is not null);
        SendSampleCommand = new RelayCommand(async _ => await SendSampleAsync(), _ => IsRunning);
        FailNextRequestCommand = new RelayCommand(_ =>
        {
            _server.FailNextRequest = true;
            StatusMessage = "The next API call will be answered with a 500.";
        });
        ConnectProjectCommand = new RelayCommand(async parameter => await ConnectAsync(parameter));
        DisconnectProjectCommand = new RelayCommand(async parameter => await DisconnectAsync(parameter));
        SaveAttachmentCommand = new RelayCommand(SaveAttachment);
    }

    public ObservableCollection<CapturedMessage> Messages { get; } = [];
    public ICollectionView MessagesView { get; }
    public ObservableCollection<FilterEntry> Filters { get; }
    public ObservableCollection<FilterEntry> TagFilters { get; }
    public ObservableCollection<EndpointHit> EndpointHits { get; }
    public ObservableCollection<ConnectedProject> ConnectedProjects { get; } = [];

    public RelayCommand StartServerCommand { get; }
    public RelayCommand StopServerCommand { get; }
    public RelayCommand ClearAllCommand { get; }
    public RelayCommand SimulateOpenCommand { get; }
    public RelayCommand SimulateClickCommand { get; }
    public RelayCommand ResetCountersCommand { get; }
    public RelayCommand SetStateCommand { get; }
    public RelayCommand SendSampleCommand { get; }
    public RelayCommand FailNextRequestCommand { get; }
    public RelayCommand ConnectProjectCommand { get; }
    public RelayCommand DisconnectProjectCommand { get; }
    public RelayCommand SaveAttachmentCommand { get; }

    public IReadOnlyList<string> AvailableStates { get; } =
        MessageStates.All.Select(MessageStates.ToWire).ToList();

    public string ListeningUrl => _server.BaseUrl;

    public string ConfigHint => $"\"MandrillBaseUrl\": \"{_server.BaseUrl}\"";

    public int MessageCount => Messages.Count;

    public bool HasMessages => Messages.Count > 0;

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (!Set(ref _isRunning, value)) return;
            Raise(nameof(ServerStatusText));
            StartServerCommand.RaiseCanExecuteChanged();
            StopServerCommand.RaiseCanExecuteChanged();
            SendSampleCommand.RaiseCanExecuteChanged();
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
            Raise(nameof(ConfigHint));
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
            _ = _settingsService.SaveAsync(_settings);
        }
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
            SimulateOpenCommand.RaiseCanExecuteChanged();
            SimulateClickCommand.RaiseCanExecuteChanged();
            ResetCountersCommand.RaiseCanExecuteChanged();
            SetStateCommand.RaiseCanExecuteChanged();
            SelectionChanged?.Invoke(this, value);
        }
    }

    public bool HasSelection => SelectedMessage is not null;

    public IReadOnlyList<CapturedAttachment> SelectedAttachments =>
        SelectedMessage?.Attachments.Where(a => !a.IsEmbeddedImage).ToList() ?? [];

    public IReadOnlyList<CapturedAttachment> SelectedEmbeddedImages =>
        SelectedMessage?.Attachments.Where(a => a.IsEmbeddedImage).ToList() ?? [];

    public FilterEntry SelectedFilter
    {
        get => _selectedFilter;
        set
        {
            if (Set(ref _selectedFilter, value)) MessagesView.Refresh();
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (Set(ref _searchText, value)) MessagesView.Refresh();
        }
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

    public event EventHandler<CapturedMessage?>? SelectionChanged;
    public event EventHandler<bool>? ThemeChanged;

    public async Task InitialiseAsync()
    {
        _settings = await _settingsService.LoadAsync();
        Port = _settings.Port;
        IsDarkTheme = _settings.DarkTheme;

        foreach (var project in _settings.RecentProjects) ConnectedProjects.Add(project);

        if (_settings.AutoStart) StartServer();
    }

    public async Task ShutdownAsync()
    {
        _settings.Port = Port;
        _settings.RecentProjects = ConnectedProjects.ToList();
        await _settingsService.SaveAsync(_settings);
        _server.Stop();
    }

    public void StartServer()
    {
        try
        {
            _server.Start(Port);
            IsRunning = true;
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
        IsRunning = false;
    }

    private void AddMessage(CapturedMessage message)
    {
        Messages.Add(message);
        RefreshCounts();

        if (_settings.AutoSelectNewest || SelectedMessage is null) SelectedMessage = message;
    }

    private void ResetMessages()
    {
        Messages.Clear();
        SelectedMessage = null;
        RefreshCounts();
    }

    private void RefreshCounts()
    {
        Raise(nameof(MessageCount));
        Raise(nameof(HasMessages));

        foreach (var filter in Filters)
        {
            filter.Count = filter.Kind == FilterKind.All
                ? Messages.Count
                : Messages.Count(m => m.StateWire == filter.Value);
        }

        var tags = Messages.SelectMany(m => m.Tags.Count > 0 ? m.Tags : ["(untagged)"])
            .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        TagFilters.Clear();
        foreach (var tag in tags)
        {
            TagFilters.Add(new FilterEntry(tag.Key, FilterKind.Tag, tag.Key) { Count = tag.Count() });
        }
    }

    private bool FilterMessage(object item)
    {
        if (item is not CapturedMessage message) return false;

        if (SelectedFilter.Kind == FilterKind.State && message.StateWire != SelectedFilter.Value) return false;

        if (SelectedFilter.Kind == FilterKind.Tag)
        {
            var tags = message.Tags.Count > 0 ? message.Tags : ["(untagged)"];
            if (!tags.Contains(SelectedFilter.Value!, StringComparer.OrdinalIgnoreCase)) return false;
        }

        if (string.IsNullOrWhiteSpace(SearchText)) return true;

        var needle = SearchText.Trim();
        return message.ToEmail.Contains(needle, StringComparison.OrdinalIgnoreCase)
               || message.Subject.Contains(needle, StringComparison.OrdinalIgnoreCase)
               || message.Id.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }

    private void Bump(Action<CapturedMessage> change)
    {
        if (SelectedMessage is null) return;
        change(SelectedMessage);
        StatusMessage = $"messages/search will now report opens {SelectedMessage.Opens}, clicks {SelectedMessage.Clicks} for this message.";
    }

    private void SetState(object? parameter)
    {
        if (SelectedMessage is null || parameter is not string wire) return;

        var state = MessageStates.FromWire(wire);
        SelectedMessage.State = state;

        if (state is MessageState.Rejected or MessageState.Invalid)
            SelectedMessage.RejectReason ??= "invalid-sender";
        else if (state is MessageState.Bounced or MessageState.SoftBounced)
            SelectedMessage.BounceDescription ??= "smtp; 550 mailbox unavailable";
        else
        {
            SelectedMessage.RejectReason = null;
            SelectedMessage.BounceDescription = null;
        }

        RefreshCounts();
        MessagesView.Refresh();
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

    private void SaveAttachment(object? parameter)
    {
        if (parameter is not CapturedAttachment attachment) return;

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = attachment.Name,
            Filter = "All files|*.*"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            File.WriteAllBytes(dialog.FileName, attachment.Content);
            StatusMessage = $"Saved {attachment.Name}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not save {attachment.Name}: {ex.Message}";
        }
    }

    private void OnRequestHandled(object? sender, RequestLogEntry entry) => OnUi(() =>
    {
        LastRequestText = $"{entry.Method} {entry.RawPath} · {entry.StatusCode} · {entry.At:HH:mm:ss} · {entry.ElapsedMs} ms";

        var hit = EndpointHits.FirstOrDefault(h =>
            SimulatorServer.NormalisePath(h.Path) == entry.Route);

        if (hit is not null) hit.Hits++;
    });

    private static string Humanise(MessageState state) =>
        state == MessageState.SoftBounced ? "Soft-bounced" : state.ToString();

    private static void OnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) action();
        else dispatcher.Invoke(action);
    }
}
