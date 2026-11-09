using UnSetBim.Core.Api.Models;

namespace UnSetBim.Core.Textures;

/// <summary>An extracted texture package on disk: the folder with the maps and its texture.json.</summary>
public sealed class TexturePackage
{
    public TexturePackage(string folder, TextureManifest manifest)
    {
        Folder = folder;
        Manifest = manifest;
    }

    public string Folder { get; }

    public TextureManifest Manifest { get; }

    public string Slug => Manifest.Slug;

    public int Version => Manifest.Version;

    public string Resolution => Manifest.Resolution;

    /// <summary>Absolute path of a map (see <see cref="TextureMapType"/>), or null when the package has none.</summary>
    public string? GetMapPath(string mapType) =>
        Manifest.Maps.TryGetValue(mapType, out var file) ? Path.Combine(Folder, file) : null;

    public bool HasMap(string mapType) => GetMapPath(mapType) is { } path && File.Exists(path);
}
