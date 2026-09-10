using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using MandrillSimulator.Models;
using MandrillSimulator.ViewModels;
using Microsoft.Web.WebView2.Core;

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

        _viewModel.SelectionChanged += (_, message) => _ = ShowPreviewAsync(message);
        _viewModel.ThemeChanged += (_, dark) => ApplyTheme(dark);

        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await InitialisePreviewAsync();
        await _viewModel.InitialiseAsync();
        ApplyTheme(_viewModel.IsDarkTheme);
    }

    private async void OnClosing(object? sender, CancelEventArgs e) => await _viewModel.ShutdownAsync();

    private async Task InitialisePreviewAsync()
    {
        try
        {
            PreviewBrowser.DefaultBackgroundColor = System.Drawing.Color.White;

            // Keep the browser profile out of the install directory, which may
            // not be writable where a tester unzips this.
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MandrillSimulator",
                "WebView2");

            var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await PreviewBrowser.EnsureCoreWebView2Async(environment);

            // Mail is authored for a light page. Without this the profile follows
            // the OS into dark mode and repaints every email on black.
            PreviewBrowser.CoreWebView2.Profile.PreferredColorScheme =
                CoreWebView2PreferredColorScheme.Light;

            var settings = PreviewBrowser.CoreWebView2.Settings;
            // Captured mail is untrusted content: render it, never run it.
            settings.IsScriptEnabled = false;
            settings.AreDefaultContextMenusEnabled = false;
            settings.AreDevToolsEnabled = false;
            settings.IsStatusBarEnabled = false;

            PreviewBrowser.CoreWebView2.NavigationStarting += OnPreviewNavigationStarting;
            PreviewBrowser.CoreWebView2.NewWindowRequested += OnPreviewNewWindowRequested;

            _previewReady = true;
        }
        catch (Exception ex)
        {
            _viewModel.StatusMessage =
                $"HTML preview unavailable (WebView2 runtime missing?): {ex.Message}";
        }
    }

    // Following a link would take the pane away from the email. Cancelling it and
    // counting a click is both safer and what a tester actually wants.
    private void OnPreviewNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (e.Uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return;

        e.Cancel = true;

        if (_viewModel.SelectedMessage is null) return;
        _viewModel.SimulateClickCommand.Execute(null);
        _viewModel.StatusMessage = $"Counted a click on {e.Uri}";
    }

    private void OnPreviewNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e) =>
        e.Handled = true;

    private async Task ShowPreviewAsync(CapturedMessage? message)
    {
        if (!_previewReady) return;

        var html = string.IsNullOrWhiteSpace(message?.Html)
            ? "<html><body style=\"font-family:Segoe UI,Arial,sans-serif;color:#777;padding:24px\">This message carried no HTML body.</body></html>"
            : message!.Html;

        try
        {
            PreviewBrowser.NavigateToString(html);
        }
        catch (ArgumentException)
        {
            // NavigateToString caps the payload; fall back to a note rather than throwing.
            PreviewBrowser.NavigateToString(
                "<html><body style=\"font-family:Segoe UI,Arial,sans-serif;padding:24px\">"
                + "This message is too large to preview. Use the HTML source tab.</body></html>");
        }

        await Task.CompletedTask;
    }

    private void ApplyTheme(bool dark)
    {
        var source = new Uri(dark ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative);
        var dictionary = new ResourceDictionary { Source = source };

        var merged = Application.Current.Resources.MergedDictionaries;
        if (merged.Count > 0) merged[0] = dictionary;
        else merged.Add(dictionary);
    }

    private void OnCopyUrl(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_viewModel.ListeningUrl);
            _viewModel.StatusMessage = "Base URL copied.";
        }
        catch (Exception ex)
        {
            _viewModel.StatusMessage = $"Could not copy: {ex.Message}";
        }
    }

    private void OnConnectProject(object sender, RoutedEventArgs e)
    {
        var dialog = new ConnectProjectWindow(_viewModel.ListeningUrl) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        _viewModel.ConnectProjectCommand.Execute((dialog.ConfigPath, dialog.KeyPath));
    }

    private void OnStateSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressStateChange || sender is not ComboBox box || box.SelectedItem is not string wire) return;
        if (_viewModel.SelectedMessage is null) return;
        if (_viewModel.SelectedMessage.StateWire == wire) return;

        _suppressStateChange = true;
        _viewModel.SetStateCommand.Execute(wire);
        _suppressStateChange = false;
    }

    private void OnExit(object sender, RoutedEventArgs e) => Close();
}
