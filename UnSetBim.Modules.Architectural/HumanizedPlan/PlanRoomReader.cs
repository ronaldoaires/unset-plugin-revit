using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace UnSetBim.Modules.Architectural.HumanizedPlan;

/// <summary>Reads the placed rooms shown in a plan view (same level, inside the crop region when it is active).</summary>
internal static class PlanRoomReader
{
    public static List<PlanRoom> Read(Document doc, ViewPlan view)
    {
        var levelId = view.GenLevel?.Id;
        var crop = view.CropBoxActive ? view.CropBox : null;
        var toView = crop?.Transform.Inverse;

        var result = new List<PlanRoom>();

        var rooms = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_Rooms)
            .WhereElementIsNotElementType()
            .OfType<Room>();

        foreach (var room in rooms)
        {
            if (room.Area <= 0)
                continue; // not placed, not enclosed or redundant

            if (levelId is not null && room.LevelId != levelId)
                continue;

            if (crop is not null && toView is not null && room.Location is LocationPoint point)
            {
                var p = toView.OfPoint(point.Point);
                if (p.X < crop.Min.X || p.X > crop.Max.X || p.Y < crop.Min.Y || p.Y > crop.Max.Y)
                    continue;
            }

            var name = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString();
            if (string.IsNullOrWhiteSpace(name))
                name = "Ambiente";

            var finish = room.get_Parameter(BuiltInParameter.ROOM_FINISH_FLOOR)?.AsString();
            var area = UnitUtils.ConvertFromInternalUnits(room.Area, UnitTypeId.SquareMeters);

            result.Add(new PlanRoom(name.Trim(), area, string.IsNullOrWhiteSpace(finish) ? null : finish.Trim()));
        }

        return result;
    }
}
