namespace UnSetBim.Modules.Architectural.Align;

/// <summary>Extent of one element along the movement axis, in feet (view coordinates).</summary>
public readonly record struct AlignExtent(int Index, double Min, double Max)
{
    public double Center => (Min + Max) / 2;

    public double Size => Max - Min;
}

/// <summary>
/// Pure math of the Align tool (no Revit API): how far each element must move along the axis.
/// "Min" is the left/bottom side, "Max" the right/top side.
/// </summary>
public static class AlignPlanner
{
    /// <summary>Returns the offset of each extent, keyed by <see cref="AlignExtent.Index"/>.</summary>
    public static Dictionary<int, double> Plan(AlignMode mode, IReadOnlyList<AlignExtent> items)
    {
        var offsets = new Dictionary<int, double>();
        if (items.Count == 0)
            return offsets;

        var min = items.Min(i => i.Min);
        var max = items.Max(i => i.Max);

        switch (mode)
        {
            case AlignMode.Left:
            case AlignMode.Bottom:
                foreach (var item in items)
                    offsets[item.Index] = min - item.Min;
                break;

            case AlignMode.Right:
            case AlignMode.Top:
                foreach (var item in items)
                    offsets[item.Index] = max - item.Max;
                break;

            case AlignMode.CenterHorizontal:
            case AlignMode.CenterVertical:
                var center = (min + max) / 2;
                foreach (var item in items)
                    offsets[item.Index] = center - item.Center;
                break;

            case AlignMode.DistributeHorizontal:
            case AlignMode.DistributeVertical:
                PlanDistribute(items, offsets);
                break;
        }

        return offsets;
    }

    /// <summary>
    /// Equal gaps between neighbours. The first and the last element (by position) stay put.
    /// When the elements overlap more than the span allows, the gap is negative and they keep overlapping evenly.
    /// </summary>
    private static void PlanDistribute(IReadOnlyList<AlignExtent> items, Dictionary<int, double> offsets)
    {
        if (items.Count < 3)
            return;

        var sorted = items.OrderBy(i => i.Center).ThenBy(i => i.Index).ToList();
        var first = sorted[0];
        var last = sorted[^1];

        var sumOfSizes = sorted.Sum(i => i.Size);
        var span = last.Max - first.Min;
        var gap = (span - sumOfSizes) / (sorted.Count - 1);

        offsets[first.Index] = 0;
        offsets[last.Index] = 0;

        var cursor = first.Max + gap;
        for (var k = 1; k < sorted.Count - 1; k++)
        {
            var item = sorted[k];
            offsets[item.Index] = cursor - item.Min;
            cursor += item.Size + gap;
        }
    }
}
