using System.IO;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using UnSetBim.Core.Diagnostics;
using UnSetBim.Core.Storage;
using UnSetBim.Framework.Dialogs;
using UnSetBim.Modules.Architectural.Commands;

namespace UnSetBim.Modules.Architectural.HumanizedPlan;

/// <summary>
/// Ribbon "Planta Humanizada IA": options → clean PNG of the active plan + prompts for image AIs → result window.
/// Files go to Pictures\UnSet BIM\Planta Humanizada (image + .txt with the prompts).
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class HumanizedPlanCommand : ArchitecturalCommand
{
    private const string PrefStyle = "humanized_plan.style";
    private const string PrefLanguage = "humanized_plan.language";
    private const string PrefWidth = "humanized_plan.width";
    private const string PrefFurnish = "humanized_plan.furnish";

    private static readonly HashSet<ViewType> SupportedViews = new()
    {
        ViewType.FloorPlan,
        ViewType.AreaPlan,
    };

    protected override string FeatureName => "A Planta Humanizada IA";

    protected override Result Run(UIApplication app)
    {
        var doc = app.ActiveUIDocument.Document;

        if (doc.ActiveView is not ViewPlan view || view.IsTemplate || !SupportedViews.Contains(view.ViewType))
        {
            MessageDialog.Show(
                "Abra uma planta de piso",
                "A Planta Humanizada IA trabalha com plantas de piso. Abra a planta que você quer humanizar e clique de novo.",
                DialogKind.Warning);
            return Result.Cancelled;
        }

        if (!view.CanViewBeDuplicated(ViewDuplicateOption.Duplicate))
        {
            MessageDialog.Show("Planta Humanizada IA", "Esta vista não pode ser exportada pelo plugin. Tente em outra planta.", DialogKind.Warning);
            return Result.Cancelled;
        }

        var rooms = PlanRoomReader.Read(doc, view);

        var options = HumanizedPlanOptionsWindow.Ask(view.Name, rooms.Count, LoadPreferences());
        if (options is null)
            return Result.Cancelled;

        SavePreferences(options);

        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "UnSet BIM", "Planta Humanizada");
        var fileName = SafeFileName($"{doc.Title} - {view.Name} - {DateTime.Now:yyyy-MM-dd HHmm}");

        string imagePath;
        try
        {
            imagePath = PlanImageExporter.Export(doc, view, options.ImageWidth, folder, fileName);
        }
        catch (Exception ex)
        {
            Log.Error("Humanized plan export failed.", ex);
            MessageDialog.Show(
                "Não foi possível exportar",
                "O Revit não conseguiu gerar a imagem desta planta. Sua vista não foi alterada. Detalhes foram gravados no log do UnSet BIM.",
                DialogKind.Error);
            return Result.Failed;
        }

        var prompts = PromptBuilder.Build(rooms, options);
        WritePromptFile(Path.ChangeExtension(imagePath, ".txt"), prompts);

        HumanizedPlanResultWindow.ShowFor(imagePath, prompts);
        return Result.Succeeded;
    }

    private static HumanizedPlanOptions LoadPreferences()
    {
        var prefs = PluginState.Load().ToolPreferences;
        var d = HumanizedPlanOptions.Default;

        return d with
        {
            StyleId = prefs.TryGetValue(PrefStyle, out var s) ? s : d.StyleId,
            Language = prefs.TryGetValue(PrefLanguage, out var l) && Enum.TryParse<PromptLanguage>(l, out var lang) ? lang : d.Language,
            ImageWidth = prefs.TryGetValue(PrefWidth, out var w) && int.TryParse(w, out var width) && HumanizedPlanOptions.Widths.Contains(width) ? width : d.ImageWidth,
            Furnish = prefs.TryGetValue(PrefFurnish, out var f) ? f == "1" : d.Furnish,
        };
    }

    /// <summary>Notes are not kept: they usually belong to one project.</summary>
    private static void SavePreferences(HumanizedPlanOptions options) =>
        PluginState.Update(state =>
        {
            state.ToolPreferences[PrefStyle] = options.StyleId;
            state.ToolPreferences[PrefLanguage] = options.Language.ToString();
            state.ToolPreferences[PrefWidth] = options.ImageWidth.ToString();
            state.ToolPreferences[PrefFurnish] = options.Furnish ? "1" : "0";
        });

    private static void WritePromptFile(string path, HumanizedPlanPrompts prompts)
    {
        try
        {
            var text = new StringBuilder()
                .AppendLine("=== PROMPT COMPLETO (Gemini, ChatGPT, Copilot) ===")
                .AppendLine(prompts.Full)
                .AppendLine()
                .AppendLine("=== PROMPT CURTO (Leonardo, Midjourney, Firefly) ===")
                .AppendLine(prompts.Short)
                .AppendLine()
                .AppendLine("=== PROMPT NEGATIVO ===")
                .AppendLine(prompts.Negative)
                .ToString();

            File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }
        catch (Exception ex)
        {
            Log.Warn("Humanized plan: could not write the prompt file.", ex);
        }
    }

    private static string SafeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');

        return name.Length > 150 ? name[..150] : name;
    }
}
