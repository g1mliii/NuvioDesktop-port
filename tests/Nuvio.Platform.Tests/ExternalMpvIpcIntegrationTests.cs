using System.Diagnostics;
using System.Text.Json;
using Nuvio.Platform;

namespace Nuvio.Platform.Tests;

public sealed class ExternalMpvIpcIntegrationTests
{
    [Fact]
    public async Task ExternalMpv_AnswersJsonIpcGetVersion_WhenEnabled()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("NUVIO_RUN_MPV_INTEGRATION"), "1", StringComparison.Ordinal))
        {
            return;
        }

        var mpv = new MpvProcessLocator().Locate();
        Assert.True(mpv.IsAvailable, mpv.Message);

        await using var endpoint = MpvIpcEndpoint.Create();
        using var process = StartMpv(mpv.ExecutablePath!, endpoint.MpvArgument);

        try
        {
            await using var stream = await endpoint.ConnectAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            using var reader = new StreamReader(stream);
            await using var writer = new StreamWriter(stream) { AutoFlush = true };

            await writer.WriteLineAsync("""{"command":["get_version"],"request_id":1}""");
            var line = await ReadLineAsync(reader, TimeSpan.FromSeconds(5));

            using var json = JsonDocument.Parse(line);
            Assert.Equal("success", json.RootElement.GetProperty("error").GetString());
            Assert.Equal(1, json.RootElement.GetProperty("request_id").GetInt32());

            await writer.WriteLineAsync("""{"command":["quit"],"request_id":2}""");
        }
        finally
        {
            if (!process.WaitForExit(milliseconds: 5_000) && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }

    private static Process StartMpv(string executablePath, string ipcEndpoint)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("--idle=yes");
        startInfo.ArgumentList.Add("--config=no");
        startInfo.ArgumentList.Add("--terminal=no");
        startInfo.ArgumentList.Add("--input-terminal=no");
        startInfo.ArgumentList.Add($"--input-ipc-server={ipcEndpoint}");

        return Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start mpv.");
    }

    private static async Task<string> ReadLineAsync(StreamReader reader, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        var line = await reader.ReadLineAsync(cancellation.Token);
        return line ?? throw new InvalidOperationException("mpv IPC closed before a response was received.");
    }
}
