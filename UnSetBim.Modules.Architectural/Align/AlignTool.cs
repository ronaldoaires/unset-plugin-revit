using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using UnSetBim.Core.Diagnostics;
using UnSetBim.Framework.Dialogs;

namespace UnSetBim.Modules.Architectural.Align;

/// <summary>
/// Align / center / distribute selected elements in the active view.
///
/// Flow: use the current selection (or ask the user to pick), measure each element's bounding box
/// projected on the view's right/up direction, compute the offsets (<see cref="AlignPlanner"/>),
/// then move everything in one transaction (one Ctrl+Z undoes it).
/// </summary>
internal static class AlignTool
{
    /// <summary>Movements smaller than this (feet, ~0.003 mm) are ignored.</summary>
    private const double Tolerance = 1e-5;

    private static readonly HashSet<ViewType> SupportedViews = new()
    {
        ViewType.FloorPlan,
        ViewType.CeilingPlan,
        ViewType.EngineeringPlan,
        ViewType.AreaPlan,
        ViewType.Section,
        ViewType.Elevation,
        ViewType.Detail,
        ViewType.DraftingView,
        ViewType.Legend,
        ViewType.DrawingSheet,
    };

    public static Result Run(UIApplication app, AlignModeInfo mode)
    {
        var uidoc = app.ActiveUIDocument;
        if (uidoc is null)
            return Result.Cancelled;

        var doc = uidoc.Document;
        var view = doc.ActiveView;

        if (view is null || view.IsTemplate || !SupportedViews.Contains(view.ViewType))
        {
            MessageDialog.Show(
                "Vista não suportada",
                "O Alinhar funciona em plantas, cortes, elevações, vistas de detalhe e de desenho, legendas e folhas. Abra uma dessas vistas e tente de novo.",
                DialogKind.Warning);
            return Result.Cancelled;
        }

        var skipped = new SkipCounter();

        // 1. Elements: current selection, or pick in the view.
        var selected = uidoc.Selection.GetElementIds()
            .Select(doc.GetElement)
            .Where(e => e is not null)
            .ToList();

        var elements = new List<Element>();
        foreach (var element in selected)
        {
            if (WhyNotMovable(element, view) is { } reason)
                skipped.Add(reason);
            else
                elements.Add(element);
        }

        if (elements.Count < mode.MinimumCount)
        {
            var picked = Pick(uidoc, view, mode, elements);
            if (picked is null)
                return Result.Cancelled;

            elements = picked;
            skipped = new SkipCounter(); // the picker only accepts movable elements

            if (elements.Count < mode.MinimumCount)
            {
                MessageDialog.Show(mode.Title, $"Selecione pelo menos {mode.MinimumCount} elementos.", DialogKind.Info);
                return Result.Cancelled;
            }
        }

        // 2. Hosted elements (doors, windows...) whose host is also selected move with the host.
        var selectedIds = new HashSet<ElementId>(elements.Select(e => e.Id));
        var followers = elements.Where(e => e is FamilyInstance { Host: { } host } && selectedIds.Contains(host.Id)).ToList();
        foreach (var follower in followers)
        {
            elements.Remove(follower);
            skipped.Add(SkipReason.MovesWithHost);
        }

        // 3. Measure along the movement axis.
        var axis = (mode.MovesHorizontally ? view.RightDirection : view.UpDirection).Normalize();
        var measured = new List<(Element Element, AlignExtent Extent)>();
        foreach (var element in elements)
        {
            if (TryGetExtent(element, view, axis, measured.Count, out var extent))
                measured.Add((element, extent));
            else
                skipped.Add(SkipReason.NotVisible);
        }

        if (measured.Count < mode.MinimumCount)
        {
            MessageDialog.Show(
                mode.Title,
                $"São necessários pelo menos {mode.MinimumCount} elementos que possam ser movidos nesta vista." + skipped.Describe(),
                DialogKind.Info);
            return Result.Cancelled;
        }

        // 4. Plan and move.
        var offsets = AlignPlanner.Plan(mode.Mode, measured.Select(m => m.Extent).ToList());
        var moves = measured
            .Select(m => (m.Element, Offset: offsets.TryGetValue(m.Extent.Index, out var o) ? o : 0))
            .Where(m => Math.Abs(m.Offset) > Tolerance)
            .ToList();

        if (moves.Count == 0)
        {
            MessageDialog.Show(mode.Title, "Os elementos já estão nessa posição." + skipped.Describe(), DialogKind.Info);
            return Result.Succeeded;
        }

        var moved = 0;
        var failed = 0;

        using (var transaction = new Transaction(doc, "UnSet BIM: " + mode.Title))
        {
            transaction.Start();

            foreach (var (element, offset) in moves)
            {
                try
                {
                    Move(doc, element, axis.Multiply(offset));
                    moved++;
                }
                catch (Exception ex)
                {
                    failed++;
                    Log.Warn($"Align: could not move element {element.Id} ({element.Category?.Name}).", ex);
                }
            }

            if (moved == 0)
            {
                transaction.RollBack();
                MessageDialog.Show(mode.Title, "Nenhum elemento pôde ser movido. Detalhes foram gravados no log do UnSet BIM.", DialogKind.Warning);
                return Result.Failed;
            }

            transaction.Commit();
        }

        // Keep the elements selected so the user can chain another option.
        uidoc.Selection.SetElementIds(measured.Select(m => m.Element.Id).Concat(followers.Select(f => f.Id)).ToList());

        if (failed > 0)
            skipped.Add(SkipReason.MoveFailed, failed);

        if (skipped.Total > 0)
            MessageDialog.Show(mode.Title, $"{moved} elemento(s) movido(s)." + skipped.Describe(), DialogKind.Info);

        return Result.Succeeded;
    }

    /// <summary>Asks the user to pick elements, keeping the valid ones already selected. Null when cancelled.</summary>
    private static List<Element>? Pick(UIDocument uidoc, View view, AlignModeInfo mode, IReadOnlyList<Element> preselected)
    {
        try
        {
            var references = uidoc.Selection.PickObjects(
                ObjectType.Element,
                new MovableElementFilter(view),
                $"{mode.Title}: selecione pelo menos {mode.MinimumCount} elementos e clique em Concluir (Esc cancela).",
                preselected.Select(e => new Reference(e)).ToList());

            return references
                .Select(r => uidoc.Document.GetElement(r))
                .Where(e => e is not null)
                .GroupBy(e => e.Id)
                .Select(g => g.First())
                .ToList();
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>Null when the element can be moved by this tool, otherwise the reason.</summary>
    internal static SkipReason? WhyNotMovable(Element element, View view)
    {
        if (element is ElementType or View)
            return SkipReason.NotSupported;

        // Rooms/areas/spaces (moving the point does not move the boundary), dimensions,
        // levels, grids and reference planes (moving them changes the model).
        if (element is SpatialElement or Dimension or DatumPlane)
            return SkipReason.NotSupported;

        if (element.Pinned)
            return SkipReason.Pinned;

        if (element.GroupId != ElementId.InvalidElementId)
            return SkipReason.InGroup;

        if (element is FamilyInstance { SuperComponent: not null })
            return SkipReason.Nested;

        if (element.ViewSpecific && element.OwnerViewId != view.Id)
            return SkipReason.NotVisible;

        if (element.get_BoundingBox(view) is null)
            return SkipReason.NotVisible;

        return null;
    }

    /// <summary>Projects the 8 corners of the element's box in the view onto <paramref name="axis"/>.</summary>
    private static bool TryGetExtent(Element element, View view, XYZ axis, int index, out AlignExtent extent)
    {
        extent = default;

        var box = element.get_BoundingBox(view);
        if (box is null || !box.Enabled)
            return false;

        var transform = box.Transform ?? Transform.Identity;
        var min = double.MaxValue;
        var max = double.MinValue;

        foreach (var x in new[] { box.Min.X, box.Max.X })
        foreach (var y in new[] { box.Min.Y, box.Max.Y })
        foreach (var z in new[] { box.Min.Z, box.Max.Z })
        {
            var d = transform.OfPoint(new XYZ(x, y, z)).DotProduct(axis);
            min = Math.Min(min, d);
            max = Math.Max(max, d);
        }

        extent = new AlignExtent(index, min, max);
        return true;
    }

    private static void Move(Document doc, Element element, XYZ translation)
    {
        // Viewports on sheets are moved through their box center.
        if (element is Viewport viewport)
        {
            viewport.SetBoxCenter(viewport.GetBoxCenter().Add(translation));
            return;
        }

        ElementTransformUtils.MoveElement(doc, element.Id, translation);
    }

    private sealed class MovableElementFilter : ISelectionFilter
    {
        private readonly View _view;

        public MovableElementFilter(View view) => _view = view;

        public bool AllowElement(Element elem) => WhyNotMovable(elem, _view) is null;

        public bool AllowReference(Reference reference, XYZ position) => false;
    }
}

internal enum SkipReason
{
    Pinned,
    InGroup,
    Nested,
    NotSupported,
    NotVisible,
    MovesWithHost,
    MoveFailed,
}

/// <summary>Counts ignored elements by reason and writes the Portuguese summary.</summary>
internal sealed class SkipCounter
{
    private readonly Dictionary<SkipReason, int> _counts = new();

    public int Total => _counts.Values.Sum();

    public void Add(SkipReason reason, int count = 1) =>
        _counts[reason] = _counts.TryGetValue(reason, out var current) ? current + count : count;

    /// <summary>Empty when nothing was skipped; otherwise a paragraph starting with a blank line.</summary>
    public string Describe()
    {
        if (Total == 0)
            return string.Empty;

        var lines = _counts.Select(pair => $"• {pair.Value} {Text(pair.Key)}");
        return "\n\nNão foram movidos:\n" + string.Join("\n", lines);
    }

    private static string Text(SkipReason reason) => reason switch
    {
        SkipReason.Pinned => "fixado(s) com pino — desafixe para mover",
        SkipReason.InGroup => "dentro de grupo — edite o grupo para mover",
        SkipReason.Nested => "família(s) aninhada(s) dentro de outra família",
        SkipReason.NotSupported => "de tipo que não se alinha (ambientes, cotas, níveis, eixos, planos de referência)",
        SkipReason.NotVisible => "sem posição nesta vista",
        SkipReason.MovesWithHost => "porta(s)/janela(s) que acompanham a parede selecionada",
        SkipReason.MoveFailed => "recusado(s) pelo Revit (veja o log)",
        _ => reason.ToString(),
    };
}
