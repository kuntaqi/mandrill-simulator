using System.ComponentModel;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using MandrillSimulator.Models;
using MandrillSimulator.ViewModels;

namespace MandrillSimulator;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();
    private bool _previewReady;
    private bool _suppressStateChange;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;

        _viewModel.SelectionChanged += (_, message) => ShowPreview(message);
        _viewModel.ThemeChanged += (_, dark) => ApplyTheme(dark);
        _viewModel.SaveAttachmentRequested += async (_, attachment) => await SaveAttachmentAsync(attachment);
        _viewModel.ExportSessionRequested += async (_, json) => await ExportSessionAsync(json);

        Opened += OnOpened;
        Closing += OnClosing;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        InitialisePreview();
        await _viewModel.InitialiseAsync();
        ApplyTheme(_viewModel.IsDarkTheme);
    }

    private async void OnClosing(object? sender, CancelEventArgs e) => await _viewModel.ShutdownAsync();

    private void InitialisePreview()
    {
        var browser = this.FindControl<NativeWebView>("PreviewBrowser");
        if (browser is null) return;

        try
        {
            // Captured mail is untrusted content: render it, never follow it.
            browser.NavigationStarted += OnPreviewNavigationStarted;
            browser.NewWindowRequested += (_, args) => args.Handled = true;
            _previewReady = true;
        }
        catch (Exception ex)
        {
            ShowPreviewFallback($"HTML preview unavailable on this platform: {ex.Message}");
        }
    }

    // Following the link would navigate the pane away from the email. Cancelling
    // it and counting a click is both safer and what a tester actually wants.
    private void OnPreviewNavigationStarted(object? sender, WebViewNavigationStartingEventArgs e)
    {
        var uri = e.Request?.ToString() ?? string.Empty;
        if (uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            || uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        e.Cancel = true;

        if (_viewModel.SelectedMessage is null) return;
        _viewModel.SimulateClickCommand.Execute(null);
        _viewModel.StatusMessage = $"Counted a click on {uri}";
    }

    private void ShowPreview(CapturedMessage? message)
    {
        if (!_previewReady) return;

        var browser = this.FindControl<NativeWebView>("PreviewBrowser");
        if (browser is null) return;

        var html = string.IsNullOrWhiteSpace(message?.Html)
            ? "<html><body style=\"font-family:system-ui,sans-serif;color:#777;padding:24px\">This message carried no HTML body.</body></html>"
            : Wrap(message!.Html);

        try
        {
            browser.NavigateToString(html, new Uri("https://localhost/"));
        }
        catch (Exception ex)
        {
            ShowPreviewFallback($"Could not render this message: {ex.Message}");
        }
    }

    // Mail is authored for a light page, and a native WebView otherwise inherits
    // the desktop's dark scheme and repaints the body on black.
    private static string Wrap(string html)
    {
        var head = "<meta name=\"color-scheme\" content=\"light\">"
                   + "<style>html,body{background:#fff;color-scheme:light}</style>";

        var index = html.IndexOf("<head", StringComparison.OrdinalIgnoreCase);
        if (index >= 0)
        {
            var close = html.IndexOf('>', index);
            if (close > 0) return html.Insert(close + 1, head);
        }

        return $"<html><head>{head}</head><body>{html}</body></html>";
    }

    private void ShowPreviewFallback(string message)
    {
        _previewReady = false;

        var browser = this.FindControl<NativeWebView>("PreviewBrowser");
        var fallback = this.FindControl<TextBlock>("PreviewFallback");
        if (browser is not null) browser.IsVisible = false;
        if (fallback is null) return;

        fallback.Text = $"{message}\n\nUse the HTML source tab to read the message body.";
        fallback.IsVisible = true;
    }

    private static void ApplyTheme(bool dark)
    {
        if (Application.Current is null) return;
        Application.Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
    }

    private async void OnCopyUrl(object? sender, RoutedEventArgs e)
    {
        var clipboard = GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return;

        await clipboard.SetTextAsync(_viewModel.ListeningUrl);
        _viewModel.StatusMessage = "Base URL copied.";
    }

    private void OnToggleAutoSelect(object? sender, RoutedEventArgs e) =>
        _viewModel.AutoSelectNewest = !_viewModel.AutoSelectNewest;

    private async void OnConnectProject(object? sender, RoutedEventArgs e)
    {
        var dialog = new ConnectProjectWindow(_viewModel.ListeningUrl);
        var confirmed = await dialog.ShowDialog<bool>(this);
        if (!confirmed) return;

        _viewModel.ConnectProjectCommand.Execute((dialog.ConfigPath, dialog.KeyPath));
    }

    private void OnStateSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressStateChange || sender is not ComboBox box || box.SelectedItem is not string wire) return;
        if (_viewModel.SelectedMessage is null || _viewModel.SelectedMessage.StateWire == wire) return;

        _suppressStateChange = true;
        _viewModel.SetStateCommand.Execute(wire);
        _suppressStateChange = false;
    }

    private async Task SaveAttachmentAsync(CapturedAttachment attachment)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save attachment",
            SuggestedFileName = attachment.Name
        });

        if (file is null) return;

        try
        {
            await using var stream = await file.OpenWriteAsync();
            await stream.WriteAsync(attachment.Content);
            _viewModel.StatusMessage = $"Saved {attachment.Name}.";
        }
        catch (Exception ex)
        {
            _viewModel.StatusMessage = $"Could not save {attachment.Name}: {ex.Message}";
        }
    }

    private async Task ExportSessionAsync(string json)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export session",
            SuggestedFileName = $"mandrill-session-{DateTime.Now:yyyyMMdd-HHmmss}.json"
        });

        if (file is null) return;

        try
        {
            await using var stream = await file.OpenWriteAsync();
            await stream.WriteAsync(Encoding.UTF8.GetBytes(json));
            _viewModel.StatusMessage = "Session exported.";
        }
        catch (Exception ex)
        {
            _viewModel.StatusMessage = $"Could not export the session: {ex.Message}";
        }
    }

    private void OnAbout(object? sender, RoutedEventArgs e) =>
        _viewModel.StatusMessage =
            "Mandrill Simulator · a local stand-in for the Mandrill HTTP API · loopback only, nothing is sent.";

    private void OnExit(object? sender, RoutedEventArgs e) => Close();
}
