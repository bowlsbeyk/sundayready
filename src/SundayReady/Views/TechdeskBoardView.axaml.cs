using Avalonia.Controls;
using Avalonia.Interactivity;
using SundayReady.ViewModels;

namespace SundayReady.Views;

public partial class TechdeskBoardView : UserControl
{
    public TechdeskBoardView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// A PC running in techdesk mode has no station screen to go back to, so this is its only
    /// route to Settings — including the toggle that takes it out of techdesk mode again.
    /// </summary>

    private void OnMapClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TechdeskViewModel techdesk || techdesk.CreateMapWorkspace() is not { } workspace)
        {
            return;
        }

        var window = new MapWindow { DataContext = workspace };

        if (TopLevel.GetTopLevel(this) is Window owner)
        {
            window.Show(owner);
        }
        else
        {
            window.Show();
        }

        workspace.Start();
    }

    private void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TechdeskViewModel techdesk || techdesk.CreateSettings() is not { } settings)
        {
            return;
        }

        var window = new SettingsWindow { DataContext = settings };

        if (TopLevel.GetTopLevel(this) is Window owner)
        {
            window.Show(owner);
        }
        else
        {
            window.Show();
        }
    }
}
