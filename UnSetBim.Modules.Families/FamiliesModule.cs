using UnSetBim.Framework.Modules;
using UnSetBim.Modules.Families.Commands;
using UnSetBim.Modules.Families.Views;

namespace UnSetBim.Modules.Families;

/// <summary>Families module: browse the UnSet BIM library and insert families into the project.</summary>
public sealed class FamiliesModule : IUnSetModule
{
    private const string IconBase = "pack://application:,,,/UnSetBim.Modules.Families;component/Resources/";

    public string Id => "families";

    public string DisplayName => "Famílias";

    // Families are never gated by plan modules: the server decides each download.
    public string? RequiredPlanModule => null;

    public void OnStartup(ModuleContext context)
    {
        var panel = context.CreatePanel("Famílias");

        panel.AddItem(ModuleContext.CreateButton<OpenCatalogCommand>(
            name: "UnSetBim.Families.Open",
            text: "Biblioteca\nde Famílias",
            tooltip: "Pesquise, favorite e insira famílias do UnSet BIM no projeto.",
            largeIcon: IconBase + "families_32.png",
            smallIcon: IconBase + "families_16.png"));
    }

    public void OnShutdown() => CatalogWindow.CloseIfOpen();
}
