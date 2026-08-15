using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SundayReady.Views;

public partial class ChecklistEditorWindow : Window
{
    public ChecklistEditorWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

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
