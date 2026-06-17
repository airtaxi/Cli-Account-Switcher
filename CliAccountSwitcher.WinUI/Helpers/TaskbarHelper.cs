using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace CliAccountSwitcher.WinUI.Helpers;

public static partial class TaskbarHelper
{
    private const int GetTaskbarPositionMessage = 5;
    public const double PreferredTaskbarContentWidth = 200;

    [SupportedOSPlatformGuard("windows10.0.22000.0")]
    public static bool IsTaskbarContentHostSupported => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);

    [LibraryImport("shell32.dll", EntryPoint = "SHAppBarMessage")]
    private static partial nint SendAppBarMessage(int messageIdentifier, ref AppBarData appBarData);

    public static NativeRectangle GetTaskbarRectangle()
    {
        var appBarData = GetAppBarData();
        return appBarData.Rectangle;
    }

    public static TaskbarPosition GetTaskbarPosition()
    {
        if (IsTaskbarContentHostSupported) return TaskbarPosition.Bottom;

        var appBarData = GetAppBarData();
        return appBarData.EdgeIdentifier switch
        {
            0 => TaskbarPosition.Left,
            1 => TaskbarPosition.Top,
            2 => TaskbarPosition.Right,
            3 => TaskbarPosition.Bottom,
            _ => TaskbarPosition.Bottom
        };
    }

    private static AppBarData GetAppBarData()
    {
        var appBarData = new AppBarData
        {
            SizeInBytes = Marshal.SizeOf<AppBarData>()
        };

        SendAppBarMessage(GetTaskbarPositionMessage, ref appBarData);
        return appBarData;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AppBarData
    {
        public int SizeInBytes;
        public nint WindowHandle;
        public int CallbackMessageIdentifier;
        public int EdgeIdentifier;
        public NativeRectangle Rectangle;
        public nint Parameter;
    }
}

public enum TaskbarPosition
{
    Left,
    Top,
    Right,
    Bottom
}

[StructLayout(LayoutKind.Sequential)]
public struct NativeRectangle
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}
