using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Visual;
using UnSetBim.Core.Api.Models;
using UnSetBim.Core.Diagnostics;
using UnSetBim.Core.Textures;
using static UnSetBim.Modules.Materials.RevitApi.AssetEdit;

namespace UnSetBim.Modules.Materials.RevitApi;

/// <summary>A material of the active document created by the plugin.</summary>
public sealed class ProjectMaterialInfo
{
    public long Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public int Version { get; init; }
    public string Resolution { get; init; } = string.Empty;

    public override string ToString() => Name;
}

/// <summary>Editable settings of a texture material, as shown in the editor.</summary>
public sealed class MaterialSettings
{
    public double WidthMm { get; set; }
    public double HeightMm { get; set; }
    public double RotationDeg { get; set; }
    public double OffsetXMm { get; set; }
    public double OffsetYMm { get; set; }

    /// <summary>Tile size of the texture as published on the site (for "Tamanho original").</summary>
    public double OriginalWidthMm { get; set; }
    public double OriginalHeightMm { get; set; }

    public bool HasRelief { get; set; }
    public double ReliefAmount { get; set; } = 1;

    /// <summary>Image brightness (diffuse bitmap RGB amount), 1 = as is.</summary>
    public double ImageBrightness { get; set; } = 1;

    /// <summary>A roughness image exists (linked now or in the downloaded package).</summary>
    public bool RoughnessAvailable { get; set; }
    public bool UseRoughnessMap { get; set; }
    public string? RoughnessFile { get; set; }

    /// <summary>0 = matte, 1 = mirror-like. Used when the roughness map is off.</summary>
    public double Glossiness { get; set; } = 0.25;

    public bool TintEnabled { get; set; }
    public string TintColor { get; set; } = "#ffffff";

    /// <summary>Diffuse image on disk (for the preview), or null.</summary>
    public string? DiffuseFile { get; set; }

    public MaterialSettings Clone() => (MaterialSettings)MemberwiseClone();
}

/// <summary>
/// Reads and changes the appearance of materials made by <see cref="TextureMaterialFactory"/>.
/// Every method must run in a valid Revit API context; <see cref="Apply"/> opens its own transaction.
/// </summary>
internal static class TextureMaterialEditor
{
    public static List<ProjectMaterialInfo> List(Document doc) =>
        new FilteredElementCollector(doc)
            .OfClass(typeof(Material))
            .Cast<Material>()
            .Select(m => (Material: m, Marker: TextureMaterialSchema.Read(m)))
            .Where(x => x.Marker is not null)
            .Select(x => new ProjectMaterialInfo
            {
                Id = x.Material.Id.Value,
                Name = x.Material.Name,
                Slug = x.Marker!.Slug,
                Version = x.Marker.Version,
                Resolution = x.Marker.Resolution,
            })
            .OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    public static MaterialSettings? Read(Document doc, long materialId)
    {
        if (doc.GetElement(new ElementId(materialId)) is not Material material
            || doc.GetElement(material.AppearanceAssetId) is not AppearanceAssetElement appearance
            || appearance.GetRenderingAsset() is not { } asset)
        {
            return null;
        }

        var settings = new MaterialSettings();
        var marker = TextureMaterialSchema.Read(material);
        var package = marker is null ? null : TextureCache.TryGet(marker.Slug, marker.Version, marker.Resolution);

        var diffuse = Connected(asset, "generic_diffuse");
        var tile = (diffuse is null ? null : ReadTransform(diffuse)) ?? new TileTransform(1000, 1000);

        settings.WidthMm = Math.Round(tile.WidthMm, 1);
        settings.HeightMm = Math.Round(tile.HeightMm, 1);
        settings.RotationDeg = Math.Round(tile.RotationDeg > 180 ? tile.RotationDeg - 360 : tile.RotationDeg, 1);
        settings.OffsetXMm = Math.Round(tile.OffsetXMm, 1);
        settings.OffsetYMm = Math.Round(tile.OffsetYMm, 1);
        settings.OriginalWidthMm = package?.Manifest.TileWidthMm > 0 ? package.Manifest.TileWidthMm : settings.WidthMm;
        settings.OriginalHeightMm = package?.Manifest.TileHeightMm > 0 ? package.Manifest.TileHeightMm : settings.HeightMm;

        settings.DiffuseFile = ReadLinked(asset, "generic_diffuse")?.File;
        settings.ImageBrightness = diffuse is null ? 1 : GetDouble(diffuse, "unifiedbitmap_RGBAmount") ?? 1;

        settings.HasRelief = Connected(asset, "generic_bump_map") is not null;
        settings.ReliefAmount = GetDouble(asset, "generic_bump_amount") ?? 1;

        var roughness = ReadLinked(asset, "generic_glossiness");
        settings.UseRoughnessMap = roughness is not null;
        settings.RoughnessFile = roughness?.File ?? package?.GetMapPath(TextureMapType.Roughness);
        settings.RoughnessAvailable = settings.RoughnessFile is not null;
        settings.Glossiness = GetDouble(asset, "generic_glossiness") ?? 0.25;

        settings.TintEnabled = GetBool(asset, "common_Tint_toggle") ?? false;
        settings.TintColor = GetColor(asset, "common_Tint_color") is { } tint ? ToHex(tint) : "#ffffff";

        return settings;
    }

    /// <summary>
    /// Writes the settings: every linked image is relinked with the new size, rotation and
    /// position (relinking is what makes Revit pick the change up reliably).
    /// </summary>
    public static void Apply(Document doc, long materialId, MaterialSettings settings)
    {
        if (doc.GetElement(new ElementId(materialId)) is not Material material
            || doc.GetElement(material.AppearanceAssetId) is not AppearanceAssetElement appearance)
        {
            throw new InvalidOperationException("O material não existe mais neste projeto.");
        }

        var tile = new TileTransform(settings.WidthMm, settings.HeightMm, settings.RotationDeg, settings.OffsetXMm, settings.OffsetYMm);

        using var transaction = new Transaction(doc, "UnSet BIM - Ajustar material");
        transaction.Start();

        try
        {
            // Never change another material that shares this appearance.
            appearance = TextureMaterialTools.EnsureOwnAppearance(doc, material, appearance);

            using (var scope = new AppearanceAssetEditScope(doc))
            {
                var asset = scope.Start(appearance.Id);

                // Read every link before touching anything.
                var diffuse = ReadLinked(asset, "generic_diffuse");
                var relief = ReadLinked(asset, "generic_bump_map");
                var roughness = ReadLinked(asset, "generic_glossiness");
                var cutout = ReadLinked(asset, "generic_cutout_opacity");

                if (diffuse is not null)
                {
                    ConnectBitmap(asset, "generic_diffuse", diffuse.File, tile, diffuse.Invert);
                    if (Connected(asset, "generic_diffuse") is { } bitmap)
                        SetDouble(bitmap, "unifiedbitmap_RGBAmount", Math.Clamp(settings.ImageBrightness, 0.05, 3));
                }

                if (relief is not null)
                {
                    ConnectRelief(asset,
                        relief.IsNormalMap ? relief.File : null,
                        relief.IsNormalMap ? null : relief.File,
                        tile,
                        Math.Clamp(settings.ReliefAmount, 0, 5));
                }

                var roughnessFile = roughness?.File ?? settings.RoughnessFile;
                if (settings.UseRoughnessMap && roughnessFile is not null)
                {
                    ConnectBitmap(asset, "generic_glossiness", roughnessFile, tile, invert: true);
                }
                else
                {
                    Disconnect(asset, "generic_glossiness");
                    SetDouble(asset, "generic_glossiness", Math.Clamp(settings.Glossiness, 0, 1));
                }

                if (cutout is not null)
                    ConnectBitmap(asset, "generic_cutout_opacity", cutout.File, tile, cutout.Invert);

                SetBool(asset, "common_Tint_toggle", settings.TintEnabled);
                if (ParseColor(settings.TintColor) is { } tint)
                    SetColor(asset, "common_Tint_color", tint);

                scope.Commit(true);
            }

            transaction.Commit();
        }
        catch
        {
            if (transaction.HasStarted() && !transaction.HasEnded())
                transaction.RollBack();
            throw;
        }

        Log.Info($"Material {material.Name} adjusted: {settings.WidthMm}x{settings.HeightMm} mm, {settings.RotationDeg}°.");
    }
}
