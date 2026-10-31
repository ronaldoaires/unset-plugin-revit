using System.Windows;
using System.Windows.Interop;
using UnSetBim.Framework.Hosting;

namespace UnSetBim.Framework.Dialogs;

public static class WindowOwner
{
    /// <summary>Make Revit's main window the owner, so the window stays above Revit and closes with it.</summary>
    public static T AttachToRevit<T>(this T window) where T : Window
    {
        if (UnSetHost.IsStarted && UnSetHost.Current.MainWindowHandle != IntPtr.Zero)
            new WindowInteropHelper(window).Owner = UnSetHost.Current.MainWindowHandle;

        window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        return window;
    }
}
