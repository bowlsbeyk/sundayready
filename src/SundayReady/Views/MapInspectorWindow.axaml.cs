using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SundayReady.Views;

public partial class MapInspectorWindow : Window
{
    public MapInspectorWindow()
    {
        // The generated InitializeComponent — a hand-written one leaves x:Name fields null.
        InitializeComponent();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
