using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using LocalNetViewer.App.ViewModels;
using LocalNetViewer.App.Views;
using LocalNetViewer.Platform.Services;

namespace LocalNetViewer.App;

public sealed class App : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Light;
        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://LocalNetViewer.App/App"))
        {
            Source = new Uri("avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml"),
        });
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var discovery = new NetworkDiscoveryService();
            var portScan = new PortScanService();
            var commands = new CommandService();
            var capture = new CaptureInventoryService();
            var platform = new PlatformStatusService();
            desktop.MainWindow = new MainWindow(new MainWindowViewModel(discovery, portScan, commands, capture, platform));
        }

        base.OnFrameworkInitializationCompleted();
    }
}
