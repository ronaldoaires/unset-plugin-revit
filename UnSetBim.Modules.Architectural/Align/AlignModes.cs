using Autodesk.Revit.UI;
using UnSetBim.Framework.Commands;
using UnSetBim.Framework.Modules;

namespace UnSetBim.Modules.Architectural.Align;

/// <summary>What the Align tool does. "Horizontal" means moving along the view's right direction.</summary>
public enum AlignMode
{
    Left,
    Right,
    Top,
    Bottom,
    CenterHorizontal,
    CenterVertical,
    DistributeHorizontal,
    DistributeVertical,
}

/// <summary>Texts, icon and command of one Align option.</summary>
public sealed class AlignModeInfo
{
    private readonly Func<string, PushButtonData> _createButton;

    private AlignModeInfo(AlignMode mode, string title, Func<string, PushButtonData> createButton)
    {
        Mode = mode;
        Title = title;
        _createButton = createButton;
    }

    public AlignMode Mode { get; }

    /// <summary>Portuguese name used in prompts, dialogs and the undo list.</summary>
    public string Title { get; }

    /// <summary>True when elements move along the view's right direction.</summary>
    public bool MovesHorizontally => Mode is AlignMode.Left or AlignMode.Right or AlignMode.CenterHorizontal or AlignMode.DistributeHorizontal;

    public bool IsDistribute => Mode is AlignMode.DistributeHorizontal or AlignMode.DistributeVertical;

    /// <summary>Elements needed: 2 to align, 3 to distribute.</summary>
    public int MinimumCount => IsDistribute ? 3 : 2;

    public PushButtonData CreateButtonData(string iconBase) => _createButton(iconBase);

    internal static AlignModeInfo Create<TCommand>(AlignMode mode, string title, string buttonText, string tooltip, string icon)
        where TCommand : UnSetCommand =>
        new(mode, title, iconBase => ModuleContext.CreateButton<TCommand>(
            name: "UnSetBim.Arch.Align." + mode,
            text: buttonText,
            tooltip: tooltip,
            largeIcon: iconBase + icon + "_32.png",
            smallIcon: iconBase + icon + "_16.png"));
}

public static class AlignModes
{
    private const string SelectionHint =
        " Selecione os elementos antes de clicar ou escolha-os depois na vista. Funciona em plantas, cortes, elevações, vistas de desenho, legendas e folhas.";

    private static readonly Dictionary<AlignMode, AlignModeInfo> Items = new()
    {
        [AlignMode.Left] = AlignModeInfo.Create<AlignLeftCommand>(
            AlignMode.Left, "Alinhar à esquerda", "Alinhar à\nesquerda",
            "Encosta todos os elementos na borda esquerda do elemento mais à esquerda." + SelectionHint,
            "align_left"),

        [AlignMode.Right] = AlignModeInfo.Create<AlignRightCommand>(
            AlignMode.Right, "Alinhar à direita", "Alinhar à\ndireita",
            "Encosta todos os elementos na borda direita do elemento mais à direita." + SelectionHint,
            "align_right"),

        [AlignMode.Top] = AlignModeInfo.Create<AlignTopCommand>(
            AlignMode.Top, "Alinhar em cima", "Alinhar\nem cima",
            "Encosta todos os elementos na borda de cima do elemento mais alto na vista." + SelectionHint,
            "align_top"),

        [AlignMode.Bottom] = AlignModeInfo.Create<AlignBottomCommand>(
            AlignMode.Bottom, "Alinhar embaixo", "Alinhar\nembaixo",
            "Encosta todos os elementos na borda de baixo do elemento mais baixo na vista." + SelectionHint,
            "align_bottom"),

        [AlignMode.CenterHorizontal] = AlignModeInfo.Create<CenterHorizontalCommand>(
            AlignMode.CenterHorizontal, "Centralizar na horizontal", "Centralizar na\nhorizontal",
            "Coloca o centro de todos os elementos numa mesma linha vertical (move só para os lados)." + SelectionHint,
            "center_horizontal"),

        [AlignMode.CenterVertical] = AlignModeInfo.Create<CenterVerticalCommand>(
            AlignMode.CenterVertical, "Centralizar na vertical", "Centralizar na\nvertical",
            "Coloca o centro de todos os elementos numa mesma linha horizontal (move só para cima e para baixo)." + SelectionHint,
            "center_vertical"),

        [AlignMode.DistributeHorizontal] = AlignModeInfo.Create<DistributeHorizontalCommand>(
            AlignMode.DistributeHorizontal, "Distribuir na horizontal", "Distribuir na\nhorizontal",
            "Deixa o mesmo espaço entre os elementos, da esquerda para a direita. Os dois das pontas não se movem. Mínimo de 3 elementos." + SelectionHint,
            "distribute_horizontal"),

        [AlignMode.DistributeVertical] = AlignModeInfo.Create<DistributeVerticalCommand>(
            AlignMode.DistributeVertical, "Distribuir na vertical", "Distribuir na\nvertical",
            "Deixa o mesmo espaço entre os elementos, de baixo para cima. Os dois das pontas não se movem. Mínimo de 3 elementos." + SelectionHint,
            "distribute_vertical"),
    };

    /// <summary>Ribbon order.</summary>
    public static IReadOnlyList<AlignMode> All { get; } = new[]
    {
        AlignMode.Left, AlignMode.Right, AlignMode.Top, AlignMode.Bottom,
        AlignMode.CenterHorizontal, AlignMode.CenterVertical,
        AlignMode.DistributeHorizontal, AlignMode.DistributeVertical,
    };

    public static AlignModeInfo Get(AlignMode mode) => Items[mode];
}
