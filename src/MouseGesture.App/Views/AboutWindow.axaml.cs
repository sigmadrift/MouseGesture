using System.Reflection;
using Avalonia.Controls;
using MouseGesture.App.Services;
using MouseGesture.Core.Persistence;

namespace MouseGesture.App.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        var version = Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
            ?? "1.0.0";

        // Trim trailing +commitHash if present.
        var plus = version.IndexOf('+');
        if (plus > 0)
            version = version[..plus];

        VersionText.Text = $"버전 {version}";
        ConfigPathText.Text = BindingStore.DefaultPath();
        LogPathText.Text = Logger.LogDirectory ?? "(없음)";
    }

    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();
}
