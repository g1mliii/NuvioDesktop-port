using System.Runtime.InteropServices;

namespace Nuvio.Platform;

public static class PlatformInfoProvider
{
    public static PlatformInfo Current()
    {
        var family = GetFamily();
        return new PlatformInfo(family, GetRuntimeIdentifier(family), RuntimeInformation.OSDescription.Trim());
    }

    private static PlatformFamily GetFamily()
    {
        if (OperatingSystem.IsWindows())
        {
            return PlatformFamily.Windows;
        }

        if (OperatingSystem.IsMacOS())
        {
            return PlatformFamily.MacOS;
        }

        if (OperatingSystem.IsLinux())
        {
            return PlatformFamily.Linux;
        }

        return PlatformFamily.Unknown;
    }

    private static string GetRuntimeIdentifier(PlatformFamily family)
    {
        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.X64 => "x64",
            Architecture.X86 => "x86",
            Architecture.Arm => "arm",
            _ => RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()
        };

        var os = family switch
        {
            PlatformFamily.Windows => "win",
            PlatformFamily.MacOS => "osx",
            PlatformFamily.Linux => "linux",
            _ => "unknown"
        };

        return $"{os}-{arch}";
    }
}
