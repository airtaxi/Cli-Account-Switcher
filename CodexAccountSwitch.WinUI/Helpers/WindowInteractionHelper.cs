using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;
using WinRT.Interop;
using WinUIEx;

namespace CodexAccountSwitch.WinUI.Helpers;

public static partial class WindowInteractionHelper
{
    private const uint DwmAttributeTransitionsForceDisabled = 3;
    private const uint DwmAttributeUseImmersiveDarkMode = 20;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint windowHandle);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachThreadInput(uint attachThreadIdentifier, uint attachToThreadIdentifier, [MarshalAs(UnmanagedType.Bool)] bool shouldAttach);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint GetWindowThreadProcessId(nint windowHandle, out uint processIdentifier);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint windowHandle, uint attributeIdentifier, ref uint attributeValue, uint attributeSize);

    public static void ForceForegroundWindow(Window window)
    {
        var windowHandle = window.GetWindowHandle();
        var foregroundWindowHandle = GetForegroundWindow();
        var foregroundThreadIdentifier = GetWindowThreadProcessId(foregroundWindowHandle, out _);
        var currentThreadIdentifier = GetCurrentThreadId();
        var shouldDetachThreadInput = foregroundThreadIdentifier != 0 && foregroundThreadIdentifier != currentThreadIdentifier && AttachThreadInput(foregroundThreadIdentifier, currentThreadIdentifier, true);

        try { SetForegroundWindow(windowHandle); }
        finally
        {
            if (shouldDetachThreadInput) AttachThreadInput(foregroundThreadIdentifier, currentThreadIdentifier, false);
        }
    }

    public static void DisableWindowAnimations(Window window)
    {
        var windowHandle = window.GetWindowHandle();
        var disableAnimation = 1u;
        DwmSetWindowAttribute(windowHandle, DwmAttributeTransitionsForceDisabled, ref disableAnimation, sizeof(uint));
    }

    public static void SetDarkModeWindow(Window window)
    {
        var windowHandle = window.GetWindowHandle();
        var darkMode = 1u;
        DwmSetWindowAttribute(windowHandle, DwmAttributeUseImmersiveDarkMode, ref darkMode, sizeof(uint));
    }
}
