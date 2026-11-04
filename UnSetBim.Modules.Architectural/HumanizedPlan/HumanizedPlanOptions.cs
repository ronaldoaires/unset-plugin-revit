namespace UnSetBim.Modules.Architectural.HumanizedPlan;

public enum PromptLanguage
{
    Portuguese,
    English,
}

/// <summary>What the user chose in the options window.</summary>
public sealed record HumanizedPlanOptions(
    string StyleId,
    PromptLanguage Language,
    int ImageWidth,
    bool Furnish,
    string Notes)
{
    public static readonly int[] Widths = { 4000, 6000, 8000 };

    public static HumanizedPlanOptions Default { get; } = new(PlanStyles.DefaultId, PromptLanguage.English, 6000, true, string.Empty);
}

/// <summary>The three texts shown in the result window.</summary>
public sealed record HumanizedPlanPrompts(string Full, string Short, string Negative);
