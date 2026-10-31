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

/// <summary>One card of the catalog grid.</summary>
public sealed class FamilyCardViewModel : ObservableObject
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private bool _isFavorite;
    private bool _isSelected;

    public FamilyCardViewModel(FamilySummary family, int revitYear)
    {
        Model = family;
        _isFavorite = family.IsFavorite;
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
}
