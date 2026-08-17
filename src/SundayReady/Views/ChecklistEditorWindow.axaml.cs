using Avalonia.Controls;

namespace SundayReady.Views;

public partial class ChecklistEditorWindow : Window
{
    public ChecklistEditorWindow()
    {
        // The generated InitializeComponent, deliberately. A hand-written one that only calls
        // AvaloniaXamlLoader.Load leaves every x:Name field null — which is how TourLayer arrived
        // here as null and took the app down the first time the tour opened this window.
        InitializeComponent();

        // A tour is usually already running by the time this window exists — opening it is one
        // of the steps — so registering is also how the spotlight follows the person in here.
        TourHost.Register(this, Services.TourSurface.Editor, TourLayer);
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
