using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SundayReady.ViewModels;

namespace SundayReady.Views;

public partial class FirstRunWindow : Window
{
    public FirstRunWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is FirstRunViewModel model)
        {
            // Started before the close, so the station's overlay is already armed when this
            // window goes and the first step is on screen immediately rather than after a blink.
            model.TourRequested += (_, _) => TourHost.Start();
            model.Finished += (_, _) => Close();
        }
    }
}
