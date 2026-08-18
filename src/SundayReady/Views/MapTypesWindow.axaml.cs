using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Interactivity;
using SundayReady.ViewModels;

namespace SundayReady.Views;

public partial class MapTypesWindow : Window
{
    public MapTypesWindow()
    {
        // The generated InitializeComponent — a hand-written one leaves x:Name fields null.
        InitializeComponent();
    }

    private MapTypeRegistryViewModel? Registry => DataContext as MapTypeRegistryViewModel;

    private void OnCardPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: MapTypeCardViewModel card })
        {
            Registry?.Select(card);

            // The picked ring is presentational; the classes live on the card's Border.
            if (sender is Border border)
            {
                foreach (var other in this.GetVisualDescendants().OfType<Border>()
                             .Where(b => b.Classes.Contains("typeCard")))
                {
                    other.Classes.Set("picked", ReferenceEquals(other, border));
                }
            }
        }
    }

    private void OnSwatchClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: string colour } && Registry is { } registry)
        {
            registry.SelectedColour = colour;
        }
    }

    private void OnStyleClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string style } && Registry is { } registry)
        {
            registry.LineStyle = style;
        }
    }
}
