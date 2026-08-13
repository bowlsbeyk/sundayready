using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SundayReady.Views;

public partial class OverrideView : UserControl
{
    public OverrideView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
