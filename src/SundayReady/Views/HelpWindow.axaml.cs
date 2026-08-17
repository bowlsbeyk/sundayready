using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SundayReady.Views;

public partial class HelpWindow : Window
{
    public HelpWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// Starts the guided tour and gets out of its way. This window is not one of the surfaces the
    /// tour points at, and leaving it open would sit on top of the station it is describing.
    /// </summary>
    private void OnTourClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        TourHost.Start();
        Close();
    }
}
