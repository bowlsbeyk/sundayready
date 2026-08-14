using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SundayReady.Models;
using SundayReady.Services;
using SundayReady.ViewModels;
using SundayReady.Views;

namespace SundayReady;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            AppPaths.EnsureDataDirectories();

            var checklists = new ChecklistLoader();
            var stationLoader = new StationConfigLoader(checklists);
            var config = stationLoader.Load();

            if (config.Techdesk)
            {
                // Same binary, same station.json — this PC just aggregates instead of
                // checking. It loads no checklists of its own and publishes no heartbeat.
                var techdesk = new TechdeskViewModel(
                    config, new ProcessLauncher(), checklists, stationLoader, VerifierRegistry.CreateDefault());
                desktop.MainWindow = new TechdeskWindow { DataContext = techdesk };
                desktop.ShutdownRequested += (_, _) => techdesk.Dispose();
                techdesk.Start();
            }
            else
            {
                var viewModel = BuildStation(checklists, stationLoader, config);
                desktop.MainWindow = new MainWindow { DataContext = viewModel };
                desktop.ShutdownRequested += (_, _) => viewModel.Dispose();
                viewModel.Start();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static StationViewModel BuildStation(
        ChecklistLoader checklists,
        StationConfigLoader stationLoader,
        Models.StationConfig config)
    {
        var definitions = new List<ChecklistDefinition>();
        var errors = new List<string>();

        foreach (var file in config.Checklists)
        {
            try
            {
                definitions.Add(checklists.Load(file));
            }
            catch (Exception ex)
            {
                // One bad file must not cost the operator the other tabs.
                errors.Add($"{file}: {ex.Message}");
            }
        }

        // A station with no checklists at all is a fresh install, not a failure. The station
        // view shows a getting-started panel for that; only genuine load errors go in the
        // red banner, so nobody's first impression of the app is something in red.

        if (config.Updates.Enabled)
        {
            _ = CheckForUpdatesAsync(config);
        }

        return new StationViewModel(
            config,
            definitions,
            VerifierRegistry.CreateDefault(),
            new ProcessLauncher(),
            new DailyStateStore(),
            new CompletionLogger(),
            checklists,
            stationLoader,
            errors.Count == 0 ? null : string.Join("  ·  ", errors));
    }

    /// <summary>
    /// Looks for a newer release and downloads it in the background. Nothing is swapped in
    /// now — <see cref="UpdateInstaller"/> does that at the next launch, so an operator is
    /// never interrupted mid-service. Failure is silent by design; Settings shows the detail.
    /// </summary>
    private static async Task CheckForUpdatesAsync(Models.StationConfig config)
    {
        try
        {
            using var updates = new UpdateService(config.Updates.Repository);
            if (await updates.CheckAsync(CancellationToken.None) is { } available)
            {
                await updates.StageAsync(available, CancellationToken.None);
            }
        }
        catch (Exception)
        {
            // Booth PCs boot before the network settles, and a church firewall may block this
            // entirely. Neither is worth a dialog on a Sunday morning.
        }
    }
}
