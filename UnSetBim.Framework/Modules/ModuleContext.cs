using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;
using UnSetBim.Core.Diagnostics;
using UnSetBim.Framework.Commands;
using UnSetBim.Framework.Hosting;

namespace UnSetBim.Framework.Modules;

/// <summary>What a module receives on startup: the Revit application, the shared tab and the host services.</summary>
public sealed class ModuleContext
{
    private readonly UIControlledApplication _application;

    internal ModuleContext(UIControlledApplication application, string tabName, UnSetHost host)
    {
        _application = application;
        TabName = tabName;
        Host = host;
    }

    public string TabName { get; }

    public UnSetHost Host { get; }

    public UIControlledApplication Application => _application;

    /// <summary>Create a panel on the UnSet BIM tab.</summary>
    public RibbonPanel CreatePanel(string panelName) => _application.CreateRibbonPanel(TabName, panelName);

    /// <summary>Button data for a command of the calling module.</summary>
    /// <param name="largeIcon">pack URI of a 32x32 PNG, e.g. "pack://application:,,,/UnSetBim.Modules.Families;component/Resources/families_32.png"</param>
    public static PushButtonData CreateButton<TCommand>(string name, string text, string tooltip, string? largeIcon = null, string? smallIcon = null)
        where TCommand : UnSetCommand
    {
        var type = typeof(TCommand);
        var data = new PushButtonData(name, text, type.Assembly.Location, type.FullName)
        {
            ToolTip = tooltip,
            // The command class is also its availability class (same assembly, as Revit requires).
            // UnSetCommand is available without an open document by default; each command checks what it needs.
            AvailabilityClassName = type.FullName,
        };

        if (largeIcon is not null)
            data.LargeImage = LoadIcon(largeIcon);
        if (smallIcon is not null)
            data.Image = LoadIcon(smallIcon);

        return data;
    }

    public static ImageSource? LoadIcon(string packUri)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(packUri, UriKind.Absolute);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex)
        {
            Log.Warn($"Ribbon icon not found: {packUri}", ex);
            return null;
        }
    }
}
