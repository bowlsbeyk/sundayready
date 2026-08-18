using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using SundayReady.ViewModels;

namespace SundayReady.Views;

public partial class StationView : UserControl
{
    public StationView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// Manual and action items toggle on a click anywhere in the row. Buttons inside the row
    /// mark the event handled, so Launch and the failure actions do not also tick the item.
    /// </summary>
    private void OnRowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: ChecklistItemViewModel item }
            && item.ToggleCommand.CanExecute(null))
        {
            item.ToggleCommand.Execute(null);
        }
    }

    private void OnEditClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is StationViewModel station)
        {
            // Saving in here writes the file; the station is watching the folder, so the
            // checklist behind this window updates as soon as the save lands.
            Show(new ChecklistEditorWindow { DataContext = station.CreateEditor() });
        }
    }

    private void OnMapClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not StationViewModel station)
        {
            return;
        }

        var workspace = station.CreateMapWorkspace();
        var window = new MapWindow { DataContext = workspace };

        // The window disposes the workspace when it closes; Start is here because the map
        // polls, and polling belongs to being on screen.
        Show(window);
        workspace.Start();
    }

    private void OnTechdeskClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not StationViewModel station)
        {
            return;
        }

        var techdesk = station.CreateTechdesk();
        var window = new TechdeskWindow { DataContext = techdesk };

        // Owns its own polling, so it has to be stopped when the window goes.
        window.Closed += (_, _) => techdesk.Dispose();

        Show(window);
        techdesk.Start();
    }

    private void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is StationViewModel station)
        {
            Show(new SettingsWindow { DataContext = station.CreateSettings() });
        }
    }

    private void OnLogClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is StationViewModel station)
        {
            // Built fresh each time so it reflects the log as it stands right now.
            Show(new CompletionLogWindow { DataContext = station.CreateLog() });
        }
    }

    private void Show(Window window)
    {
        if (TopLevel.GetTopLevel(this) is Window owner)
        {
            window.Show(owner);
        }
        else
        {
            window.Show();
        }
    }

    private void OnHelpClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var window = new HelpWindow { DataContext = new ViewModels.HelpViewModel() };

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
