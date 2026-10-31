namespace UnSetBim.Core.Auth;

/// <summary>
/// The device_name sent on sign-in. It must be stable per installation: signing in again
/// with the same name replaces that device's previous token instead of using a new slot
/// (the server keeps at most 5 devices per user).
///
/// One name per Windows machine, shared by every Revit year, because the token file is shared too.
/// </summary>
public static class DeviceIdentity
{
    public static string DeviceName
    {
        get
        {
            var machine = string.IsNullOrWhiteSpace(Environment.MachineName) ? "PC" : Environment.MachineName;
            var name = $"{machine} (Revit)";
            return name.Length <= 100 ? name : name[..100];
        }
    }
}
