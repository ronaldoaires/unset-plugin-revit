using UnSetBim.Framework.Modules;
using UnSetBim.Modules.Materials.Commands;
using UnSetBim.Modules.Materials.Views;

namespace UnSetBim.Modules.Materials;

/// <summary>
/// Materials module: panel "Materiais" with the textures dockable pane. Kept out of the
/// "Arquitetura" panel on purpose so it does not look tied to the architectural plan.
/// </summary>
public sealed class MaterialsModule : IUnSetModule
{
    internal const string IconBase = "pack://application:,,,/UnSetBim.Modules.Materials;component/Resources/";

    public string Id => "materials";

    public string DisplayName => "Materiais";

    // Free textures for everyone, the others for any active plan: the server decides each one.
    public string? RequiredPlanModule => null;

    public void OnStartup(ModuleContext context)
    {
        // Dockable panes can only be registered here, during Revit startup.
        context.Application.RegisterDockablePane(TexturesPane.PaneId, "UnSet BIM — Texturas", new TexturesPaneProvider());

        var panel = context.CreatePanel("Materiais");

        panel.AddItem(ModuleContext.CreateButton<OpenTexturesCommand>(
            name: "UnSetBim.Materials.Textures",
            text: "Texturas",
            tooltip: "Abre o painel de texturas do UnSet BIM (ao lado de Propriedades): crie materiais no tamanho real, pinte faces, ajuste e crie variações.",
            largeIcon: IconBase + "textures_32.png",
            smallIcon: IconBase + "textures_16.png"));
    }

    public void OnShutdown() => TexturesPane.Shutdown();
}
