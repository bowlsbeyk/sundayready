using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SundayReady.Views;

public partial class FailedVerifyView : UserControl
{
    public FailedVerifyView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
