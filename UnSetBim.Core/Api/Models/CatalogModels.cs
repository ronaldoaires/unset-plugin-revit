namespace UnSetBim.Core.Api.Models;

// DTOs of the catalog endpoints (docs/api.md, sections 5 and 7).

/// <summary>{id, name, slug} reference used for category, subcategory and brand on a family.</summary>
public sealed class TaxonRef
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
}

public sealed class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Icon { get; set; }
    public int FamiliesCount { get; set; }
    public List<Subcategory> Subcategories { get; set; } = new();
}

public sealed class Subcategory
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Unique only inside its category.</summary>
    public string Slug { get; set; } = string.Empty;
}

public sealed class Brand
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Country { get; set; }
    public string? LogoUrl { get; set; }
}

/// <summary>
/// Family list item. Also used by /me/favorites (adds <see cref="FavoritedAt"/>)
/// and /me/downloads (adds <see cref="LastDownloadedAt"/> and <see cref="DownloadsCount"/>).
/// </summary>
public class FamilySummary
{
    public int Id { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public TaxonRef? Category { get; set; }
    public TaxonRef? Subcategory { get; set; }
    public TaxonRef? Brand { get; set; }

    /// <summary>Minimum Revit year the file opens in.</summary>
    public int RevitVersion { get; set; }

    public bool IsParametric { get; set; }
    public bool IsFree { get; set; }

    /// <summary>BRL. Null or 0 on a non-free family = subscribers only.</summary>
    public decimal? Price { get; set; }

    /// <summary>"Gratuito", "Exclusivo para assinantes" or "R$ 19,90".</summary>
    public string FormattedPrice { get; set; } = string.Empty;

    public string? ThumbnailUrl { get; set; }
    public bool IsFavorite { get; set; }

    // Present only on /me/favorites
    public DateTimeOffset? FavoritedAt { get; set; }

    // Present only on /me/downloads
    public DateTimeOffset? LastDownloadedAt { get; set; }
    public int? DownloadsCount { get; set; }
}

public sealed class FamilyDetail : FamilySummary
{
    public string? Description { get; set; }
    public List<string> Tags { get; set; } = new();
    public string? ImageUrl { get; set; }
    public int DownloadCount { get; set; }

    /// <summary>Invalidates the local .rfa cache when it changes.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>Null when the family has no file (download answers 404).</summary>
    public FamilyFileInfo? File { get; set; }

    public string? PageUrl { get; set; }
    public DownloadDecision Download { get; set; } = new();
}

public sealed class FamilyFileInfo
{
    public long SizeBytes { get; set; }
    public string FormattedSize { get; set; } = string.Empty;
}

/// <summary>Server decision for this user, right now. The download endpoint enforces it again.</summary>
public sealed class DownloadDecision
{
    public string Url { get; set; } = string.Empty;
    public bool Allowed { get; set; }

    /// <summary>Portuguese reason when not allowed.</summary>
    public string? Message { get; set; }

    /// <summary>access_required, quota_exceeded or null.</summary>
    public string? Code { get; set; }

    /// <summary>Site page to buy the family; only when not allowed and price &gt; 0.</summary>
    public string? PurchaseUrl { get; set; }
}

public sealed class FavoriteState
{
    public string Slug { get; set; } = string.Empty;
    public bool IsFavorite { get; set; }
}

/// <summary>Sort keys accepted by GET /families.</summary>
public static class FamilySort
{
    public const string Recent = "recent";
    public const string Downloads = "downloads";
    public const string Views = "views";
    public const string Name = "name";
}

/// <summary>Filters of GET /families. Null values are not sent.</summary>
public sealed class FamilyQuery
{
    public string? Search { get; set; }
    public string? Category { get; set; }

    /// <summary>Requires <see cref="Category"/>.</summary>
    public string? Subcategory { get; set; }

    public string? Brand { get; set; }

    /// <summary>true = only free, false = only paid, null = both.</summary>
    public bool? Free { get; set; }

    /// <summary>Always the running Revit year.</summary>
    public int? RevitVersion { get; set; }

    public string Sort { get; set; } = FamilySort.Recent;
    public int Page { get; set; } = 1;
    public int PerPage { get; set; } = 24;

    public string ToQueryString()
    {
        var parts = new List<string>();

        void Add(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                parts.Add($"{key}={Uri.EscapeDataString(value.Trim())}");
        }

        var search = Search?.Trim();
        if (search is { Length: > 100 })
            search = search[..100];

        Add("q", search);
        Add("category", Category);
        if (!string.IsNullOrWhiteSpace(Category))
            Add("subcategory", Subcategory);
        Add("brand", Brand);
        if (Free.HasValue)
            Add("free", Free.Value ? "1" : "0");
        if (RevitVersion.HasValue)
            Add("revit_version", RevitVersion.Value.ToString());
        Add("sort", Sort);
        Add("per_page", Math.Clamp(PerPage, 1, 50).ToString());
        Add("page", Math.Max(Page, 1).ToString());

        return parts.Count == 0 ? string.Empty : "?" + string.Join("&", parts);
    }
}
