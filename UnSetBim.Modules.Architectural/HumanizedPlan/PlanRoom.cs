using System.Globalization;
using System.Text;

namespace UnSetBim.Modules.Architectural.HumanizedPlan;

/// <summary>A room read from the model: name, area in m² and the floor finish typed in the room (may be empty).</summary>
public sealed record PlanRoom(string Name, double AreaM2, string? FloorFinish);

/// <summary>Floor material guessed from the room name (Portuguese names), used when the room has no floor finish.</summary>
internal static class FloorGuess
{
    private sealed record Rule(string[] Keys, string En, string Pt);

    // Checked in order: bathrooms before "suite", service areas before "area", etc.
    private static readonly Rule[] Rules =
    {
        new(new[] { "banh", "wc", "lavabo", "sanit" }, "light porcelain tiles", "porcelanato claro"),
        new(new[] { "cozinha", "copa", "gourmet" }, "light porcelain tiles", "porcelanato claro"),
        new(new[] { "servico", "lavanderia", "despensa", "deposito" }, "ceramic tiles", "piso cerâmico"),
        new(new[] { "piscina" }, "blue pool water with a stone edge", "água azul de piscina com borda de pedra"),
        new(new[] { "varanda", "sacada", "terraco", "deck", "pergolado" }, "wooden deck", "deck de madeira"),
        new(new[] { "garagem", "abrigo", "estacionamento" }, "concrete floor", "piso de concreto"),
        new(new[] { "jardim", "quintal", "gramado", "canteiro" }, "green lawn and plants", "grama e plantas"),
        new(new[] { "quarto", "dorm", "suite", "escritorio", "office", "closet", "estudo" }, "warm wooden floor", "piso de madeira"),
        new(new[] { "sala", "estar", "jantar", "living", "tv", "home" }, "light wood-look floor", "piso amadeirado claro"),
        new(new[] { "circula", "corredor", "hall", "escada", "entrada" }, "light porcelain tiles", "porcelanato claro"),
    };

    public static string? For(string roomName, bool english)
    {
        var key = Simplify(roomName);
        foreach (var rule in Rules)
        {
            if (rule.Keys.Any(k => key.Contains(k)))
                return english ? rule.En : rule.Pt;
        }

        return null;
    }

    /// <summary>Lower case without accents ("Área de Serviço" -> "area de servico").</summary>
    internal static string Simplify(string text)
    {
        var normalized = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                builder.Append(c);
        }

        return builder.ToString();
    }
}
