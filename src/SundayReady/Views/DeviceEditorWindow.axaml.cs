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

        // The live preview re-renders on any edit. Cheap enough to do bluntly: the preview is
        // a few dozen records, and subtlety here is how previews drift from reality.
        DataContextChanged += (_, _) => Hook();
        Hook();
    }

    private void Hook()
    {
        if (DataContext is not MapDeviceEditorViewModel editor)
        {
            return;
        }

        editor.PropertyChanged += (_, e) =>
        {
            // Never refresh in response to the refresh itself, or this recurses forever.
            if (e.PropertyName is not null && !e.PropertyName.StartsWith("Preview", StringComparison.Ordinal))
            {
                editor.RefreshPreview();
            }
        };
        editor.Ports.CollectionChanged += (_, _) => editor.RefreshPreview();
    }

    private void OnLibraryPick(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MapDeviceEditorViewModel editor
            && sender is ListBox { SelectedItem: string name })
        {
            editor.LoadFromLibrary(name);
        }
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
