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
            model.Finished += (_, _) => Close();
        }
    }
}
