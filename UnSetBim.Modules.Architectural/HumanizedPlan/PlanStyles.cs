namespace UnSetBim.Modules.Architectural.HumanizedPlan;

/// <summary>A rendering style the user can pick. UI texts in Portuguese, prompt fragments in both languages.</summary>
public sealed record PlanStyle(
    string Id,
    string Name,
    string Description,
    string FullEn,
    string FullPt,
    string KeywordsEn);

public static class PlanStyles
{
    public const string DefaultId = "realistic";

    public static IReadOnlyList<PlanStyle> All { get; } = new[]
    {
        new PlanStyle(
            "realistic",
            "Realista",
            "Fotorrealista: texturas de verdade, luz natural suave e sombras.",
            "photorealistic rendered floor plan with real material textures, soft natural daylight coming from the top-left and gentle cast shadows under walls and furniture",
            "planta humanizada fotorrealista, com texturas reais dos materiais, luz natural suave vindo do canto superior esquerdo e sombras leves sob paredes e móveis",
            "photorealistic, realistic material textures, soft natural daylight, soft cast shadows, high detail"),

        new PlanStyle(
            "real_estate",
            "Imobiliária",
            "Cores vivas e limpas, no estilo de folder de lançamento.",
            "clean real-estate marketing floor plan, bright and saturated colors, crisp edges, soft drop shadows, brochure style",
            "planta humanizada de folder imobiliário, cores vivas e limpas, bordas nítidas, sombras suaves, estilo de material de vendas",
            "real estate brochure floor plan, bright saturated colors, crisp clean edges, soft drop shadows"),

        new PlanStyle(
            "minimal",
            "Minimalista",
            "Tons claros e neutros, poucos detalhes, visual elegante.",
            "minimalist floor plan illustration with a light neutral palette (white, beige, light gray), flat colors with subtle textures, elegant and airy",
            "ilustração minimalista de planta, paleta clara e neutra (branco, bege, cinza claro), cores chapadas com texturas sutis, visual elegante e leve",
            "minimalist floor plan, light neutral palette, white beige light gray, flat colors, subtle textures, elegant"),

        new PlanStyle(
            "watercolor",
            "Aquarela",
            "Pintura em aquarela com contorno a nanquim e tons suaves.",
            "hand-painted watercolor floor plan with soft washes of color, visible paper texture and delicate ink outlines",
            "planta pintada à mão em aquarela, manchas suaves de cor, textura de papel aparente e contornos delicados a nanquim",
            "watercolor floor plan, soft color washes, paper texture, delicate ink outlines, hand painted"),

        new PlanStyle(
            "sketch",
            "Croqui",
            "Desenho à mão com lápis e marcador, estilo de estudo.",
            "hand-drawn architectural sketch floor plan, pencil and marker rendering, warm paper background, loose hatching",
            "croqui de planta desenhado à mão, lápis e marcador, fundo de papel em tom quente, hachuras soltas",
            "hand drawn architectural sketch, pencil and marker, warm paper background, loose hatching"),
    };

    public static PlanStyle Get(string? id) => All.FirstOrDefault(s => s.Id == id) ?? All[0];
}
