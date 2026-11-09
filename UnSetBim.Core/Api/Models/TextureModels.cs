namespace UnSetBim.Core.Api.Models;

// DTOs of the texture endpoints (docs/api.md, section 13).

public sealed class TextureCategory
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public int TexturesCount { get; set; }
}

/// <summary>Material kinds sent in <c>material_type</c>. Only <see cref="Pbr"/> ships image maps.</summary>
public static class TextureMaterialType
{
    public const string Pbr = "pbr";
    public const string Glass = "glass";
    public const string Metal = "metal";
    public const string Water = "water";
    public const string Mirror = "mirror";
    public const string Emissive = "emissive";
}

/// <summary>Map types inside a package (file names are "{type}.png" or "{type}.jpg").</summary>
public static class TextureMapType
{
    public const string Diffuse = "diffuse";
    public const string Normal = "normal";
    public const string Roughness = "roughness";
    public const string Ao = "ao";
    public const string Displacement = "displacement";
    public const string Alpha = "alpha";
    public const string Metallic = "metallic";
}

/// <summary>Resolutions from lowest to highest.</summary>
public static class TextureResolutions
{
    public const string Default = "2k";

    public static IReadOnlyList<string> All { get; } = new[] { "1k", "2k", "4k", "8k" };

    public static int Rank(string? resolution) => resolution is null ? -1 : All.ToList().IndexOf(resolution);
}

public class TextureSummary
{
    public int Id { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public TaxonRef? Category { get; set; }
    public string MaterialType { get; set; } = TextureMaterialType.Pbr;
    public string MaterialTypeLabel { get; set; } = string.Empty;

    /// <summary>Real-world size of one repetition of the image, the same for every resolution.</summary>
    public int TileWidthMm { get; set; }

    public int TileHeightMm { get; set; }
    public bool IsSeamless { get; set; }
    public bool IsFree { get; set; }
    public bool IsFeatured { get; set; }

    /// <summary>"#rrggbb" for the material's shaded-view color, or null.</summary>
    public string? ShadingColor { get; set; }

    public string? ThumbnailUrl { get; set; }

    /// <summary>Goes up every time the maps change: cached packages are keyed by it.</summary>
    public int Version { get; set; }
}

public sealed class TextureDetail : TextureSummary
{
    public string? Description { get; set; }
    public string? PreviewUrl { get; set; }

    /// <summary>"#rrggbb". Main color of non-image materials, optional tint for pbr.</summary>
    public string? BaseColor { get; set; }

    public int DownloadCount { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>False for non-image materials: they are built from a Revit library asset and BaseColor.</summary>
    public bool NeedsDownload { get; set; }

    /// <summary>Available resolutions, lowest first.</summary>
    public List<TextureResolutionInfo> Resolutions { get; set; } = new();

    /// <summary>What the download serves without asking: the highest available up to 2k.</summary>
    public string? DefaultResolution { get; set; }

    public TextureDownloadDecision Download { get; set; } = new();

    /// <summary>
    /// The resolution the server will serve for <paramref name="requested"/>, using the same rule
    /// (requested if available, else the highest below it, else the lowest). Lets the client find a
    /// cached package without calling the download endpoint.
    /// </summary>
    public string? PickResolution(string? requested)
    {
        if (Resolutions.Count == 0)
            return null;

        var target = TextureResolutions.Rank(requested ?? TextureResolutions.Default);
        var atOrBelow = Resolutions.Where(r => TextureResolutions.Rank(r.Resolution) <= target).ToList();

        return atOrBelow.Count > 0 ? atOrBelow[^1].Resolution : Resolutions[0].Resolution;
    }
}

public sealed class TextureResolutionInfo
{
    public string Resolution { get; set; } = string.Empty;
    public int? WidthPx { get; set; }
    public int? HeightPx { get; set; }

    /// <summary>Map types in this package.</summary>
    public List<string> Maps { get; set; } = new();

    public long SizeBytes { get; set; }
}

/// <summary>Server decision for this user, right now. The download endpoint enforces it again.</summary>
public sealed class TextureDownloadDecision
{
    public string Url { get; set; } = string.Empty;
    public bool Allowed { get; set; }

    /// <summary>Portuguese reason when not allowed.</summary>
    public string? Message { get; set; }

    /// <summary>subscription_required or null.</summary>
    public string? Code { get; set; }
}

/// <summary>texture.json inside a package: everything needed to build the material offline.</summary>
public sealed class TextureManifest
{
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Version { get; set; }
    public string Resolution { get; set; } = string.Empty;
    public string MaterialType { get; set; } = TextureMaterialType.Pbr;
    public int TileWidthMm { get; set; }
    public int TileHeightMm { get; set; }
    public bool IsSeamless { get; set; }
    public string? ShadingColor { get; set; }
    public string? BaseColor { get; set; }

    /// <summary>Map type => file name inside the package folder.</summary>
    public Dictionary<string, string> Maps { get; set; } = new();
}

/// <summary>Sort keys accepted by GET /textures.</summary>
public static class TextureSort
{
    public const string Recent = "recent";
    public const string Downloads = "downloads";
    public const string Name = "name";
}

/// <summary>Filters of GET /textures. Null values are not sent.</summary>
public sealed class TextureQuery
{
    public string? Search { get; set; }
    public string? Category { get; set; }
    public string? MaterialType { get; set; }

    /// <summary>true = only free, false = only subscriber textures, null = both.</summary>
    public bool? Free { get; set; }

    public bool Featured { get; set; }
    public string Sort { get; set; } = TextureSort.Recent;
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
        Add("material_type", MaterialType);
        if (Free.HasValue)
            Add("free", Free.Value ? "1" : "0");
        if (Featured)
            Add("featured", "1");
        Add("sort", Sort);
        Add("per_page", Math.Clamp(PerPage, 1, 50).ToString());
        Add("page", Math.Max(Page, 1).ToString());

        return parts.Count == 0 ? string.Empty : "?" + string.Join("&", parts);
    }
}
