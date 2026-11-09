using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace UnSetBim.Modules.Materials.RevitApi;

/// <summary>Result of <see cref="TextureMaterialTools.PickMaterialFromFace"/>.</summary>
internal sealed record PickedMaterial(bool Cancelled, long? MaterialId, bool IsUnSet);

/// <summary>
/// Local operations on texture materials that never touch the server: variations (same images,
/// own settings) and finding the material of a face.
/// </summary>
internal static class TextureMaterialTools
{
    /// <summary>
    /// Copies a texture material with its own appearance, so its scale, rotation and finish can
    /// differ from the original (Revit transforms belong to the material, not to the face).
    /// Uses the same image files already on disk. Opens its own transaction.
    /// </summary>
    public static ProjectMaterialInfo CreateVariant(Document doc, long materialId)
    {
        if (doc.GetElement(new ElementId(materialId)) is not Material source)
            throw new InvalidOperationException("O material não existe mais neste projeto.");

        using var transaction = new Transaction(doc, "UnSet BIM - Criar variação de material");
        transaction.Start();

        try
        {
            var name = NextVariantName(doc, source.Name);
            var copy = source.Duplicate(name);

            if (doc.GetElement(source.AppearanceAssetId) is AppearanceAssetElement appearance)
            {
                var ownAppearance = appearance.Duplicate(TextureMaterialFactory.UniqueAppearanceName(doc, name));
                copy.AppearanceAssetId = ownAppearance.Id;
            }

            var marker = TextureMaterialSchema.Read(source);
            if (marker is not null)
                TextureMaterialSchema.Write(copy, marker);

            transaction.Commit();

            return new ProjectMaterialInfo
            {
                Id = copy.Id.Value,
                Name = copy.Name,
                Slug = marker?.Slug ?? string.Empty,
                Version = marker?.Version ?? 0,
                Resolution = marker?.Resolution ?? string.Empty,
            };
        }
        catch
        {
            if (transaction.HasStarted() && !transaction.HasEnded())
                transaction.RollBack();
            throw;
        }
    }

    /// <summary>
    /// Gives the material its own appearance when it shares one with other materials (e.g. a
    /// material duplicated in Revit's Material Browser shares the appearance by default), so an
    /// edit never changes another material. Must run inside an open transaction.
    /// </summary>
    public static AppearanceAssetElement EnsureOwnAppearance(Document doc, Material material, AppearanceAssetElement appearance)
    {
        if (TextureMaterialFactory.UsersOf(doc, appearance.Id) <= 1)
            return appearance;

        var own = appearance.Duplicate(TextureMaterialFactory.UniqueAppearanceName(doc, material.Name));
        material.AppearanceAssetId = own.Id;
        doc.Regenerate();
        return own;
    }

    /// <summary>The user clicks a face; returns the material painted on it (or the element's own).</summary>
    public static PickedMaterial PickMaterialFromFace(UIDocument uidoc)
    {
        var doc = uidoc.Document;

        Reference reference;
        try
        {
            reference = uidoc.Selection.PickObject(ObjectType.Face, "Clique numa face para abrir o material dela. Esc cancela.");
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            return new PickedMaterial(Cancelled: true, null, false);
        }

        var element = doc.GetElement(reference);
        if (element?.GetGeometryObjectFromReference(reference) is not Face face)
            return new PickedMaterial(false, null, false);

        var target = FacePainter.RegionUnderCursor(face, reference) ?? face;
        var materialId = doc.IsPainted(element.Id, target)
            ? doc.GetPaintedMaterial(element.Id, target)
            : target.MaterialElementId;

        if (materialId == ElementId.InvalidElementId || doc.GetElement(materialId) is not Material material)
            return new PickedMaterial(false, null, false);

        return new PickedMaterial(false, material.Id.Value, TextureMaterialSchema.Read(material) is not null);
    }

    /// <summary>
    /// Deletes a material from the project. Painted faces go back to their element's material;
    /// elements and types that pointed at it fall back to "By Category". Its own appearance is
    /// deleted too when no other material uses it. Opens its own transaction.
    /// </summary>
    public static string DeleteMaterial(Document doc, long materialId)
    {
        if (doc.GetElement(new ElementId(materialId)) is not Material material)
            throw new InvalidOperationException("O material não existe mais neste projeto.");

        var name = material.Name;
        var appearanceId = material.AppearanceAssetId;

        using var transaction = new Transaction(doc, "UnSet BIM - Excluir material");
        transaction.Start();

        try
        {
            doc.Delete(material.Id);

            // Our appearances are named "UnSet - ..."; never delete a shared or foreign one.
            if (doc.GetElement(appearanceId) is AppearanceAssetElement appearance
                && appearance.Name.StartsWith("UnSet - ", StringComparison.Ordinal)
                && TextureMaterialFactory.UsersOf(doc, appearanceId) == 0)
            {
                doc.Delete(appearanceId);
            }

            transaction.Commit();
            return name;
        }
        catch
        {
            if (transaction.HasStarted() && !transaction.HasEnded())
                transaction.RollBack();
            throw;
        }
    }

    /// <summary>"UnSet - Pedra" → "UnSet - Pedra - 2", "UnSet - Pedra - 2" → "UnSet - Pedra - 3"...</summary>
    private static string NextVariantName(Document doc, string name)
    {
        var baseName = Regex.Replace(name, @" - \d+$", string.Empty);
        var names = new FilteredElementCollector(doc).OfClass(typeof(Material)).Select(m => m.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var n = 2;
        while (names.Contains($"{baseName} - {n}"))
            n++;

        return $"{baseName} - {n}";
    }
}
