using System.Diagnostics;

namespace Nuvio.Platform;

public sealed record MpvProcessLocatorEnvironment(
    PlatformFamily PlatformFamily,
    string AppBaseDirectory,
    string? EnvironmentOverridePath,
    string? PathVariable,
    string? UserProfileDirectory,
    string? LocalApplicationDataDirectory,
    string? ApplicationDataDirectory,
    string? ProgramDataDirectory,
    Func<string, bool> FileExists,
    Func<string, string, IEnumerable<string>> EnumerateFiles,
    Func<string, string?> GetVersion)
{
    public static MpvProcessLocatorEnvironment Current()
    {
        return new MpvProcessLocatorEnvironment(
            PlatformInfoProvider.Current().Family,
            AppContext.BaseDirectory,
            Environment.GetEnvironmentVariable("NUVIO_MPV_PATH"),
            Environment.GetEnvironmentVariable("PATH"),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            File.Exists,
            EnumerateFilesSafe,
            TryGetVersion);
    }

    private static IEnumerable<string> EnumerateFilesSafe(string directory, string searchPattern)
    {
        try
        {
            return Directory.EnumerateFiles(directory, searchPattern, SearchOption.AllDirectories).Take(64).ToArray();
        }
        catch (DirectoryNotFoundException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string? TryGetVersion(string executablePath)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("--version");

            using var process = Process.Start(startInfo);

            if (process is null)
            {
                return null;
            }

            if (!process.WaitForExit(milliseconds: 2_000))
            {
                process.Kill(entireProcessTree: true);
                return null;
            }

            var firstLine = process.StandardOutput.ReadLine();
            return string.IsNullOrWhiteSpace(firstLine) ? null : firstLine.Trim();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
