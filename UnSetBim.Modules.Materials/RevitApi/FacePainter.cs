using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using UnSetBim.Core.Diagnostics;

namespace UnSetBim.Modules.Materials.RevitApi;

internal sealed record PaintResult(int Painted, int Skipped);

/// <summary>
/// "Pintar faces": the user clicks faces until Esc; each click paints the face (or the split
/// region under the cursor) with the material, in its own transaction so Ctrl+Z undoes one face.
/// </summary>
internal static class FacePainter
{
    public static PaintResult PaintFaces(UIDocument uidoc, ElementId materialId, string materialName)
    {
        var doc = uidoc.Document;
        var painted = 0;
        var skipped = 0;

        while (true)
        {
            Reference reference;
            try
            {
                reference = uidoc.Selection.PickObject(ObjectType.Face, new PaintableFilter(),
                    $"Clique nas faces para aplicar \"{materialName}\". Esc para terminar.");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                break;
            }

            var element = doc.GetElement(reference);
            if (element?.GetGeometryObjectFromReference(reference) is not Face face)
            {
                skipped++;
                continue;
            }

            var target = RegionUnderCursor(face, reference) ?? face;

            using var transaction = new Transaction(doc, "UnSet BIM - Pintar face");
            transaction.Start();
            try
            {
                doc.Paint(element.Id, target, materialId);
                transaction.Commit();
                painted++;
            }
            catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ArgumentException or Autodesk.Revit.Exceptions.InvalidOperationException)
            {
                // Some elements (most loadable families, imported geometry) cannot be painted.
                transaction.RollBack();
                skipped++;
                Log.Info($"Face of element {element.Id} not paintable: {ex.Message}");
            }
        }

        return new PaintResult(painted, skipped);
    }

    /// <summary>
    /// "Remover pintura": the user clicks painted faces until Esc; each click removes the paint
    /// (the face goes back to the element's own material). One transaction per face.
    /// </summary>
    public static PaintResult RemovePaint(UIDocument uidoc)
    {
        var doc = uidoc.Document;
        var removed = 0;
        var skipped = 0;

        while (true)
        {
            Reference reference;
            try
            {
                reference = uidoc.Selection.PickObject(ObjectType.Face, new PaintableFilter(),
                    "Clique nas faces pintadas para remover o material. Esc para terminar.");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                break;
            }

            var element = doc.GetElement(reference);
            if (element?.GetGeometryObjectFromReference(reference) is not Face face)
            {
                skipped++;
                continue;
            }

            var target = RegionUnderCursor(face, reference) ?? face;
            if (!doc.IsPainted(element.Id, target))
            {
                skipped++;
                continue;
            }

            using var transaction = new Transaction(doc, "UnSet BIM - Remover pintura");
            transaction.Start();
            try
            {
                doc.RemovePaint(element.Id, target);
                transaction.Commit();
                removed++;
            }
            catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ArgumentException or Autodesk.Revit.Exceptions.InvalidOperationException)
            {
                transaction.RollBack();
                skipped++;
                Log.Info($"Paint of element {element.Id} not removed: {ex.Message}");
            }
        }

        return new PaintResult(removed, skipped);
    }

    /// <summary>When the face was split (Split Face), paint only the region that was clicked.</summary>
    internal static Face? RegionUnderCursor(Face face, Reference reference)
    {
        if (!face.HasRegions || reference.UVPoint is not { } uv)
            return null;

        return face.GetRegions().FirstOrDefault(region => region.IsInside(uv));
    }

    private sealed class PaintableFilter : ISelectionFilter
    {
        public bool AllowElement(Element element) =>
            element is not ImportInstance && element.Category is not null;

        public bool AllowReference(Reference reference, XYZ position) => true;
    }
}
