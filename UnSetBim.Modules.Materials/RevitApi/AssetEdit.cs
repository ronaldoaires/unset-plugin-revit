using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Visual;
using UnSetBim.Core.Diagnostics;

namespace UnSetBim.Modules.Materials.RevitApi;

/// <summary>Size, rotation and position of one repetition of an image on the surface.</summary>
internal readonly record struct TileTransform(double WidthMm, double HeightMm, double RotationDeg = 0, double OffsetXMm = 0, double OffsetYMm = 0);

/// <summary>A bitmap linked to an appearance property, as read before rebuilding it.</summary>
internal sealed record LinkedBitmap(string Property, string File, bool IsNormalMap, bool Invert);

/// <summary>
/// Reading and writing Generic appearance properties (public Revit API, documented schema names).
/// Shared by the material factory and the material editor. Missing properties are ignored and
/// failed writes are logged: schemas vary slightly between Revit years, and one missing tweak
/// must never cancel a whole material.
/// </summary>
internal static class AssetEdit
{
    /// <summary>Appearance properties that can carry an image.</summary>
    public static readonly string[] MapProperties = { "generic_diffuse", "generic_bump_map", "generic_glossiness", "generic_cutout_opacity" };

    // ── Linking images ──────────────────────────────────

    /// <summary>
    /// Links <paramref name="file"/> as a fresh UnifiedBitmap (a new bitmap instead of editing the
    /// old one: edited scales do not always stick).
    /// </summary>
    public static void ConnectBitmap(Asset asset, string propertyName, string file, TileTransform tile, bool invert)
    {
        if (asset.FindByName(propertyName) is not { } property)
            return;

        try
        {
            Disconnect(asset, propertyName);
            property.AddConnectedAsset("UnifiedBitmap");

            var bitmap = property.GetSingleConnectedAsset();
            if (bitmap?.FindByName("unifiedbitmap_Bitmap") is not AssetPropertyString path || !path.IsValidValue(file))
            {
                Log.Warn($"Could not link {Path.GetFileName(file)} to {propertyName}.");
                return;
            }

            path.Value = file;
            SetBool(bitmap, "unifiedbitmap_Invert", invert);
            ApplyTransforms(bitmap, tile);
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not link {Path.GetFileName(file)} to {propertyName}.", ex);
        }
    }

    /// <summary>
    /// Relief: the normal map through Revit's bump-map asset (type = normal map); if that asset is
    /// not available, the height image as a classic bump. Returns false when nothing was linked.
    /// </summary>
    public static bool ConnectRelief(Asset asset, string? normalFile, string? heightFile, TileTransform tile, double amount)
    {
        if (asset.FindByName("generic_bump_map") is not { } bumpProperty)
            return false;

        if (normalFile is not null)
        {
            try
            {
                Disconnect(asset, "generic_bump_map");
                bumpProperty.AddConnectedAsset("BumpMap");
                var bump = bumpProperty.GetSingleConnectedAsset();

                if (bump?.FindByName("bumpmap_Bitmap") is AssetPropertyString path && path.IsValidValue(normalFile))
                {
                    path.Value = normalFile;
                    SetInt(bump, "bumpmap_Type", 1); // 1 = normal map
                    ApplyTransforms(bump, tile);
                    SetDouble(asset, "generic_bump_amount", amount);
                    return true;
                }

                Log.Info("BumpMap asset without bumpmap_Bitmap: falling back to a height bump.");
            }
            catch (Exception ex)
            {
                Log.Info($"Normal map not supported here ({ex.Message}); falling back to a height bump.");
            }
        }

        if (heightFile is not null)
        {
            ConnectBitmap(asset, "generic_bump_map", heightFile, tile, invert: false);
            SetDouble(asset, "generic_bump_amount", amount);
            return true;
        }

        Disconnect(asset, "generic_bump_map");
        return false;
    }

    /// <summary>Real-world size of one repetition, rotation, position, linked transforms.</summary>
    public static void ApplyTransforms(Asset bitmap, TileTransform tile)
    {
        // The lock forces X = Y: off before writing different sizes.
        SetBool(bitmap, "texture_ScaleLock", false);
        SetDistanceMm(bitmap, "texture_RealWorldScaleX", Math.Max(tile.WidthMm, 1));
        SetDistanceMm(bitmap, "texture_RealWorldScaleY", Math.Max(tile.HeightMm, 1));
        SetDistanceMm(bitmap, "texture_RealWorldOffsetX", tile.OffsetXMm);
        SetDistanceMm(bitmap, "texture_RealWorldOffsetY", tile.OffsetYMm);
        SetDouble(bitmap, "texture_WAngle", NormalizeAngle(tile.RotationDeg));
        SetBool(bitmap, "texture_LinkTextureTransforms", true);
    }

    /// <summary>Bitmap linked to a property (read-only or editable asset), or null.</summary>
    public static Asset? Connected(Asset asset, string propertyName) =>
        asset.FindByName(propertyName) is { NumberOfConnectedProperties: > 0 } property
            ? property.GetSingleConnectedAsset()
            : null;

    /// <summary>The image file behind a property, whatever bitmap kind links it.</summary>
    public static LinkedBitmap? ReadLinked(Asset asset, string propertyName)
    {
        if (Connected(asset, propertyName) is not { } bitmap)
            return null;

        if (GetString(bitmap, "bumpmap_Bitmap") is { Length: > 0 } normal)
            return new LinkedBitmap(propertyName, CleanPath(normal), IsNormalMap: true, Invert: false);

        return GetString(bitmap, "unifiedbitmap_Bitmap") is { Length: > 0 } file
            ? new LinkedBitmap(propertyName, CleanPath(file), IsNormalMap: false, Invert: GetBool(bitmap, "unifiedbitmap_Invert") ?? false)
            : null;
    }

    /// <summary>Tile transform of a linked bitmap (the diffuse one is the reference).</summary>
    public static TileTransform? ReadTransform(Asset bitmap)
    {
        var width = GetDistanceMm(bitmap, "texture_RealWorldScaleX");
        var height = GetDistanceMm(bitmap, "texture_RealWorldScaleY");
        if (width is null || height is null)
            return null;

        return new TileTransform(
            width.Value,
            height.Value,
            GetDouble(bitmap, "texture_WAngle") ?? 0,
            GetDistanceMm(bitmap, "texture_RealWorldOffsetX") ?? 0,
            GetDistanceMm(bitmap, "texture_RealWorldOffsetY") ?? 0);
    }

    public static void Disconnect(Asset asset, string name)
    {
        if (asset.FindByName(name) is { NumberOfConnectedProperties: > 0 } property)
            property.RemoveConnectedAsset();
    }

    // ── Values ──────────────────────────────────────────

    public static void SetBool(Asset asset, string name, bool value) =>
        TrySet(name, () =>
        {
            if (asset.FindByName(name) is AssetPropertyBoolean property)
                property.Value = value;
        });

    public static void SetInt(Asset asset, string name, int value) =>
        TrySet(name, () =>
        {
            if (asset.FindByName(name) is AssetPropertyInteger property)
                property.Value = value;
        });

    public static void SetDouble(Asset asset, string name, double value) =>
        TrySet(name, () =>
        {
            switch (asset.FindByName(name))
            {
                case AssetPropertyDouble d:
                    d.Value = value;
                    break;
                case AssetPropertyFloat f:
                    f.Value = (float)value;
                    break;
            }
        });

    public static void SetDistanceMm(Asset asset, string name, double millimeters) =>
        TrySet(name, () =>
        {
            if (asset.FindByName(name) is AssetPropertyDistance property)
                property.Value = UnitUtils.Convert(millimeters, UnitTypeId.Millimeters, property.GetUnitTypeId());
        });

    public static void SetColor(Asset asset, string name, Color color) =>
        TrySet(name, () =>
        {
            if (asset.FindByName(name) is AssetPropertyDoubleArray4d property)
                property.SetValueAsColor(color);
        });

    public static bool? GetBool(Asset asset, string name) =>
        asset.FindByName(name) is AssetPropertyBoolean property ? property.Value : null;

    public static double? GetDouble(Asset asset, string name) => asset.FindByName(name) switch
    {
        AssetPropertyDouble d => d.Value,
        AssetPropertyFloat f => f.Value,
        _ => null,
    };

    public static string? GetString(Asset asset, string name) =>
        asset.FindByName(name) is AssetPropertyString property ? property.Value : null;

    public static double? GetDistanceMm(Asset asset, string name) =>
        asset.FindByName(name) is AssetPropertyDistance property
            ? UnitUtils.Convert(property.Value, property.GetUnitTypeId(), UnitTypeId.Millimeters)
            : null;

    public static Color? GetColor(Asset asset, string name) =>
        asset.FindByName(name) is AssetPropertyDoubleArray4d property ? property.GetValueAsColor() : null;

    public static Color? ParseColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return null;

        hex = hex.Trim();
        if (hex.Length != 7 || hex[0] != '#')
            return null;

        try
        {
            return new Color(
                Convert.ToByte(hex.Substring(1, 2), 16),
                Convert.ToByte(hex.Substring(3, 2), 16),
                Convert.ToByte(hex.Substring(5, 2), 16));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public static string ToHex(Color color) => $"#{color.Red:x2}{color.Green:x2}{color.Blue:x2}";

    private static double NormalizeAngle(double degrees)
    {
        var angle = degrees % 360;
        return angle < 0 ? angle + 360 : angle;
    }

    // Revit sometimes stores several paths separated by "|" (relative + absolute): keep the first existing one.
    private static string CleanPath(string value)
    {
        var parts = value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.FirstOrDefault(File.Exists) ?? parts.FirstOrDefault() ?? value;
    }

    private static void TrySet(string name, Action set)
    {
        try
        {
            set();
        }
        catch (Exception ex)
        {
            Log.Info($"Appearance property {name} not set: {ex.Message}");
        }
    }
}
