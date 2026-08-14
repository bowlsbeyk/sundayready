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
}
