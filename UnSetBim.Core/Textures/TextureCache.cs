using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using UnSetBim.Core.Api;
using UnSetBim.Core.Api.Models;
using UnSetBim.Core.Diagnostics;
using UnSetBim.Core.Storage;

namespace UnSetBim.Core.Textures;

/// <summary>
/// Local copies of texture packages: Textures\{slug}\v{version}\{resolution}\{type}.png + texture.json.
///
/// Unlike families, textures are kept: a Revit material stores the absolute path of its images,
/// so the files must stay where they were when the material was created. Old versions are not
/// deleted automatically for the same reason (projects may still point at them).
/// </summary>
public static class TextureCache
{
    public const string ManifestFileName = "texture.json";

    // Package entries are flat: "{type}.{png|jpg}" plus texture.json. Anything else is rejected,
    // which also rules out "..\" style paths escaping the cache folder.
    private static readonly Regex AllowedEntry = new(@"^[a-z_]+\.(png|jpg|jpeg)$|^texture\.json$", RegexOptions.CultureInvariant);

    // Sanity limit against a broken or hostile archive (an 8k set is well below this).
    private const long MaxExtractedBytes = 2L * 1024 * 1024 * 1024;

    public static string GetFolder(string slug, int version, string resolution) =>
        Path.Combine(PluginPaths.TexturesFolder, SafeSegment(slug), $"v{version}", SafeSegment(resolution));

    /// <summary>The cached package, or null when it is missing or incomplete.</summary>
    public static TexturePackage? TryGet(string slug, int version, string resolution)
    {
        var folder = GetFolder(slug, version, resolution);
        var manifestFile = Path.Combine(folder, ManifestFileName);

        if (!File.Exists(manifestFile))
            return null;

        try
        {
            var manifest = JsonSerializer.Deserialize<TextureManifest>(File.ReadAllText(manifestFile), ApiJson.Options);
            if (manifest is null)
                return null;

            var package = new TexturePackage(folder, manifest);

            // Every listed map must still be there (the user may have cleaned files by hand).
            return manifest.Maps.Keys.All(package.HasMap) ? package : null;
        }
        catch (Exception ex)
        {
            Log.Warn($"Ignoring unreadable texture cache at {folder}.", ex);
            return null;
        }
    }

    /// <summary>
    /// Extracts a downloaded package into its cache folder (version and resolution read from its
    /// texture.json) and returns it. Extraction goes to a temporary sibling folder that is renamed
    /// at the end, so a crash never leaves a half-filled cache folder.
    /// </summary>
    public static TexturePackage Install(string zipFile, string expectedSlug)
    {
        using var archive = ZipFile.OpenRead(zipFile);

        var manifestEntry = archive.GetEntry(ManifestFileName)
            ?? throw new InvalidDataException("Texture package without texture.json.");

        TextureManifest manifest;
        using (var stream = manifestEntry.Open())
        {
            manifest = JsonSerializer.Deserialize<TextureManifest>(stream, ApiJson.Options)
                ?? throw new InvalidDataException("Empty texture.json.");
        }

        if (!string.Equals(manifest.Slug, expectedSlug, StringComparison.Ordinal))
            throw new InvalidDataException($"Package is for '{manifest.Slug}', expected '{expectedSlug}'.");

        if (TextureResolutions.Rank(manifest.Resolution) < 0 || manifest.Version < 1)
            throw new InvalidDataException("Invalid version or resolution in texture.json.");

        long total = 0;
        foreach (var entry in archive.Entries)
        {
            if (!AllowedEntry.IsMatch(entry.FullName))
                throw new InvalidDataException($"Unexpected entry in texture package: {entry.FullName}");

            total += entry.Length;
        }

        if (total > MaxExtractedBytes)
            throw new InvalidDataException("Texture package is too large.");

        foreach (var (type, file) in manifest.Maps)
        {
            if (!AllowedEntry.IsMatch(file) || archive.GetEntry(file) is null)
                throw new InvalidDataException($"Map '{type}' listed in texture.json is missing: {file}");
        }

        var folder = GetFolder(manifest.Slug, manifest.Version, manifest.Resolution);

        // Another Revit (or a previous run) may already have it.
        if (TryGet(manifest.Slug, manifest.Version, manifest.Resolution) is { } existing)
            return existing;

        var parent = Path.GetDirectoryName(folder)!;
        Directory.CreateDirectory(parent);

        var temp = Path.Combine(parent, $".{manifest.Resolution}-{Guid.NewGuid():N}.tmp");
        Directory.CreateDirectory(temp);

        try
        {
            foreach (var entry in archive.Entries)
                entry.ExtractToFile(Path.Combine(temp, entry.FullName), overwrite: true);

            // An incomplete folder from an older crash would block the rename.
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);

            Directory.Move(temp, folder);
        }
        catch
        {
            TryDeleteFolder(temp);
            throw;
        }

        return new TexturePackage(folder, manifest);
    }

    /// <summary>Path segment that cannot leave the cache folder (slugs are already safe; this is defense in depth).</summary>
    private static string SafeSegment(string value)
    {
        var clean = new string(value.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
        return string.IsNullOrEmpty(clean) ? "_" : clean;
    }

    private static void TryDeleteFolder(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not remove temporary texture folder {path}.", ex);
        }
    }
}
