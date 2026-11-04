using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using UnSetBim.Modules.Architectural.Commands;

namespace UnSetBim.Modules.Architectural.Align;

// One command class per ribbon item (Revit binds a button to a class name).

/// <summary>Shared body of the Align commands.</summary>
public abstract class AlignCommandBase : ArchitecturalCommand
{
    protected abstract AlignMode Mode { get; }

    protected override string FeatureName => "O Alinhar";

    protected override Result Run(UIApplication app) => AlignTool.Run(app, AlignModes.Get(Mode));
}

[Transaction(TransactionMode.Manual)]
public sealed class AlignLeftCommand : AlignCommandBase
{
    protected override AlignMode Mode => AlignMode.Left;
}

[Transaction(TransactionMode.Manual)]
public sealed class AlignRightCommand : AlignCommandBase
{
    protected override AlignMode Mode => AlignMode.Right;
}

[Transaction(TransactionMode.Manual)]
public sealed class AlignTopCommand : AlignCommandBase
{
    protected override AlignMode Mode => AlignMode.Top;
}

[Transaction(TransactionMode.Manual)]
public sealed class AlignBottomCommand : AlignCommandBase
{
    protected override AlignMode Mode => AlignMode.Bottom;
}

[Transaction(TransactionMode.Manual)]
public sealed class CenterHorizontalCommand : AlignCommandBase
{
    protected override AlignMode Mode => AlignMode.CenterHorizontal;
}

[Transaction(TransactionMode.Manual)]
public sealed class CenterVerticalCommand : AlignCommandBase
{
    protected override AlignMode Mode => AlignMode.CenterVertical;
}

[Transaction(TransactionMode.Manual)]
public sealed class DistributeHorizontalCommand : AlignCommandBase
{
    protected override AlignMode Mode => AlignMode.DistributeHorizontal;
}

[Transaction(TransactionMode.Manual)]
public sealed class DistributeVerticalCommand : AlignCommandBase
{
    protected override AlignMode Mode => AlignMode.DistributeVertical;
}
