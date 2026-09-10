using System.IO;
using System.Windows;

namespace MandrillSimulator;

public partial class ConnectProjectWindow : Window
{
    public ConnectProjectWindow(string baseUrl)
    {
        InitializeComponent();
        PreviewText.Text = $"\"{{key}}\": \"{baseUrl}\"";
        BaseUrl = baseUrl;
    }

    public string BaseUrl { get; }

    public string ConfigPath => PathBox.Text.Trim();

    public string KeyPath => KeyBox.Text.Trim();

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Pick a JSON configuration file",
            Filter = "JSON configuration|*.json|All files|*.*"
        };

        if (dialog.ShowDialog() == true) PathBox.Text = dialog.FileName;
    }

    private void OnConnect(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(ConfigPath))
        {
            MessageBox.Show(this, "Pick a configuration file that exists.", "Connect a project",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(KeyPath))
        {
            MessageBox.Show(this, "Enter the key that should hold the base URL.", "Connect a project",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
