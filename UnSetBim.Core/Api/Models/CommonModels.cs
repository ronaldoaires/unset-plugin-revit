namespace UnSetBim.Core.Api.Models;

/// <summary>{"data": ...} envelope used by single-resource endpoints.</summary>
public sealed class DataEnvelope<T>
{
    public T? Data { get; set; }
}

/// <summary>Standard Laravel paginated resource (docs/api.md 5.3).</summary>
public sealed class PagedResponse<T>
{
    public List<T> Data { get; set; } = new();
    public PageLinks Links { get; set; } = new();
    public PageMeta Meta { get; set; } = new();

    public bool HasNext => Meta.CurrentPage < Meta.LastPage;
    public bool HasPrevious => Meta.CurrentPage > 1;
}

public sealed class PageLinks
{
    public string? First { get; set; }
    public string? Last { get; set; }
    public string? Prev { get; set; }
    public string? Next { get; set; }
}

public sealed class PageMeta
{
    public int CurrentPage { get; set; } = 1;
    public int? From { get; set; }
    public int LastPage { get; set; } = 1;
    public int PerPage { get; set; }
    public int? To { get; set; }
    public int Total { get; set; }
}

/// <summary>Error body: always "message", sometimes "code" and "errors" (422).</summary>
public sealed class ApiErrorBody
{
    public string? Message { get; set; }
    public string? Code { get; set; }
    public Dictionary<string, string[]>? Errors { get; set; }
}

/// <summary>Body of GET /plugin/latest (docs/api.md 8).</summary>
public sealed class PluginRelease
{
    public string Version { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset? ReleasedAt { get; set; }
    public string? ReleaseUrl { get; set; }

    /// <summary>Installer asset; null when the release has none (open <see cref="ReleaseUrl"/>).</summary>
    public string? DownloadUrl { get; set; }

    public string? FileName { get; set; }
    public long? SizeBytes { get; set; }

    /// <summary>SHA-256 of the installer. The file must match it before it is run.</summary>
    public string? Sha256 { get; set; }

    public string MinVersion { get; set; } = "0.0.0";
    public string? Current { get; set; }
    public bool? UpdateAvailable { get; set; }
    public bool? UpdateRequired { get; set; }
}
