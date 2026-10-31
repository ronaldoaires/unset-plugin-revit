namespace UnSetBim.Modules.Families.ViewModels;

/// <summary>Entry of the sidebar: "Todas as famílias", a category, or a subcategory of the selected category.</summary>
public sealed class NavItem
{
    public required string Label { get; init; }

    public string? CategorySlug { get; init; }

    public string? SubcategorySlug { get; init; }

    public int? Count { get; init; }

    public bool IsSubcategory => SubcategorySlug is not null;

    public string CountText => Count is > 0 ? Count.Value.ToString() : string.Empty;

    public static NavItem All(int total) => new() { Label = "Todas as famílias", Count = total };
}

/// <summary>Option of a filter ComboBox.</summary>
public sealed class FilterOption<T>
{
    public FilterOption(string label, T value)
    {
        Label = label;
        Value = value;
    }

    public string Label { get; }

    public T Value { get; }

    public override string ToString() => Label;
}
