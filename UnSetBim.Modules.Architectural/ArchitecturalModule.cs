using Autodesk.Revit.UI;
using UnSetBim.Framework.Modules;
using UnSetBim.Modules.Architectural.Align;
using UnSetBim.Modules.Architectural.HumanizedPlan;

namespace UnSetBim.Modules.Architectural;

/// <summary>
/// Architectural module: one ribbon panel ("Arquitetura") with one button per tool.
/// Every command derives from <see cref="Commands.ArchitecturalCommand"/>, which requires the
/// "architectural" plan module.
///
/// Tools are added one at a time, following the roadmap (phase 1: Align, AI floor plan,
/// views per room, tags, parameters).
/// </summary>
public sealed class ArchitecturalModule : IUnSetModule
{
    internal const string IconBase = "pack://application:,,,/UnSetBim.Modules.Architectural;component/Resources/";

    public string Id => "architectural";

    public string DisplayName => "Arquitetura";

    public string? RequiredPlanModule => PlanModules.Architectural;

    public void OnStartup(ModuleContext context)
    {
        var panel = context.CreatePanel("Arquitetura");
        AddAlignButton(panel);

        panel.AddItem(ModuleContext.CreateButton<HumanizedPlanCommand>(
            name: "UnSetBim.Arch.HumanizedPlan",
            text: "Planta\nHumanizada IA",
            tooltip: "Exporta a planta ativa como imagem limpa em alta resolução e gera o prompt para ChatGPT, Gemini, Leonardo e outras IAs de imagem transformarem em planta humanizada. Sua vista não é alterada.",
            largeIcon: IconBase + "humanized_plan_32.png",
            smallIcon: IconBase + "humanized_plan_16.png"));
    }

    public void OnShutdown()
    {
    }

    /// <summary>Split button: the visible item follows the last option used.</summary>
    private static void AddAlignButton(RibbonPanel panel)
    {
        var split = (SplitButton)panel.AddItem(new SplitButtonData("UnSetBim.Arch.Align", "Alinhar"));
        split.IsSynchronizedWithCurrentItem = true;

        foreach (var mode in AlignModes.All)
        {
            var info = AlignModes.Get(mode);
            split.AddPushButton(info.CreateButtonData(IconBase));
        }
    }
}
