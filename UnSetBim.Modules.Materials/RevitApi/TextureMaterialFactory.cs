using System.IO;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Visual;
using UnSetBim.Core.Api.Models;
using UnSetBim.Core.Diagnostics;
using UnSetBim.Core.Textures;
using static UnSetBim.Modules.Materials.RevitApi.AssetEdit;

namespace UnSetBim.Modules.Materials.RevitApi;

/// <summary>What the factory needs: the texture data and, for image materials, the package on disk.</summary>
internal sealed record TextureMaterialRequest(TextureDetail Texture, TexturePackage? Package);

/// <summary>Result of <see cref="TextureMaterialFactory.Ensure"/>.</summary>
internal sealed record TextureMaterialResult(ElementId MaterialId, string MaterialName, bool Created, bool Updated);

/// <summary>
/// Creates (or reuses) a native Revit material for a texture: a Generic render appearance with the
/// package's maps linked as bitmaps at the real-world tile size from the server.
///
/// Built only with the public Revit API; the appearance properties are the documented
/// Generic/UnifiedBitmap schema names (generic_diffuse, texture_RealWorldScaleX...).
/// Must be called in a valid API context; it opens its own transaction.
/// </summary>
internal static class TextureMaterialFactory
{
    private const string NamePrefix = "UnSet - ";
    private const string BaseAppearanceName = "UnSet - Base Generic";

    // Reflects a bit of light without looking wet: used when the package has no roughness map.
    private const double DefaultGlossiness = 0.25;

    public static TextureMaterialResult Ensure(Document doc, Application app, TextureMaterialRequest request)
    {
        var texture = request.Texture;
        var marker = new TextureMaterialSchema.Marker(
            texture.Slug,
            request.Package?.Version ?? texture.Version,
            request.Package?.Resolution ?? string.Empty);

        var existing = TextureMaterialSchema.FindMaterial(doc, texture.Slug);
        if (existing is not null && TextureMaterialSchema.Read(existing) == marker && ImagesExist(doc, existing, request.Package))
            return new TextureMaterialResult(existing.Id, existing.Name, Created: false, Updated: false);

        using var transaction = new Transaction(doc, "UnSet BIM - Material de textura");
        transaction.Start();

        try
        {
            var created = existing is null;
            var material = existing ?? CreateMaterial(doc, texture.Name);

            var appearance = GetOwnAppearance(doc, app, material, texture.Name);
            ConfigureAppearance(doc, appearance, request);

            material.AppearanceAssetId = appearance.Id;

            if (ParseColor(texture.ShadingColor ?? texture.BaseColor) is { } color)
                material.Color = color;

            // The shaded view uses Material.Color (average of the image) instead of the render appearance.
            material.UseRenderAppearanceForShading = false;

            if (texture.MaterialType is TextureMaterialType.Glass or TextureMaterialType.Water)
                material.Transparency = 60;

            TextureMaterialSchema.Write(material, marker);

            transaction.Commit();
            Log.Info($"Texture material '{material.Name}' {(created ? "created" : "updated")} (v{marker.Version} {marker.Resolution}).");
            return new TextureMaterialResult(material.Id, material.Name, created, !created);
        }
        catch
        {
            if (transaction.HasStarted() && !transaction.HasEnded())
                transaction.RollBack();
            throw;
        }
    }

    private static Material CreateMaterial(Document doc, string textureName)
    {
        var name = UniqueMaterialName(doc, NamePrefix + textureName);
        return (Material)doc.GetElement(Material.Create(doc, name));
    }

    private static string UniqueMaterialName(Document doc, string baseName)
    {
        var names = new FilteredElementCollector(doc).OfClass(typeof(Material)).Select(m => m.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var name = Sanitize(baseName);
        for (var i = 2; names.Contains(name); i++)
            name = $"{Sanitize(baseName)} ({i})";

        return name;
    }

    /// <summary>
    /// The appearance used only by this material (so editing it never changes other materials).
    /// Created by duplicating a clean Generic base.
    /// </summary>
    private static AppearanceAssetElement GetOwnAppearance(Document doc, Application app, Material material, string textureName)
    {
        if (doc.GetElement(material.AppearanceAssetId) is AppearanceAssetElement current
            && current.Name.StartsWith(NamePrefix, StringComparison.Ordinal)
            && current.Name != BaseAppearanceName
            && UsersOf(doc, current.Id) <= 1)
        {
            return current;
        }

        var baseAppearance = GetBaseGeneric(doc, app);
        return baseAppearance.Duplicate(UniqueAppearanceName(doc, NamePrefix + textureName));
    }

    private static int UsersOf(Document doc, ElementId appearanceId) =>
        new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>()
            .Count(m => m.AppearanceAssetId == appearanceId);

    private static string UniqueAppearanceName(Document doc, string baseName)
    {
        var name = Sanitize(baseName);
        for (var i = 2; AppearanceAssetElement.GetAppearanceAssetElementByName(doc, name) is not null; i++)
            name = $"{Sanitize(baseName)} ({i})";

        return name;
    }

    /// <summary>
    /// A Generic appearance kept in the project as the starting point of every texture material:
    /// an existing "UnSet - Base Generic", else a copy of a plain project Generic, else one created
    /// from the Revit library (slow the first time, so it is only the last resort).
    /// </summary>
    private static AppearanceAssetElement GetBaseGeneric(Document doc, Application app)
    {
        if (AppearanceAssetElement.GetAppearanceAssetElementByName(doc, BaseAppearanceName) is { } own)
            return own;

        var candidates = new FilteredElementCollector(doc)
            .OfClass(typeof(AppearanceAssetElement))
            .Cast<AppearanceAssetElement>()
            .Where(e => IsGeneric(e.GetRenderingAsset()))
            .ToList();

        // A plain "Generic" first: themed ones (wood, brick, metal...) carry settings we don't want.
        var plain = candidates.FirstOrDefault(e => e.Name.Equals("Generic", StringComparison.OrdinalIgnoreCase)
                                                   || e.Name.Equals("Genérico", StringComparison.OrdinalIgnoreCase))
                    ?? candidates.FirstOrDefault(e => !LooksThemed(e.Name));

        if (plain is not null)
            return plain.Duplicate(BaseAppearanceName);

        var library = app.GetAssets(AssetType.Appearance).FirstOrDefault(a => IsGeneric(a) && !LooksThemed(a.Name))
                      ?? throw new InvalidOperationException("Nenhuma aparência genérica encontrada no Revit.");

        return AppearanceAssetElement.Create(doc, BaseAppearanceName, library);
    }

    private static bool IsGeneric(Asset? asset) => asset?.FindByName("generic_diffuse") is not null;

    private static bool LooksThemed(string name)
    {
        var lower = name.ToLowerInvariant();
        string[] themes = { "wood", "madeira", "brick", "tijolo", "metal", "glass", "vidro", "stone", "pedra", "concrete", "concreto", "ceramic", "cerâm", "water", "água", "mirror", "espelho" };
        return themes.Any(lower.Contains);
    }

    private static void ConfigureAppearance(Document doc, AppearanceAssetElement appearance, TextureMaterialRequest request)
    {
        var texture = request.Texture;
        var package = request.Package;

        using var scope = new AppearanceAssetEditScope(doc);
        var asset = scope.Start(appearance.Id);

        ResetGeneric(asset);

        if (ParseColor(texture.BaseColor ?? texture.ShadingColor) is { } baseColor)
            SetColor(asset, "generic_diffuse", baseColor);

        if (package is not null)
        {
            var tile = new TileTransform(Math.Max(texture.TileWidthMm, 1), Math.Max(texture.TileHeightMm, 1));

            if (package.GetMapPath(TextureMapType.Diffuse) is { } diffuse)
                ConnectBitmap(asset, "generic_diffuse", diffuse, tile, invert: false);

            ConnectRelief(asset, package.GetMapPath(TextureMapType.Normal), package.GetMapPath(TextureMapType.Displacement), tile, amount: 1.0);

            if (package.GetMapPath(TextureMapType.Roughness) is { } roughness)
            {
                // Roughness is the inverse of glossiness.
                ConnectBitmap(asset, "generic_glossiness", roughness, tile, invert: true);
            }

            if (package.GetMapPath(TextureMapType.Alpha) is { } alpha)
                ConnectBitmap(asset, "generic_cutout_opacity", alpha, tile, invert: false);
        }

        switch (texture.MaterialType)
        {
            case TextureMaterialType.Metal:
                SetBool(asset, "generic_is_metal", true);
                SetDouble(asset, "generic_glossiness", 0.7);
                break;
            case TextureMaterialType.Mirror:
                SetBool(asset, "generic_is_metal", true);
                SetDouble(asset, "generic_glossiness", 1.0);
                break;
            case TextureMaterialType.Glass:
            case TextureMaterialType.Water:
                SetDouble(asset, "generic_transparency", 0.85);
                SetDouble(asset, "generic_glossiness", 0.95);
                break;
            case TextureMaterialType.Emissive:
                SetDouble(asset, "generic_self_illum_luminance", 1000);
                break;
        }

        scope.Commit(true);
    }

    /// <summary>Clears what a duplicated base might carry, so every texture starts from the same state.</summary>
    private static void ResetGeneric(Asset asset)
    {
        foreach (var name in MapProperties)
            Disconnect(asset, name);

        SetBool(asset, "generic_is_metal", false);
        SetDouble(asset, "generic_transparency", 0);
        SetDouble(asset, "generic_glossiness", DefaultGlossiness);
        SetBool(asset, "common_Tint_toggle", false);
    }

    private static bool ImagesExist(Document doc, Material material, TexturePackage? package)
    {
        if (package is null)
            return true;

        // Same version and resolution, but the folder may have been cleaned: rebuild the links.
        return package.Manifest.Maps.Keys.All(package.HasMap)
               && doc.GetElement(material.AppearanceAssetId) is AppearanceAssetElement;
    }

    /// <summary>Characters Revit does not accept in element names.</summary>
    private static string Sanitize(string name)
    {
        var forbidden = "\\:{}[]|;<>?`~";
        var clean = new string(name.Where(c => !forbidden.Contains(c)).ToArray()).Trim();
        return clean.Length > 120 ? clean[..120] : clean;
    }
}
