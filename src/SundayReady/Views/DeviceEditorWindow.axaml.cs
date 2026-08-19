using Avalonia.Controls;
using Avalonia.Interactivity;
using SundayReady.ViewModels;

namespace SundayReady.Views;

/// <summary>
/// The handoff's 4d: one device, edited with room. Deliberately the same
/// <see cref="MapDeviceEditorViewModel"/> the rail binds — this window is square footage, not a
/// second editor, so nothing here can drift out of sync with the rail's behaviour.
/// </summary>
public partial class DeviceEditorWindow : Window
{
    public DeviceEditorWindow()
    {
        InitializeComponent();
    }

    /// <summary>The workspace's apply, injected by whoever opened the window.</summary>
    public Action? ApplyRequested { get; set; }

    private void OnApplyClick(object? sender, RoutedEventArgs e)
    {
        ApplyRequested?.Invoke();
        Close();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
