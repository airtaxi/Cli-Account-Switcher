using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace CliAccountSwitcher.Api.Security;

public sealed class WindowsDataProtectionService
{
    public byte[] Protect(byte[] plainBytes, string entropyText) => Transform(plainBytes, entropyText, true);

    public byte[] Unprotect(byte[] protectedBytes, string entropyText) => Transform(protectedBytes, entropyText, false);

    private static byte[] Transform(byte[] inputBytes, string entropyText, bool shouldProtect)
    {
        ArgumentNullException.ThrowIfNull(inputBytes);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("DPAPI is only available on Windows.");

        var entropyBytes = Encoding.UTF8.GetBytes(entropyText);
        var inputBlob = CreateDataBlob(inputBytes);
        var entropyBlob = CreateDataBlob(entropyBytes);
        var outputBlob = new DataBlob();

        try
        {
            var succeeded = shouldProtect
                ? CryptProtectData(ref inputBlob, null, ref entropyBlob, IntPtr.Zero, IntPtr.Zero, 0, out outputBlob)
                : CryptUnprotectData(ref inputBlob, null, ref entropyBlob, IntPtr.Zero, IntPtr.Zero, 0, out outputBlob);

            if (!succeeded) throw new CryptographicException("DPAPI operation failed.", new Win32Exception(Marshal.GetLastWin32Error()));
            return CopyDataBlob(outputBlob);
        }
        finally
        {
            FreeInputDataBlob(inputBlob);
            FreeInputDataBlob(entropyBlob);
            if (outputBlob.DataPointer != IntPtr.Zero) LocalFree(outputBlob.DataPointer);
        }
    }

    private static DataBlob CreateDataBlob(byte[] inputBytes)
    {
        if (inputBytes.Length == 0) return new DataBlob();

        var dataPointer = Marshal.AllocHGlobal(inputBytes.Length);
        Marshal.Copy(inputBytes, 0, dataPointer, inputBytes.Length);

        return new DataBlob
        {
            DataLength = inputBytes.Length,
            DataPointer = dataPointer
        };
    }

    private static byte[] CopyDataBlob(DataBlob dataBlob)
    {
        if (dataBlob.DataPointer == IntPtr.Zero || dataBlob.DataLength == 0) return [];

        var outputBytes = new byte[dataBlob.DataLength];
        Marshal.Copy(dataBlob.DataPointer, outputBytes, 0, dataBlob.DataLength);
        return outputBytes;
    }

    private static void FreeInputDataBlob(DataBlob dataBlob)
    {
        if (dataBlob.DataPointer == IntPtr.Zero) return;
        Marshal.FreeHGlobal(dataBlob.DataPointer);
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref DataBlob dataIn, string? description, ref DataBlob optionalEntropy, IntPtr reserved, IntPtr promptStructure, int flags, out DataBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref DataBlob dataIn, string? description, ref DataBlob optionalEntropy, IntPtr reserved, IntPtr promptStructure, int flags, out DataBlob dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memoryHandle);

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int DataLength;

        public IntPtr DataPointer;
    }
}
