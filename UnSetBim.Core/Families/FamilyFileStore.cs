using UnSetBim.Core.Api.Models;
using UnSetBim.Core.Diagnostics;
using UnSetBim.Core.Storage;

namespace UnSetBim.Core.Families;

/// <summary>
/// Where downloaded .rfa files are written: Families\{slug}\{Family name}.rfa.
///
/// The file name is the family's display name because Revit names a loaded family after its file.
///
/// There is deliberately no "reuse the cached file" shortcut: every insertion goes through
/// GET /families/{slug}/download, so the server stays the single place that applies the
/// download rule, counts the quota and records the history.
/// </summary>
public static class FamilyFileStore
{
    // Characters Revit does not accept in family names, on top of the invalid file name characters.
    private static readonly char[] RevitForbidden = { '\\', ':', '{', '}', '[', ']', '|', ';', '<', '>', '?', '`', '~', '/', '"', '*' };

    public static string GetFilePath(FamilyDetail family) =>
        Path.Combine(GetFolder(family.Slug), SafeFamilyName(family.Name, family.Slug) + ".rfa");

    /// <summary>The family name Revit will give the loaded family (file name without extension).</summary>
    public static string GetRevitFamilyName(FamilyDetail family) => SafeFamilyName(family.Name, family.Slug);

    /// <summary>Remove older files of the same family (e.g. after a rename on the site).</summary>
    public static void CleanupSiblings(string keepFile)
    {
        try
        {
            var folder = Path.GetDirectoryName(keepFile)!;
            foreach (var file in Directory.EnumerateFiles(folder, "*.rfa"))
            {
                if (!string.Equals(file, keepFile, StringComparison.OrdinalIgnoreCase))
                    File.Delete(file);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Could not clean old family files.", ex);
        }
    }

    public static string SafeFamilyName(string name, string fallback)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(RevitForbidden).ToHashSet();
        var clean = new string(name.Select(c => invalid.Contains(c) ? '-' : c).ToArray()).Trim().Trim('.');

        if (clean.Length > 120)
            clean = clean[..120].Trim();

        return string.IsNullOrWhiteSpace(clean) ? fallback : clean;
    }

    private static string GetFolder(string slug)
    {
        var folder = Path.Combine(PluginPaths.FamiliesFolder, SafeFamilyName(slug, "familia"));
        Directory.CreateDirectory(folder);
        return folder;
    }
}
