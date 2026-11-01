using System.Globalization;
using UnSetBim.Core.Api.Models;
using UnSetBim.UI.Mvvm;

namespace UnSetBim.Modules.Families.ViewModels;

public enum PriceKind
{
    Free,
    Paid,
    SubscribersOnly,
}

/// <summary>Main button of a card.</summary>
public enum CardAction
{
    Download,
    Buy,
    Subscribe,
}

/// <summary>One card of the catalog grid.</summary>
public sealed class FamilyCardViewModel : ObservableObject
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private bool _isFavorite;
    private bool _isSelected;
    private bool _isBusy;
    private bool _isUnlocked;
    private readonly bool _hasAccess;

    /// <param name="hasAccess">The user has an active subscription (paid families count in the quota).</param>
    public FamilyCardViewModel(FamilySummary family, int revitYear, bool hasAccess)
    {
        Model = family;
        _isFavorite = family.IsFavorite;
        _hasAccess = hasAccess;
        IsCompatible = family.RevitVersion <= 0 || family.RevitVersion <= revitYear;
    }

    public FamilySummary Model { get; }

    public string Slug => Model.Slug;

    public string Name => Model.Name;

    public string? ThumbnailUrl => Model.ThumbnailUrl;

    public string CategoryText => Model.Subcategory is { } sub
        ? $"{Model.Category?.Name} › {sub.Name}"
        : Model.Category?.Name ?? string.Empty;

    public string? BrandName => Model.Brand?.Name;

    public string RevitVersionText => Model.RevitVersion > 0 ? $"Revit {Model.RevitVersion}+" : "Revit";

    public bool IsParametric => Model.IsParametric;

    /// <summary>False when the family needs a newer Revit than the running one (only possible in favorites/history).</summary>
    public bool IsCompatible { get; }

    public PriceKind PriceKind => Model.IsFree
        ? PriceKind.Free
        : Model.Price is > 0 ? PriceKind.Paid : PriceKind.SubscribersOnly;

    /// <summary>Short badge text.</summary>
    public string BadgeText => PriceKind switch
    {
        PriceKind.Free => "GRÁTIS",
        PriceKind.Paid => Model.FormattedPrice.ToUpper(PtBr),
        _ => "ASSINANTES",
    };

    public string PriceText => Model.FormattedPrice;

    /// <summary>Extra line on the "Meus downloads" tab.</summary>
    public string? HistoryText
    {
        get
        {
            if (Model.LastDownloadedAt is not { } last)
                return null;

            var times = Model.DownloadsCount is > 1 ? $"{Model.DownloadsCount} vezes · " : string.Empty;
            return $"{times}último em {last.ToLocalTime().ToString("dd/MM/yyyy", PtBr)}";
        }
    }

    public bool IsFavorite
    {
        get => _isFavorite;
        set => SetProperty(ref _isFavorite, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    // ── Card button ──────────────────────────────────────

    /// <summary>
    /// Best guess from the list data; the server decides on click (detail → download.allowed).
    /// Families already downloaded (history) or confirmed allowed are treated as downloadable.
    /// </summary>
    public CardAction Action
    {
        get
        {
            if (Model.IsFree || _hasAccess || _isUnlocked || Model.LastDownloadedAt is not null)
                return CardAction.Download;

            return PriceKind == PriceKind.Paid ? CardAction.Buy : CardAction.Subscribe;
        }
    }

    public string ActionText => IsBusy
        ? (Action == CardAction.Download ? "Baixando…" : "Abrindo…")
        : Action switch
        {
            CardAction.Download => "↓  Baixar",
            CardAction.Buy => "Comprar  ↗",
            _ => "Assinar  ↗",
        };

    public bool CanRunAction => IsCompatible && !IsBusy;

    /// <summary>The card button is loading the detail or downloading.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(ActionText));
                OnPropertyChanged(nameof(CanRunAction));
            }
        }
    }

    /// <summary>Set when the server confirmed the download is allowed (e.g. a family bought on the site).</summary>
    public bool IsUnlocked
    {
        get => _isUnlocked;
        set
        {
            if (SetProperty(ref _isUnlocked, value))
            {
                OnPropertyChanged(nameof(Action));
                OnPropertyChanged(nameof(ActionText));
            }
        }
    }
}
