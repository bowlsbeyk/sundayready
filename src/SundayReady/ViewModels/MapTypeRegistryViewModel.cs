using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SundayReady.Models;
using SundayReady.Services;

namespace SundayReady.ViewModels;

/// <summary>One card in the registry: a type, how it draws, and how many runs use it.</summary>
public sealed class MapTypeCardViewModel
{
    public MapTypeCardViewModel(MapConnectionType type, int usage)
    {
        Type = type;
        Usage = usage;
    }

    public MapConnectionType Type { get; }

    public int Usage { get; }

    public string Name => Type.Name;

    public bool IsCustom => !Type.BuiltIn;

    public string Provenance => Type.CreatedBy is { Length: > 0 } by
        ? $"ADDED BY {by.ToUpperInvariant()}{(Type.CreatedAt is { Length: > 0 } at ? $" · {at}" : string.Empty)}"
        : string.Empty;

    public bool HasProvenance => Provenance.Length > 0;

    /// <summary>The mono spec line: <c>SOLID · 2.5px · FLOW 3.6s · WARNS OVER 50 ft</c>.</summary>
    public string Spec
    {
        get
        {
            var parts = new List<string>
            {
                Type.LineStyle.ToUpperInvariant(),
                $"{Type.StrokeWidth:0.#}px",
                Type.Wireless ? "WIRELESS" : $"FLOW {Type.FlowSeconds:0.#}s",
            };

            if (Type.WarnOverFt is { } warn)
            {
                parts.Add($"WARNS OVER {warn} ft");
            }

            return string.Join(" · ", parts);
        }
    }

    /// <summary>A constraint worth noticing gets the wait tint, per the handoff.</summary>
    public bool HasWarning => Type.WarnOverFt is not null;
}

/// <summary>
/// The connection-type registry and its editor — the handoff's 2c.
/// <para>
/// Custom colours come from curated swatches, never a free picker. The built-in palette keeps
/// related types on shared hues so the legend is learnable, and a free picker is how a church
/// ends up with three unrelated blues meaning three unrelated things.
/// </para>
/// </summary>
public sealed partial class MapTypeRegistryViewModel : ObservableObject
{
    /// <summary>Hues the built-in palette does not use.</summary>
    public static readonly string[] Swatches = { "#3fcf8a", "#ee6d9d", "#b8d94c", "#cfa87a", "#dfe6ee" };

    private readonly SystemMapStore _store;
    private readonly MapWorkspaceViewModel _workspace;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _selectedColour = Swatches[0];

    [ObservableProperty]
    private string _lineStyle = MapLineStyles.Solid;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FlowLabel))]
    private double _flowSeconds = 4.0;

    [ObservableProperty]
    private bool _wireless;

    /// <summary>New runs of this type start duplex — Dante, AES50, network trunks.</summary>
    [ObservableProperty]
    private bool _duplex;

    /// <summary>Occasional traffic: drawn as a heartbeat, not a stream.</summary>
    [ObservableProperty]
    private bool _pulse;

    [ObservableProperty]
    private string _warnOverFt = string.Empty;

    [ObservableProperty]
    private string _status = string.Empty;

    /// <summary>The card selected for deletion, and the type its runs would move to.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDelete), nameof(DeleteExplanation))]
    private MapTypeCardViewModel? _selectedCard;

    [ObservableProperty]
    private string? _replacementTypeId;

    public MapTypeRegistryViewModel(SystemMapStore store, MapWorkspaceViewModel workspace)
    {
        _store = store;
        _workspace = workspace;
        Rebuild();
    }

    public ObservableCollection<MapTypeCardViewModel> Cards { get; } = new();

    public ObservableCollection<string> ReplacementChoices { get; } = new();

    public IReadOnlyList<string> SwatchChoices => Swatches;

    public string CountsLine
    {
        get
        {
            var custom = Cards.Count(c => c.IsCustom);
            return $"{Cards.Count - custom} BUILT-IN · {custom} CUSTOM · SHARED BY ALL MAPS";
        }
    }

    public string FlowLabel => $"{FlowSeconds:0.0} s";

    /// <summary>A live preview of exactly what the map would draw.</summary>
    public MapConnectionType Preview => new()
    {
        Id = "preview",
        Name = Name,
        Colour = SelectedColour,
        LineStyle = Wireless ? MapLineStyles.Dashed : LineStyle,
        StrokeWidth = Wireless ? 2 : 2.5,
        FlowSeconds = FlowSeconds,
        Wireless = Wireless,
        Pulse = Pulse,
    };

    public bool CanDelete => SelectedCard is { IsCustom: true };

    public string DeleteExplanation => SelectedCard is not { IsCustom: true } card
        ? string.Empty
        : card.Usage == 0
            ? "Not used by any run — deleting it is safe."
            : $"In use by {card.Usage} run{(card.Usage == 1 ? string.Empty : "s")}. "
              + "Pick the type they should become — silently orphaning them is not an option.";

    [RelayCommand]
    private void AddType()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            Status = "Give the type a name first.";
            return;
        }

        var id = SystemMapStore.FileNameFor(Name).Replace(".json", string.Empty);
        var all = _store.LoadTypes();

        if (all.Any(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase) && t.BuiltIn))
        {
            Status = $"“{Name}” collides with the built-in {id} type. Pick another name.";
            return;
        }

        var custom = all.Where(t => !t.BuiltIn)
            .Where(t => !string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase))
            .ToList();

        custom.Add(new MapConnectionType
        {
            Id = id,
            Name = Name.Trim(),
            Colour = SelectedColour,
            LineStyle = Wireless ? MapLineStyles.Dashed : LineStyle,
            StrokeWidth = Wireless ? 2 : 2.5,
            FlowSeconds = Math.Clamp(FlowSeconds, 2.0, 8.0),
            Wireless = Wireless,
            DefaultBidirectional = Duplex,
            Pulse = Pulse,
            WarnOverFt = int.TryParse(WarnOverFt, out var warn) && warn > 0 ? warn : null,
            CreatedBy = Environment.UserName,
            CreatedAt = DateTime.Now.ToString("MMM yyyy").ToUpperInvariant(),
        });

        try
        {
            _store.SaveTypes(custom);
        }
        catch (Exception ex)
        {
            Status = $"Could not save: {ex.Message}";
            return;
        }

        _workspace.Load();
        Rebuild();
        Status = $"Added. It is in every map's editor and legend now.";
        Name = string.Empty;
    }

    /// <summary>
    /// Deletes a custom type. When runs use it they are reassigned first — the registry never
    /// leaves a wire pointing at a word that no longer exists.
    /// </summary>
    [RelayCommand]
    private void DeleteSelected()
    {
        if (SelectedCard is not { IsCustom: true } card)
        {
            return;
        }

        if (card.Usage > 0)
        {
            if (string.IsNullOrWhiteSpace(ReplacementTypeId))
            {
                Status = "Pick the type its runs should become first.";
                return;
            }

            foreach (var file in _store.ListFiles())
            {
                try
                {
                    var map = _store.Load(file);
                    var touched = false;

                    foreach (var connection in map.Connections.Where(c =>
                                 string.Equals(c.Type, card.Type.Id, StringComparison.OrdinalIgnoreCase)))
                    {
                        connection.Type = ReplacementTypeId;
                        touched = true;
                    }

                    if (touched)
                    {
                        _store.Save(map, file);
                    }
                }
                catch (Exception)
                {
                    // A map that will not load keeps its old id; it reads as Unknown type there,
                    // which is visible rather than silent.
                }
            }
        }

        var remaining = _store.LoadTypes()
            .Where(t => !t.BuiltIn && !string.Equals(t.Id, card.Type.Id, StringComparison.OrdinalIgnoreCase));

        try
        {
            _store.SaveTypes(remaining);
        }
        catch (Exception ex)
        {
            Status = $"Could not save: {ex.Message}";
            return;
        }

        _workspace.Load();
        Rebuild();
        SelectedCard = null;
        Status = "Deleted.";
    }

    public void Select(MapTypeCardViewModel card)
    {
        SelectedCard = card;
        ReplacementTypeId = null;
    }

    private void Rebuild()
    {
        var usage = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in _store.ListFiles())
        {
            try
            {
                foreach (var connection in _store.Load(file).Connections)
                {
                    var id = connection.Type ?? "unknown";
                    usage[id] = usage.GetValueOrDefault(id) + 1;
                }
            }
            catch (Exception)
            {
                // Uncounted is fine; the count is information, not authority.
            }
        }

        Cards.Clear();
        ReplacementChoices.Clear();

        foreach (var type in _store.LoadTypes())
        {
            Cards.Add(new MapTypeCardViewModel(type, usage.GetValueOrDefault(type.Id)));
            ReplacementChoices.Add(type.Id);
        }

        OnPropertyChanged(nameof(CountsLine));
    }

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(Preview));

    partial void OnSelectedColourChanged(string value) => OnPropertyChanged(nameof(Preview));

    partial void OnLineStyleChanged(string value) => OnPropertyChanged(nameof(Preview));

    partial void OnFlowSecondsChanged(double value) => OnPropertyChanged(nameof(Preview));

    partial void OnWirelessChanged(bool value) => OnPropertyChanged(nameof(Preview));

    partial void OnPulseChanged(bool value) => OnPropertyChanged(nameof(Preview));
}
