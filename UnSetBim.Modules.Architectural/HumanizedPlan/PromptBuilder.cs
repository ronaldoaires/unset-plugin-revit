using System.Globalization;
using System.Text;

namespace UnSetBim.Modules.Architectural.HumanizedPlan;

/// <summary>
/// Builds prompts that work across image AIs:
///  - Full: natural-language instructions for chat models that edit an attached image (ChatGPT, Gemini, Copilot...).
///    Follows the language picked by the user.
///  - Short + Negative: comma-separated keywords for diffusion tools that use the image as structure guidance
///    (Leonardo, Midjourney, Firefly, Stable Diffusion). Always English: those tools follow English best.
/// </summary>
public static class PromptBuilder
{
    /// <summary>Rooms listed in the full prompt (biggest first); the rest are summarized.</summary>
    private const int MaxRoomsFull = 30;

    /// <summary>Keeps the short prompt under the limit of tools like Leonardo (about 1,500 characters).</summary>
    private const int MaxShortLength = 900;

    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    public static HumanizedPlanPrompts Build(IReadOnlyList<PlanRoom> rooms, HumanizedPlanOptions options)
    {
        var style = PlanStyles.Get(options.StyleId);
        var ordered = rooms.OrderByDescending(r => r.AreaM2).ToList();

        var full = options.Language == PromptLanguage.English
            ? BuildFullEnglish(style, ordered, options)
            : BuildFullPortuguese(style, ordered, options);

        return new HumanizedPlanPrompts(full, BuildShort(style, ordered, options), Negative);
    }

    private const string Negative =
        "perspective, 3d view, tilted camera, isometric, curved doors, curved door panels, door swing arcs, " +
        "extra walls, new walls, closed garage, walls on lot boundary, sidewalk turned into wall, missing walls, missing doors, " +
        "moved furniture, wooden floor under door swing, objects in garage, " +
        "people, text, letters, numbers, labels, dimensions, watermark, logo, signature, distorted walls, extra rooms, " +
        "blurry, low quality, noise, cropped";

    private static string BuildFullEnglish(PlanStyle style, IReadOnlyList<PlanRoom> rooms, HumanizedPlanOptions options)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Edit the attached architectural floor plan and turn it into a {style.FullEn}.");
        sb.AppendLine("Use the attached image as an exact layout guide: only add colors, textures, furniture finishes, light and shadows.");
        sb.AppendLine();
        sb.AppendLine("How to read the drawing (CAD conventions):");
        sb.AppendLine("- SOLID BLACK shapes are walls and columns. These are the only walls: keep each one exactly in place, shape and thickness. The black fill is only a drawing convention: render the top of the walls in a clean finish that suits the style.");
        sb.AppendLine("- Thin lines are NOT walls: they are floor edges, steps, counters, lot limits or sidewalks. Never turn a thin line into a wall.");
        sb.AppendLine("- Where the drawing has no black wall, the space is OPEN: do not close garages, yards, porches or the back of the lot with new walls.");
        sb.AppendLine("- A thin quarter-circle arc next to an opening is a DOOR SWING SYMBOL, not an object: draw a normal flat, straight door leaf in that opening and remove the arc. Doors are never curved.");
        sb.AppendLine("- Thin parallel lines inside a wall are WINDOWS: draw glass/window frames there. Wavy lines along a window are curtains.");
        sb.AppendLine("- GRAY or HATCHED bands and areas outside the black walls are paving: sidewalks around the house, driveway and paths. They are flat floor, never walls.");
        sb.AppendLine("- Grid lines on the floor are tile joints; the green area is lawn.");
        sb.AppendLine("- Furniture and fixtures already drawn (beds, sofas, tables, toilets, sinks, counters) must stay in the same position, size and orientation.");
        sb.AppendLine();
        sb.AppendLine("Keep exactly:");
        sb.AppendLine("- the same top-down orthographic view: no perspective, no camera tilt;");
        sb.AppendLine("- the same proportions, framing and aspect ratio as the input image.");
        sb.AppendLine("Do not add or remove walls, rooms, doors or windows. Do not write any text, labels, numbers or dimensions in the image.");
        sb.AppendLine();

        if (rooms.Count > 0)
        {
            sb.AppendLine("Rooms (name, area, floor):");
            foreach (var room in rooms.Take(MaxRoomsFull))
            {
                var floor = room.FloorFinish ?? FloorGuess.For(room.Name, english: true);
                var area = room.AreaM2.ToString("0.#", En);
                sb.AppendLine(floor is null ? $"- {room.Name}, {area} m²" : $"- {room.Name}, {area} m², {floor}");
            }

            if (rooms.Count > MaxRoomsFull)
                sb.AppendLine($"- and {rooms.Count - MaxRoomsFull} smaller rooms: choose floors that match their use.");
            sb.AppendLine("Room names are in Portuguese; use them only to understand each space, never write them in the image.");
        }
        else
        {
            sb.AppendLine("Identify each room by its shape, doors and fixtures, and choose floors that match its use.");
        }

        sb.AppendLine();
        sb.AppendLine(options.Furnish
            ? "Rooms that are empty in the drawing may receive furniture that matches their use and size, seen from above at a realistic scale, without blocking doors."
            : "Keep only the furniture already drawn; do not add new furniture.");
        sb.AppendLine("Garage, driveway, sidewalks and yards: keep them clean; do not add tools, shelves, bikes or other objects there. Only plants and trees on the lawn.");

        if (!string.IsNullOrWhiteSpace(options.Notes))
        {
            sb.AppendLine();
            sb.AppendLine("Additional notes: " + options.Notes.Trim());
        }

        sb.AppendLine();
        sb.Append("Output: one high-resolution image with the same aspect ratio as the input.");
        return sb.ToString();
    }

    private static string BuildFullPortuguese(PlanStyle style, IReadOnlyList<PlanRoom> rooms, HumanizedPlanOptions options)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Edite a planta baixa em anexo e transforme-a em uma {style.FullPt}.");
        sb.AppendLine("Use a imagem enviada como guia exato do layout: apenas acrescente cores, texturas, acabamentos, luz e sombras.");
        sb.AppendLine();
        sb.AppendLine("Como ler o desenho (convenções de projeto):");
        sb.AppendLine("- Formas em PRETO SÓLIDO são paredes e pilares. Só elas são paredes: mantenha cada uma no mesmo lugar, formato e espessura. O preto é só convenção de desenho: renderize o topo das paredes com acabamento limpo, de acordo com o estilo.");
        sb.AppendLine("- Linhas finas NÃO são paredes: são bordas de piso, degraus, bancadas, limites do lote ou calçadas. Nunca transforme linha fina em parede.");
        sb.AppendLine("- Onde o desenho não tem parede preta, o espaço é ABERTO: não feche garagem, quintal, varanda nem o fundo do lote com paredes novas.");
        sb.AppendLine("- Um arco fino de um quarto de círculo junto a um vão é o SÍMBOLO DE ABERTURA DA PORTA, não um objeto: desenhe uma folha de porta reta e normal nesse vão e remova o arco. Portas nunca são curvas.");
        sb.AppendLine("- Linhas finas paralelas dentro da parede são JANELAS: desenhe vidro e caixilho nesse ponto. Linhas onduladas junto à janela são cortinas.");
        sb.AppendLine("- Faixas e áreas CINZA ou HACHURADAS fora das paredes pretas são pavimento: calçada em volta da casa, garagem e caminhos. São piso plano, nunca parede.");
        sb.AppendLine("- Linhas em grade no piso são rejuntes; a área verde é grama.");
        sb.AppendLine("- Móveis e louças já desenhados (camas, sofás, mesas, vasos sanitários, pias, bancadas) devem ficar na mesma posição, tamanho e direção.");
        sb.AppendLine();
        sb.AppendLine("Mantenha exatamente:");
        sb.AppendLine("- a mesma vista de cima, ortogonal: sem perspectiva e sem inclinar a câmera;");
        sb.AppendLine("- as mesmas proporções, o mesmo enquadramento e o mesmo formato da imagem enviada.");
        sb.AppendLine("Não crie nem remova paredes, ambientes, portas ou janelas. Não escreva textos, nomes, números ou cotas na imagem.");
        sb.AppendLine();

        if (rooms.Count > 0)
        {
            sb.AppendLine("Ambientes (nome, área, piso):");
            foreach (var room in rooms.Take(MaxRoomsFull))
            {
                var floor = room.FloorFinish ?? FloorGuess.For(room.Name, english: false);
                var area = room.AreaM2.ToString("0.#", Pt);
                sb.AppendLine(floor is null ? $"- {room.Name}, {area} m²" : $"- {room.Name}, {area} m², {floor}");
            }

            if (rooms.Count > MaxRoomsFull)
                sb.AppendLine($"- e mais {rooms.Count - MaxRoomsFull} ambientes menores: escolha pisos de acordo com o uso.");
            sb.AppendLine("Use os nomes só para entender cada espaço; não escreva os nomes na imagem.");
        }
        else
        {
            sb.AppendLine("Identifique cada ambiente pelo formato, pelas portas e pelas louças, e escolha o piso de acordo com o uso.");
        }

        sb.AppendLine();
        sb.AppendLine(options.Furnish
            ? "Ambientes vazios no desenho podem receber móveis de acordo com o uso e o tamanho, vistos de cima em escala real, sem bloquear portas."
            : "Mantenha apenas os móveis já desenhados; não acrescente móveis novos.");
        sb.AppendLine("Garagem, calçadas e quintal: mantenha limpos; não acrescente ferramentas, prateleiras, bicicletas ou outros objetos. Apenas plantas e árvores sobre a grama.");

        if (!string.IsNullOrWhiteSpace(options.Notes))
        {
            sb.AppendLine();
            sb.AppendLine("Observações: " + options.Notes.Trim());
        }

        sb.AppendLine();
        sb.Append("Resultado: uma única imagem em alta resolução, com a mesma proporção da imagem enviada.");
        return sb.ToString();
    }

    /// <summary>Keywords + floors grouped by material ("warm wooden floor in Quarto 1, Suíte").</summary>
    private static string BuildShort(PlanStyle style, IReadOnlyList<PlanRoom> rooms, HumanizedPlanOptions options)
    {
        var parts = new List<string>
        {
            "top-down orthographic architectural floor plan, exact same layout as the guide image",
            style.KeywordsEn,
            "solid black walls kept exactly, straight flat doors, open areas without walls stay open",
        };

        var byFloor = rooms
            .Select(r => (Room: r.Name, Floor: FloorGuess.For(r.Name, english: true)))
            .Where(r => r.Floor is not null)
            .GroupBy(r => r.Floor!)
            .Select(g => $"{g.Key} in {string.Join(", ", g.Select(x => x.Room).Distinct().Take(4))}");
        parts.AddRange(byFloor);

        if (options.Furnish)
            parts.Add("existing furniture kept in place, furniture seen from above at realistic scale");

        parts.Add("clean walls, sharp lines, high detail");

        var text = string.Join(", ", parts);
        if (text.Length > MaxShortLength)
        {
            var cut = text.LastIndexOf(',', MaxShortLength);
            text = text[..(cut > 0 ? cut : MaxShortLength)];
        }

        return text;
    }
}
