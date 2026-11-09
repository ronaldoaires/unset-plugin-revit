using System.Globalization;
using UnSetBim.Core.Api.Models;
using UnSetBim.UI.Mvvm;

namespace UnSetBim.Modules.Materials.ViewModels;

/// <summary>One card of the textures grid.</summary>
public sealed class TextureCardViewModel : ObservableObject
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private bool _isSelected;
    private bool _hasAccess;

    public TextureCardViewModel(TextureSummary texture, bool hasAccess)
    {
        Model = texture;
        _hasAccess = hasAccess;
    }

    public TextureSummary Model { get; }

    public string Slug => Model.Slug;

    public string Name => Model.Name;

    public string? ThumbnailUrl => Model.ThumbnailUrl;

    public bool IsFree => Model.IsFree;

    public string BadgeText => Model.IsFree ? "GRÁTIS" : "ASSINANTES";

    public string TypeText => Model.MaterialTypeLabel;

    public string CategoryText => Model.Category?.Name ?? string.Empty;

    /// <summary>Real-world size of one repetition, e.g. "60 × 60 cm" or "1,2 × 0,8 m".</summary>
    public string SizeText => FormatSize(Model.TileWidthMm, Model.TileHeightMm);

    /// <summary>Subscriber texture and the user has no active plan.</summary>
    public bool IsLocked => !Model.IsFree && !_hasAccess;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public void SetAccess(bool hasAccess)
    {
        _hasAccess = hasAccess;
        OnPropertyChanged(nameof(IsLocked));
    }

    public static string FormatSize(int widthMm, int heightMm)
    {
        if (widthMm <= 0 || heightMm <= 0)
            return string.Empty;

        if (widthMm >= 1000 && heightMm >= 1000)
            return $"{(widthMm / 1000d).ToString("0.##", PtBr)} × {(heightMm / 1000d).ToString("0.##", PtBr)} m";

        if (widthMm % 10 == 0 && heightMm % 10 == 0)
            return $"{widthMm / 10} × {heightMm / 10} cm";

        return $"{widthMm} × {heightMm} mm";
    }
}

/// <summary>Option of a filter menu. <see cref="IsSelected"/> drives the check mark.</summary>
public sealed class FilterOption<T> : ObservableObject
{
    private bool _isSelected;

    public FilterOption(string label, T value)
    {
        Label = label;
        Value = value;
    }

    public string Label { get; }

    public T Value { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public override string ToString() => Label;
}

/// <summary>Category entry: "Todas as categorias" or a category.</summary>
public sealed class TextureNavItem : ObservableObject
{
    private bool _isSelected;

    public required string Label { get; init; }

    public string? CategorySlug { get; init; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    // Shown by combo boxes and menus that display the item itself.
    public override string ToString() => Label;
}
