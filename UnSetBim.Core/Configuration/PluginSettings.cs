using System.Text.Json;
using UnSetBim.Core.Diagnostics;
using UnSetBim.Core.Storage;

namespace UnSetBim.Core.Configuration;

/// <summary>
/// Environment settings. Production values are the defaults; a developer can point the plugin
/// at a local server by creating %LocalAppData%\UnSetBIM\settings.json:
/// <code>
/// { "api_base_url": "http://127.0.0.1:8000/api/v1/", "site_url": "http://127.0.0.1:8000/" }
/// </code>
/// </summary>
public sealed class PluginSettings
{
    public const string ProductionApiBaseUrl = "https://unset.com.br/api/v1/";
    public const string ProductionSiteUrl = "https://unset.com.br/";

    /// <summary>API root, always ending with "/".</summary>
    public Uri ApiBaseUrl { get; }

    /// <summary>Public site root, always ending with "/".</summary>
    public Uri SiteUrl { get; }

    /// <summary>True when settings.json overrides the production URLs.</summary>
    public bool IsCustomEnvironment { get; }

    private PluginSettings(Uri apiBaseUrl, Uri siteUrl, bool isCustom)
    {
        ApiBaseUrl = apiBaseUrl;
        SiteUrl = siteUrl;
        IsCustomEnvironment = isCustom;
    }

    public static PluginSettings Load()
    {
        var api = ProductionApiBaseUrl;
        var site = ProductionSiteUrl;
        var custom = false;

        try
        {
            if (File.Exists(PluginPaths.SettingsFile))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(PluginPaths.SettingsFile));
                var root = doc.RootElement;

                if (root.TryGetProperty("api_base_url", out var a) && a.GetString() is { Length: > 0 } apiValue)
                {
                    api = apiValue;
                    custom = true;
                }

                if (root.TryGetProperty("site_url", out var s) && s.GetString() is { Length: > 0 } siteValue)
                {
                    site = siteValue;
                    custom = true;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("settings.json could not be read, using production URLs.", ex);
            api = ProductionApiBaseUrl;
            site = ProductionSiteUrl;
            custom = false;
        }

        if (custom)
            Log.Info($"Custom environment: api={api} site={site}");

        return new PluginSettings(ToBaseUri(api), ToBaseUri(site), custom);
    }

    private static Uri ToBaseUri(string value)
    {
        if (!value.EndsWith('/'))
            value += "/";

        return new Uri(value, UriKind.Absolute);
    }
}
