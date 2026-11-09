using UnSetBim.Core.Api;
using UnSetBim.Core.Api.Models;
using UnSetBim.Core.Diagnostics;

namespace UnSetBim.Core.Textures;

/// <summary>
/// Gets a texture package ready on disk: from the cache when the same version and resolution
/// are already there, otherwise through the download endpoint (which applies the access rule and
/// records the download on the server).
/// </summary>
public sealed class TextureStore
{
    private readonly UnSetApiClient _api;

    public TextureStore(UnSetApiClient api)
    {
        _api = api;
    }

    /// <summary>
    /// Returns the package for <paramref name="texture"/> at <paramref name="resolution"/> (null =
    /// the server default). Callers check <see cref="TextureDetail.NeedsDownload"/> and
    /// <c>Download.Allowed</c> first; the server still enforces the rule (ApiException 403).
    /// </summary>
    public async Task<TexturePackage> EnsureAsync(TextureDetail texture, string? resolution, IProgress<double>? progress, CancellationToken ct)
    {
        if (!texture.NeedsDownload)
            throw new InvalidOperationException($"Texture '{texture.Slug}' has no files to download.");

        // Same rule as the server, so a cached package is found without a request.
        var expected = texture.PickResolution(resolution);
        if (expected is not null && TextureCache.TryGet(texture.Slug, texture.Version, expected) is { } cached)
        {
            progress?.Report(1d);
            return cached;
        }

        var zip = Path.Combine(Path.GetTempPath(), $"unset-texture-{Guid.NewGuid():N}.zip");

        try
        {
            var served = await _api.DownloadTexturePackageAsync(texture.Download.Url, resolution, zip, progress, ct);
            var package = TextureCache.Install(zip, texture.Slug);

            Log.Info($"Texture {texture.Slug} v{package.Version} {package.Resolution} ready "
                     + $"(server sent v{served.Version} {served.Resolution}).");

            return package;
        }
        finally
        {
            try
            {
                File.Delete(zip);
            }
            catch
            {
                // Temp file, Windows cleans it eventually.
            }
        }
    }
}
