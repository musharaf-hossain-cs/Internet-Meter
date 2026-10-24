using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace InternetMeter.Services;

public static class DeskbandRegistrationService
{
    public const string ClassId = "{40B96627-7938-4C3E-A932-0B0E79E50C92}";
    private const string DeskBandCategoryId = "{00021492-0000-0000-C000-000000000046}";
    private const string AssemblyFileName = "InternetMeter.Deskband.dll";

    public static bool IsSupported => Environment.OSVersion.Version.Major == 10 && Environment.OSVersion.Version.Build < 22000;

    public static bool EnsureRegistered()
    {
        if (!IsSupported) return false;
        var assemblyPath = Path.Combine(AppContext.BaseDirectory, AssemblyFileName);
        if (!File.Exists(assemblyPath)) return false;

        // Explorer deliberately requires approved shell extensions to be registered machine-wide.
        // Remove obsolete per-user COM entries because they take precedence over HKLM in HKCR.
        Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\CLSID\{ClassId}", false);
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\InternetMeter.Deskband.InternetMeterDeskBand", false);
        using var machineInproc = Registry.LocalMachine.OpenSubKey(
            $@"Software\Classes\CLSID\{ClassId}\InprocServer32", writable: false);
        var registeredPath = machineInproc?.GetValue(null) as string;
        if (!string.Equals(Path.GetFullPath(registeredPath ?? string.Empty), Path.GetFullPath(assemblyPath),
                StringComparison.OrdinalIgnoreCase)) return false;

        using var deskband = Registry.CurrentUser.CreateSubKey($@"Software\Microsoft\Windows\CurrentVersion\Explorer\DeskBand\{ClassId}");
        NotifyShell();
        return true;
    }

    public static void Unregister()
    {
        Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\CLSID\{ClassId}", false);
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\InternetMeter.Deskband.InternetMeterDeskBand", false);
        Registry.CurrentUser.DeleteSubKeyTree($@"Software\Microsoft\Windows\CurrentVersion\Explorer\DeskBand\{ClassId}", false);
        NotifyShell();
    }

    private static void NotifyShell() => SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
}
