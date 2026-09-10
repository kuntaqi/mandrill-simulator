using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace MandrillSimulator;

public partial class ConnectProjectWindow : Window
{
    public ConnectProjectWindow() : this("http://localhost:8025/")
    {
    }

    public ConnectProjectWindow(string baseUrl)
    {
        InitializeComponent();
        PreviewText.Text = $"\"<key>\": \"{baseUrl}\"";
    }

    public string ConfigPath => PathBox.Text?.Trim() ?? string.Empty;

    public string KeyPath => KeyBox.Text?.Trim() ?? string.Empty;

    private async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Pick a JSON configuration file",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("JSON configuration") { Patterns = ["*.json"] },
                new FilePickerFileType("All files") { Patterns = ["*"] }
            ]
        });

        if (files.Count > 0) PathBox.Text = files[0].TryGetLocalPath() ?? files[0].Path.LocalPath;
    }

    private void OnConnect(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ConfigPath) || !File.Exists(ConfigPath))
        {
            PreviewText.Text = "Pick a configuration file that exists.";
            return;
        }

        if (string.IsNullOrWhiteSpace(KeyPath))
        {
            PreviewText.Text = "Enter the key that should hold the base URL.";
            return;
        }

        Close(true);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
