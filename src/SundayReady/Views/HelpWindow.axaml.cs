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
}
