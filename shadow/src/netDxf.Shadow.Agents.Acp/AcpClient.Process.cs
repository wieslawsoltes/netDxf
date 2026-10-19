using System.Diagnostics;
using System.Text;

namespace netDxf.Shadow.Agents.Acp;

public sealed partial class AcpClient
{
    /// <summary>Starts one explicitly configured local agent process and initializes ACP v1. Executable and cwd must be absolute.</summary>
    public static async Task<AcpClient> StartAsync(AcpProcessOptions launch, AcpClientServices? services = null,
        AcpConnectionOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(launch);
        (options ??= new()).Validate();
        AbsolutePath(launch.Executable, nameof(launch.Executable)); AbsolutePath(launch.WorkingDirectory, nameof(launch.WorkingDirectory));
        if (!File.Exists(launch.Executable)) throw new FileNotFoundException("ACP executable was not found.", launch.Executable);
        if (!Directory.Exists(launch.WorkingDirectory)) throw new DirectoryNotFoundException("ACP working directory was not found.");
        if (launch.Arguments is { } arguments && (arguments.Count > 128 || arguments.Any(value => value is null || value.Length > 65536 || value.Contains('\0'))))
            throw new ArgumentException("Invalid startup arguments.", nameof(launch));
        if (launch.Environment is { } environment && (environment.Count > 128 || environment.Any(item =>
            string.IsNullOrWhiteSpace(item.Key) || item.Key.Length > 256 || item.Key.Contains('=') || item.Key.Contains('\0') || item.Value is null || item.Value.Length > 65536 || item.Value.Contains('\0'))))
            throw new ArgumentException("Invalid child environment.", nameof(launch));
        cancellationToken.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(launch.Executable)
        {
            WorkingDirectory = launch.WorkingDirectory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = new UTF8Encoding(false), StandardErrorEncoding = new UTF8Encoding(false)
        };
        // Carry only ordinary runtime/location settings. API keys and companion bridge tokens are not automatically inherited.
        var runtimeEnvironment = new[] { "PATH", "HOME", "USER", "LOGNAME", "TMPDIR", "TMP", "TEMP", "LANG", "LC_ALL", "LC_CTYPE", "SystemRoot", "WINDIR", "USERPROFILE", "HOMEDRIVE", "HOMEPATH", "APPDATA", "LOCALAPPDATA", "PATHEXT", "COMSPEC", "XDG_CONFIG_HOME", "XDG_CACHE_HOME", "XDG_DATA_HOME", "XDG_RUNTIME_DIR", "DOTNET_ROOT", "DOTNET_ROOT_X64", "DOTNET_ROOT_X86", "DOTNET_ROOT_ARM64", "DOTNET_ROOT(x86)", "DOTNET_ROLL_FORWARD" };
        start.Environment.Clear();
        foreach (var name in runtimeEnvironment) if (System.Environment.GetEnvironmentVariable(name) is { } value) start.Environment[name] = value;
        foreach (var argument in launch.Arguments ?? []) start.ArgumentList.Add(argument);
        foreach (var (name, value) in launch.Environment ?? new Dictionary<string, string>()) start.Environment[name] = value;
        var process = Process.Start(start) ?? throw new IOException("Could not start the ACP agent.");
        AcpClient client;
        try { client = new AcpClient(process.StandardOutput.BaseStream, process.StandardInput.BaseStream, services, options); }
        catch
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            process.Dispose(); throw;
        }
        var errorPump = Task.Run(async () =>
        {
            var buffer = new char[4096];
            try
            {
                while (true)
                {
                    var length = await process.StandardError.ReadAsync(buffer).ConfigureAwait(false); if (length == 0) break;
                    try { services?.StandardError?.Invoke(new string(buffer, 0, length)); } catch (Exception) { }
                }
            }
            catch (Exception error) when (error is IOException or ObjectDisposedException) { }
        });
        client.disposeProcess = async () =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); } catch (Exception) { }
            try { await errorPump.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); } catch (Exception) { }
            process.Dispose();
        };
        try { await client.InitializeAsync(cancellationToken).ConfigureAwait(false); return client; }
        catch { await client.DisposeAsync().ConfigureAwait(false); throw; }
    }
}
