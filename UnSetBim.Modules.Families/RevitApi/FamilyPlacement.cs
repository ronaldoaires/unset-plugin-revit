using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using UnSetBim.Core.Diagnostics;

namespace UnSetBim.Modules.Families.RevitApi;

public enum PlacementOutcome
{
    /// <summary>Loaded and placement started (the user clicks in the view to place it).</summary>
    PlacementStarted,

    /// <summary>Loaded, but the type cannot be placed interactively (e.g. some annotation or system-hosted families).</summary>
    LoadedOnly,

    /// <summary>No project or family document is open.</summary>
    NoDocument,

    /// <summary>The user chose not to replace the family already in the project.</summary>
    Cancelled,

    Failed,
}

public sealed record PlacementResult(PlacementOutcome Outcome, string? Message = null);

/// <summary>
/// Loads a downloaded .rfa into the active document and starts placement.
/// Runs inside the Revit API context only (through the host dispatcher).
/// </summary>
internal static class FamilyPlacement
{
    public static PlacementResult LoadAndPlace(UIApplication app, string rfaPath, string familyName)
    {
        var uiDoc = app.ActiveUIDocument;
        var doc = uiDoc?.Document;

        if (uiDoc is null || doc is null)
            return new PlacementResult(PlacementOutcome.NoDocument);

        if (doc.IsReadOnly)
            return new PlacementResult(PlacementOutcome.Failed, "O documento ativo está somente leitura.");

        try
        {
            var existing = FindFamily(doc, familyName);
            Family? family = existing;

            if (existing is not null)
            {
                var choice = AskWhatToDoWithExisting(familyName);
                if (choice == ExistingChoice.Cancel)
                    return new PlacementResult(PlacementOutcome.Cancelled);

                if (choice == ExistingChoice.Replace)
                    family = Load(doc, rfaPath) ?? existing;
            }
            else
            {
                family = Load(doc, rfaPath);
            }

            if (family is null)
                return new PlacementResult(PlacementOutcome.Failed, "O Revit não conseguiu carregar a família.");

            var symbol = FirstSymbol(doc, family);
            if (symbol is null)
                return new PlacementResult(PlacementOutcome.LoadedOnly, "A família foi carregada, mas não possui tipos para inserir.");

            try
            {
                // Must be outside a transaction. Placement starts once this API call returns.
                uiDoc.PostRequestForElementTypePlacement(symbol);
                return new PlacementResult(PlacementOutcome.PlacementStarted);
            }
            catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.InvalidOperationException or Autodesk.Revit.Exceptions.ArgumentException)
            {
                Log.Info($"Type of '{familyName}' cannot be placed interactively: {ex.Message}");
                return new PlacementResult(PlacementOutcome.LoadedOnly,
                    "A família foi carregada no projeto. Este tipo não pode ser inserido automaticamente: use-o pelo Navegador de Projeto.");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to load family '{familyName}'.", ex);
            return new PlacementResult(PlacementOutcome.Failed, $"O Revit não conseguiu carregar a família: {ex.Message}");
        }
    }

    private static Family? Load(Document doc, string rfaPath)
    {
        using var transaction = new Transaction(doc, "UnSet BIM: carregar família");
        transaction.Start();

        var loaded = doc.LoadFamily(rfaPath, new ReplaceFamilyLoadOptions(), out Family family);
        if (!loaded && family is null)
        {
            transaction.RollBack();
            return null;
        }

        transaction.Commit();
        return family;
    }

    private static Family? FindFamily(Document doc, string familyName) =>
        new FilteredElementCollector(doc)
            .OfClass(typeof(Family))
            .Cast<Family>()
            .FirstOrDefault(f => string.Equals(f.Name, familyName, StringComparison.OrdinalIgnoreCase));

    private static FamilySymbol? FirstSymbol(Document doc, Family family)
    {
        var symbol = family.GetFamilySymbolIds()
            .Select(id => doc.GetElement(id))
            .OfType<FamilySymbol>()
            .OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
            .FirstOrDefault();

        if (symbol is null || symbol.IsActive)
            return symbol;

        using var transaction = new Transaction(doc, "UnSet BIM: ativar tipo");
        transaction.Start();
        symbol.Activate();
        doc.Regenerate();
        transaction.Commit();
        return symbol;
    }

    private enum ExistingChoice
    {
        Replace,
        UseExisting,
        Cancel,
    }

    private static ExistingChoice AskWhatToDoWithExisting(string familyName)
    {
        var dialog = new TaskDialog("UnSet BIM")
        {
            MainInstruction = "Esta família já está no projeto",
            MainContent = $"O projeto já tem uma família chamada \"{familyName}\".",
            CommonButtons = TaskDialogCommonButtons.Cancel,
            DefaultButton = TaskDialogResult.CommandLink1,
        };

        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
            "Atualizar com a versão do UnSet BIM",
            "Recarrega a família baixada. Os valores dos parâmetros dos tipos existentes são mantidos.");
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
            "Usar a família que já está no projeto",
            "Não altera o projeto e apenas inicia a inserção.");

        return dialog.Show() switch
        {
            TaskDialogResult.CommandLink1 => ExistingChoice.Replace,
            TaskDialogResult.CommandLink2 => ExistingChoice.UseExisting,
            _ => ExistingChoice.Cancel,
        };
    }

    /// <summary>Reload an existing family without overwriting parameter values of existing types.</summary>
    private sealed class ReplaceFamilyLoadOptions : IFamilyLoadOptions
    {
        public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
        {
            overwriteParameterValues = false;
            return true;
        }

        public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
        {
            source = FamilySource.Family;
            overwriteParameterValues = false;
            return true;
        }
    }
}
