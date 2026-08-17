using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using SundayReady.ViewModels;

namespace SundayReady.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is SettingsViewModel settings)
        {
            settings.PropertyChanged += OnSettingsPropertyChanged;
            settings.RestartRequested += OnRestartRequested;
            settings.WalkthroughRequested += OnWalkthroughRequested;
        }
    }

    private void OnWalkthroughRequested(object? sender, EventArgs e)
    {
        if (DataContext is not SettingsViewModel settings)
        {
            return;
        }

        var window = new FirstRunWindow { DataContext = settings.CreateWalkthrough() };

        // Re-read this screen afterwards, so a station name the walkthrough changed does not sit
        // here as a stale value waiting to be saved back over itself.
        window.Closed += (_, _) => settings.ReloadCommand.Execute(null);
        window.Show(this);
    }

    /// <summary>
    /// Closing the app <em>is</em> the update: the helper process is already waiting for this one
    /// to exit before it swaps the build in and starts it again. A plain Shutdown, so the station
    /// view still gets its ShutdownRequested handler and writes the day's state out first.
    /// </summary>
    private void OnRestartRequested(object? sender, EventArgs e)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
        else
        {
            Close();
        }
    }

    /// <summary>
    /// Every section is on one scrolling page, the way the design draws it; the nav is an
    /// index into it rather than a set of tabs. Selecting a nav item scrolls its section up.
    /// </summary>
    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SettingsViewModel.SelectedPage)
            || sender is not SettingsViewModel { SelectedPage: { } page })
        {
            return;
        }

        if (this.FindControl<Control>($"Section_{page.Key}") is { } section)
        {
            section.BringIntoView();
        }
    }
}
