using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using MandrillSimulator.ViewModels;

namespace MandrillSimulator.Tests;

// MenuItem.IsChecked binds OneWay by default — unlike CheckBox, ComboBox and
// TextBox, which are all TwoWay. A menu toggle therefore flips its own tick and
// silently never reaches the view model unless the binding says so explicitly.
// These load the real XAML, so the binding mode itself is what is under test.
public class MenuToggleTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.StartNew(typeof(HeadlessApp));

    [Theory]
    [InlineData("Dark theme")]
    [InlineData("Sidebar")]
    [InlineData("Message list")]
    public async Task CheckingAMenuToggleReachesTheViewModel(string header)
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            var viewModel = Assert.IsType<MainViewModel>(window.DataContext);
            var item = FindMenuItem(window, header);

            var before = Read(viewModel, header);
            item.IsChecked = !before;

            Assert.Equal(!before, Read(viewModel, header));
        }, CancellationToken.None);
    }

    [Fact]
    public async Task TheDarkThemeToggleSwitchesTheApplicationThemeVariant()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            var item = FindMenuItem(window, "Dark theme");

            item.IsChecked = true;
            Assert.Equal(ThemeVariant.Dark, Application.Current!.RequestedThemeVariant);

            item.IsChecked = false;
            Assert.Equal(ThemeVariant.Light, Application.Current!.RequestedThemeVariant);
        }, CancellationToken.None);
    }

    private static bool Read(MainViewModel viewModel, string header) => header switch
    {
        "Dark theme" => viewModel.IsDarkTheme,
        "Sidebar" => viewModel.ShowSidebar,
        "Message list" => viewModel.ShowMessageList,
        _ => throw new ArgumentOutOfRangeException(nameof(header), header, null)
    };

    private static MenuItem FindMenuItem(Window window, string header) =>
        window.GetLogicalDescendants()
            .OfType<MenuItem>()
            .First(m => Equals(m.Header, header));
}
