using Avalonia.Controls;
using SundayReady.Services;

namespace SundayReady.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Registered whether or not a tour is running: the host only shows the overlay when one
        // is, and the station is where a tour both starts and ends.
        TourHost.Register(this, TourSurface.Station, TourLayer);
    }
}
