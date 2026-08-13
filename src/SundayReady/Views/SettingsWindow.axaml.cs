using System.ComponentModel;
using Avalonia.Controls;
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
