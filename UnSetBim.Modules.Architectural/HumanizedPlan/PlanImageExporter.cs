using System.IO;
using Autodesk.Revit.DB;
using UnSetBim.Core.Diagnostics;

namespace UnSetBim.Modules.Architectural.HumanizedPlan;

/// <summary>
/// Exports a clean PNG of a plan view for image AIs without touching the user's view:
/// inside a transaction group it duplicates the view (no annotations), hides what confuses the AI,
/// exports the copy and rolls the whole group back, so the model and the undo list stay as they were.
/// </summary>
internal static class PlanImageExporter
{
    /// <summary>Model categories hidden in the export (annotations are hidden all at once).</summary>
    private static readonly BuiltInCategory[] HiddenCategories =
    {
        BuiltInCategory.OST_Rooms,
        BuiltInCategory.OST_Areas,
        BuiltInCategory.OST_MEPSpaces,
        BuiltInCategory.OST_RoomSeparationLines,
        BuiltInCategory.OST_AreaSchemeLines,
        BuiltInCategory.OST_CLines,
        BuiltInCategory.OST_VolumeOfInterest,
        BuiltInCategory.OST_Grids,
        BuiltInCategory.OST_Levels,
        BuiltInCategory.OST_Sections,
        BuiltInCategory.OST_Elev,
        BuiltInCategory.OST_Callouts,
        BuiltInCategory.OST_Matchline,
        BuiltInCategory.OST_Cameras,
    };

    /// <summary>Returns the full path of the PNG written to <paramref name="folder"/>.</summary>
    public static string Export(Document doc, ViewPlan view, int pixelWidth, string folder, string fileName)
    {
        Directory.CreateDirectory(folder);

        var finalPath = Path.Combine(folder, fileName + ".png");
        var exportBase = Path.Combine(folder, "_unset_export_" + Guid.NewGuid().ToString("N")[..8]);
        var startedAt = DateTime.Now.AddSeconds(-2);

        using var group = new TransactionGroup(doc, "UnSet BIM: Planta Humanizada IA");
        group.Start();

        try
        {
            ElementId copyId;
            using (var transaction = new Transaction(doc, "UnSet BIM: preparar vista temporária"))
            {
                transaction.Start();
                copyId = view.Duplicate(ViewDuplicateOption.Duplicate);
                Prepare(doc, (View)doc.GetElement(copyId));
                transaction.Commit();
            }

            var options = new ImageExportOptions
            {
                ExportRange = ExportRange.SetOfViews,
                FilePath = exportBase,
                ZoomType = ZoomFitType.FitToPage,
                FitDirection = FitDirectionType.Horizontal,
                PixelSize = pixelWidth,
                ImageResolution = ImageResolution.DPI_300,
                HLRandWFViewsFileType = ImageFileType.PNG,
                ShadowViewsFileType = ImageFileType.PNG,
                ShouldCreateWebSite = false,
            };
            options.SetViewsAndSheets(new List<ElementId> { copyId });

            doc.ExportImage(options);
        }
        finally
        {
            // Always undo the temporary view, even when the export fails.
            group.RollBack();
        }

        // Revit appends the view type and name to FilePath ("<base> - Floor Plan - <name>.png").
        var written = Directory.GetFiles(folder, Path.GetFileName(exportBase) + "*.png")
            .Select(p => new FileInfo(p))
            .Where(f => f.LastWriteTime >= startedAt)
            .OrderByDescending(f => f.LastWriteTime)
            .FirstOrDefault();

        if (written is null)
            throw new InvalidOperationException("O Revit não gerou a imagem.");

        if (File.Exists(finalPath))
            File.Delete(finalPath);
        File.Move(written.FullName, finalPath);

        return finalPath;
    }

    private static void Prepare(Document doc, View copy)
    {
        // Without a template every setting below can be changed.
        if (copy.ViewTemplateId != ElementId.InvalidElementId)
            copy.ViewTemplateId = ElementId.InvalidElementId;

        TrySet(() => copy.AreAnnotationCategoriesHidden = true, "hide annotations");
        TrySet(() => copy.CropBoxVisible = false, "hide crop region");
        TrySet(() => copy.DetailLevel = ViewDetailLevel.Fine, "fine detail level");

        if (copy is ViewPlan plan)
            TrySet(() => plan.SetUnderlayBaseLevel(ElementId.InvalidElementId), "remove underlay");

        foreach (var bic in HiddenCategories)
        {
            var category = Category.GetCategory(doc, bic);
            if (category is not null && copy.CanCategoryBeHidden(category.Id))
                TrySet(() => copy.SetCategoryHidden(category.Id, true), "hide " + bic);
        }

        FillCutWalls(doc, copy);
        HideSwingSymbols(doc, copy);
    }

    /// <summary>Categories whose cut is painted solid black, so the AI tells walls from thin floor/lot lines.</summary>
    private static readonly BuiltInCategory[] SolidCutCategories =
    {
        BuiltInCategory.OST_Walls,
        BuiltInCategory.OST_StructuralColumns,
        BuiltInCategory.OST_Columns,
        BuiltInCategory.OST_CurtainWallPanels,
    };

    private static void FillCutWalls(Document doc, View copy)
    {
        var solid = new FilteredElementCollector(doc)
            .OfClass(typeof(FillPatternElement))
            .Cast<FillPatternElement>()
            .FirstOrDefault(p => p.GetFillPattern().IsSolidFill);

        if (solid is null)
        {
            Log.Warn("Humanized plan: no solid fill pattern found.");
            return;
        }

        var black = new Color(0, 0, 0);
        var overrides = new OverrideGraphicSettings()
            .SetCutForegroundPatternId(solid.Id)
            .SetCutForegroundPatternColor(black)
            .SetCutForegroundPatternVisible(true)
            .SetCutLineColor(black);

        foreach (var bic in SolidCutCategories)
        {
            var category = Category.GetCategory(doc, bic);
            if (category is not null)
                TrySet(() => copy.SetCategoryOverrides(category.Id, overrides), "fill " + bic);
        }
    }

    /// <summary>
    /// Door/window swing arcs are read by image AIs as curved panels. Hide the swing subcategories
    /// (English and Portuguese templates). Families that draw the arc in the main subcategory keep it;
    /// the prompt explains the symbol for those.
    /// </summary>
    private static readonly string[] SwingKeywords = { "swing", "abertura", "giro", "arco" };

    private static void HideSwingSymbols(Document doc, View copy)
    {
        foreach (var bic in new[] { BuiltInCategory.OST_Doors, BuiltInCategory.OST_Windows })
        {
            var category = Category.GetCategory(doc, bic);
            if (category is null)
                continue;

            var hidden = new List<string>();
            var kept = new List<string>();

            foreach (Category sub in category.SubCategories)
            {
                var name = FloorGuess.Simplify(sub.Name);
                if (SwingKeywords.Any(k => name.Contains(k)) && copy.CanCategoryBeHidden(sub.Id))
                {
                    TrySet(() => copy.SetCategoryHidden(sub.Id, true), "hide swing " + sub.Name);
                    hidden.Add(sub.Name);
                }
                else
                {
                    kept.Add(sub.Name);
                }
            }

            // Lets us see which subcategory names the user's families use when an arc still shows up.
            Log.Info($"Humanized plan: {bic} subcategories hidden [{string.Join(", ", hidden)}], kept [{string.Join(", ", kept)}].");
        }
    }

    private static void TrySet(Action action, string what)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Log.Warn($"Humanized plan: could not {what}.", ex);
        }
    }
}
