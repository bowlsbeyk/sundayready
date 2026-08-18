using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SundayReady.Models;
using SundayReady.Services;

namespace SundayReady.ViewModels;

/// <summary>One row of the inspector's CONNECTIONS table.</summary>
public sealed partial class MapInspectorRowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _bridgeStatus = string.Empty;

    public MapInspectorRowViewModel(MapConnectionViewModel connection, bool outgoing)
    {
        Connection = connection;
        Outgoing = outgoing;
    }

    public MapConnectionViewModel Connection { get; }

    public bool Outgoing { get; }

    public string Direction => Outgoing ? "OUT" : "IN";

    public string OtherEnd => Outgoing ? Connection.To.Label : Connection.From.Label;

    public string TypeName => Connection.Type.Name;

    public string TypeColour => Connection.Type.Colour;

    public string Run => Connection.LengthFt is { } ft ? $"{ft} ft"
        : Connection.Type.Wireless ? "RF" : "—";

    public string State => Connection.FlowState.ToUpperInvariant();

    public bool IsDown => Connection.IsDown;

    /// <summary>
    /// The seam between the map and the checklist: this run is documented but nothing checks it.
    /// Saying so is how the checklist grows to match reality.
    /// </summary>
    public bool IsUnverified => !Connection.HasVerify && !Connection.IsStandby;

    public bool HasBridgeStatus => BridgeStatus.Length > 0;
}

/// <summary>
/// One hop of the downstream trace: a device, and how the signal got to it.
/// </summary>
public sealed class MapTraceHopViewModel
{
    public MapTraceHopViewModel(MapDeviceViewModel device, MapConnectionViewModel? via)
    {
        Device = device;
        Via = via;
    }

    public MapDeviceViewModel Device { get; }

    /// <summary>The connection that carried the signal here. Null on the first hop.</summary>
    public MapConnectionViewModel? Via { get; }

    public string Label => Device.Label;

    public string Detail => Via is null
        ? Device.Detail ?? string.Empty
        : $"{Via.Type.Name.ToUpperInvariant()}{(Via.Label is { Length: > 0 } l ? $" · {l}" : string.Empty)}";

    public bool IsBroken => Device.ShowsFailure || Via is { IsDown: true };

    /// <summary>Downstream of a break: hollow and dashed, per the handoff — starved, not broken.</summary>
    public bool IsStarvedHop => !IsBroken && (Device.IsStarved || Via?.FlowState == "starved");

    public bool IsHealthy => !IsBroken && !IsStarvedHop
        && (Device.ShowsOk || Device.IsInferred || Device.IsReported);

    public string StateLine => IsBroken
        ? Device.ShowsFailure ? Device.StatusLabel : "LINK DOWN"
        : IsStarvedHop
            ? "NOTHING ARRIVING"
            : Device.HasVerify ? Device.StatusLabel : string.Empty;
}

/// <summary>
/// The device inspector — the handoff's 2d, carrying 2e's trace at the bottom.
/// <para>
/// The table's most important row is the one nothing verifies: the map knows about connections
/// the checklist does not cover, and offering "make this a checklist item" right there is how the
/// two layers grow toward each other instead of drifting apart.
/// </para>
/// </summary>
public sealed partial class MapInspectorViewModel : ObservableObject
{
    private readonly MapWorkspaceViewModel _workspace;
    private readonly SystemMapViewModel _map;
    private readonly ChecklistLoader _checklists;
    private readonly ChecklistWriter _writer;

    [ObservableProperty]
    private string? _bridgeTargetFile;

    [ObservableProperty]
    private string _status = string.Empty;

    public MapInspectorViewModel(
        MapWorkspaceViewModel workspace,
        SystemMapViewModel map,
        MapDeviceViewModel device,
        ChecklistLoader? checklists = null)
    {
        _workspace = workspace;
        _map = map;
        Device = device;
        _checklists = checklists ?? new ChecklistLoader();
        _writer = new ChecklistWriter(_checklists.Directory);

        foreach (var connection in map.Connections.Where(c => ReferenceEquals(c.To, device)))
        {
            Rows.Add(new MapInspectorRowViewModel(connection, outgoing: false));
        }

        foreach (var connection in map.Connections.Where(c => ReferenceEquals(c.From, device)))
        {
            Rows.Add(new MapInspectorRowViewModel(connection, outgoing: true));
        }

        foreach (var file in _checklists.ListFiles())
        {
            ChecklistFiles.Add(file);
        }

        BridgeTargetFile = ChecklistFiles.FirstOrDefault();

        BuildTrace();
    }

    public MapDeviceViewModel Device { get; }

    public ObservableCollection<MapInspectorRowViewModel> Rows { get; } = new();

    public ObservableCollection<string> ChecklistFiles { get; } = new();

    public ObservableCollection<MapTraceHopViewModel> Trace { get; } = new();

    public string Title => Device.Label;

    /// <summary>The pin glyph: 2-3 mono chars, per the handoff. From the kind.</summary>
    public string KindGlyph => Device.Kind.Length >= 2
        ? Device.Kind[..2].ToUpperInvariant()
        : Device.Kind.ToUpperInvariant();

    /// <summary>Mono meta line: detail, location, tier.</summary>
    public string Meta => string.Join(" · ", new[]
    {
        Device.Detail,
        Device.Location,
        Device.Tier.ToUpperInvariant(),
    }.Where(part => !string.IsNullOrWhiteSpace(part)));

    public bool HasRows => Rows.Count > 0;

    public bool HasUnverified => Rows.Any(r => r.IsUnverified);

    public bool CanBridge => ChecklistFiles.Count > 0;

    public bool HasTrace => Trace.Count > 1;

    [ObservableProperty]
    private string _traceVerdict = string.Empty;

    public bool HasTraceVerdict => TraceVerdict.Length > 0;

    public string TraceTitle
    {
        get
        {
            if (Trace.Count < 2)
            {
                return string.Empty;
            }

            var broken = Trace.Select((hop, index) => (hop, index)).FirstOrDefault(t => t.hop.IsBroken);
            return broken.hop is null
                ? $"{Device.Label} → {Trace[^1].Label} · {Trace.Count - 1} HOPS · ALL GOOD"
                : broken.index == 0
                    ? $"{Device.Label} → {Trace[^1].Label} · THE SOURCE IS DOWN"
                    : $"{Device.Label} → {Trace[^1].Label} · BREAKS AT HOP {broken.index}";
        }
    }

    /// <summary>
    /// Adds a manual item to a checklist: the un-checked run becomes somebody's job. Manual on
    /// purpose — the map documented the run without saying how to verify it, so a person confirms
    /// it until someone teaches the map a real check.
    /// </summary>
    [RelayCommand]
    private void MakeChecklistItem(MapInspectorRowViewModel row)
    {
        if (BridgeTargetFile is not { Length: > 0 } file)
        {
            Status = "No checklist file to add it to.";
            return;
        }

        var label = $"Confirm signal: {row.Connection.From.Label} → {row.Connection.To.Label}";

        try
        {
            var definition = _checklists.Load(file);

            if (definition.Items.Any(i => string.Equals(i.Label, label, StringComparison.OrdinalIgnoreCase)))
            {
                row.BridgeStatus = $"Already on {file}.";
                return;
            }

            definition.Items.Add(new ChecklistItem
            {
                Label = label,
                Type = ChecklistItemTypes.Manual,
                Section = "From the system map",
                CheckSteps = row.Connection.CheckSteps.Count > 0
                    ? new List<string>(row.Connection.CheckSteps)
                    : new List<string>
                    {
                        $"The map documents this {row.Connection.Type.Name} run but nothing checks it automatically.",
                        "If you know a URL or address that proves the signal, add a verifier to the connection on the map.",
                    },
            });

            _writer.Save(definition, file);
            row.BridgeStatus = $"Added to {file}. The station shows it after its next reload.";
        }
        catch (Exception ex)
        {
            row.BridgeStatus = $"Could not add it: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task CheckNowAsync()
    {
        if (!Device.HasVerify)
        {
            return;
        }

        try
        {
            await Device.PollAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        _workspace.RollUp();
        BuildTrace();
        OnPropertyChanged(nameof(TraceTitle));
    }

    /// <summary>
    /// The signal's journey onward from this device, one straight line — the handoff's 2e. At a
    /// fan-out it follows the path that reaches a break if one exists (the whole point of the
    /// screen is finding the break), otherwise the longest run.
    /// </summary>
    private void BuildTrace()
    {
        Trace.Clear();
        Trace.Add(new MapTraceHopViewModel(Device, null));

        var path = BestPath(Device, new HashSet<MapDeviceViewModel> { Device });
        foreach (var hop in path)
        {
            Trace.Add(hop);
        }

        var broken = Trace.FirstOrDefault(h => h.IsBroken);
        TraceVerdict = broken is null
            ? string.Empty
            : broken.Via is { IsDown: true }
                ? $"The break is between {broken.Via.From.Label} and {broken.Label} — everything after it "
                  + "is starved, not broken. Start at that run."
                : $"The break is at {broken.Label}. Everything after it is starved, not broken.";

        OnPropertyChanged(nameof(HasTrace));
        OnPropertyChanged(nameof(HasTraceVerdict));
        OnPropertyChanged(nameof(TraceTitle));
    }

    private List<MapTraceHopViewModel> BestPath(MapDeviceViewModel from, HashSet<MapDeviceViewModel> visited)
    {
        List<MapTraceHopViewModel> best = new();
        var bestHasBreak = false;

        foreach (var connection in _map.Connections.Where(c =>
                     ReferenceEquals(c.From, from) && !visited.Contains(c.To)))
        {
            visited.Add(connection.To);
            var rest = BestPath(connection.To, visited);
            visited.Remove(connection.To);

            var candidate = new List<MapTraceHopViewModel>
            {
                new(connection.To, connection),
            };
            candidate.AddRange(rest);

            var hasBreak = candidate.Any(h => h.IsBroken);

            // A path that reaches a break beats any that does not; among equals, longer wins.
            if ((hasBreak && !bestHasBreak)
                || (hasBreak == bestHasBreak && candidate.Count > best.Count))
            {
                best = candidate;
                bestHasBreak = hasBreak;
            }
        }

        return best;
    }
}
