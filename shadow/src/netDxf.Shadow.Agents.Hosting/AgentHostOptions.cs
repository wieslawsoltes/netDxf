using System.Security.Cryptography;
using System.Text;

namespace netDxf.Shadow.AgentHost;

public sealed class AgentHostOptions
{
    private readonly byte[] _tokenHash;

    public AgentHostOptions(int port, IEnumerable<string> origins, string bridgeToken, string stateFile,
        string? workspace = null, bool allowProcesses = false, int maximumWorkspaceFiles = 20000,
        int maximumWorkspaceBytes = 128 * 1024 * 1024, string? codexExecutable = null, string? acpExecutable = null,
        IEnumerable<string>? acpArguments = null)
    {
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        if (Encoding.UTF8.GetByteCount(bridgeToken) < 32 || bridgeToken.Length > 1024
            || bridgeToken.Any(c => c is < '!' or > '~'))
            throw new ArgumentException("The bridge token requires at least 32 bytes of visible ASCII text.", nameof(bridgeToken));
        Port = port;
        _tokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(bridgeToken));
        StateFile = Path.GetFullPath(stateFile);
        if (maximumWorkspaceFiles is < 1 or > 1000000 || maximumWorkspaceBytes < 1024)
            throw new ArgumentOutOfRangeException(nameof(maximumWorkspaceFiles), "Supply a positive bounded workspace file and byte budget.");
        if (allowProcesses && workspace is null)
            throw new ArgumentException("--allow-processes requires an explicit --workspace directory.", nameof(allowProcesses));
        Workspace = workspace is null ? null : Path.GetFullPath(workspace);
        if (Workspace is not null && !Directory.Exists(Workspace)) throw new DirectoryNotFoundException("The granted workspace directory does not exist.");
        if ((codexExecutable is not null || acpExecutable is not null) && workspace is null)
            throw new ArgumentException("External coding agents require an explicit --workspace directory.");
        static string? Executable(string? path) => path is null ? null : Path.IsPathFullyQualified(path) && File.Exists(path)
            ? Path.GetFullPath(path) : throw new ArgumentException("Agent executables must be existing absolute paths.");
        CodexExecutable = Executable(codexExecutable); AcpExecutable = Executable(acpExecutable);
        AcpArguments = (acpArguments ?? []).ToArray();
        if (AcpArguments.Count > 64 || AcpArguments.Any(a => a is null || a.Length > 8192 || a.Contains('\0')))
            throw new ArgumentException("Agent arguments exceed the startup limits.");
        AllowProcesses = allowProcesses;
        MaximumWorkspaceFiles = maximumWorkspaceFiles;
        MaximumWorkspaceBytes = maximumWorkspaceBytes;
        var allowed = new HashSet<string>(StringComparer.Ordinal) { $"http://127.0.0.1:{port}" };
        foreach (var value in origins)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo)
                || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || uri.AbsolutePath != "/"
                || (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)))
                throw new ArgumentException("Allowed origins must be exact HTTPS origins, or loopback HTTP origins, without a path.", nameof(origins));
            allowed.Add(uri.GetLeftPart(UriPartial.Authority));
        }
        AllowedOrigins = allowed;
    }

    public int Port { get; }
    public IReadOnlySet<string> AllowedOrigins { get; }
    public string StateFile { get; }
    public string? Workspace { get; }
    public bool AllowProcesses { get; }
    public string? CodexExecutable { get; }
    public string? AcpExecutable { get; }
    public IReadOnlyList<string> AcpArguments { get; }
    public int MaximumWorkspaceFiles { get; }
    public int MaximumWorkspaceBytes { get; }
    public Uri CallbackUri => new($"http://127.0.0.1:{Port}/auth/callback");
    public bool ValidateToken(string? token) => token is { Length: > 0 and <= 1024 }
        && CryptographicOperations.FixedTimeEquals(_tokenHash, SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public static (AgentHostOptions Options, string? GeneratedToken) Parse(string[] args)
    {
        var port = 5091;
        var origins = new List<string>();
        string? workspace = null;
        var allowProcesses = false;
        string? codexExecutable = null, acpExecutable = null;
        var acpArguments = new List<string>();
        var maximumFiles = 20000;
        var maximumBytes = 128 * 1024 * 1024;
        var stateFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "netDxf", "agent-host.json");
        for (var i = 0; i < args.Length; i++)
        {
            var option = args[i];
            if (option == "--allow-processes") { allowProcesses = true; continue; }
            if (i + 1 >= args.Length) throw new ArgumentException($"Missing value for '{option}'.");
            var value = args[++i];
            switch (option)
            {
                case "--port" when int.TryParse(value, out var parsed): port = parsed; break;
                case "--origin": origins.Add(value); break;
                case "--state-file": stateFile = value; break;
                case "--workspace": workspace = value; break;
                case "--codex-executable": codexExecutable = value; break;
                case "--acp-executable": acpExecutable = value; break;
                case "--acp-argument": acpArguments.Add(value); break;
                case "--max-files" when int.TryParse(value, out var files): maximumFiles = files; break;
                case "--max-bytes" when int.TryParse(value, out var bytes): maximumBytes = bytes; break;
                default: throw new ArgumentException($"Unknown option or invalid value for '{option}'. Use --help to list supported options.");
            }
        }
        if (origins.Count == 0) origins.Add("https://wieslawsoltes.github.io");
        var configuredToken = Environment.GetEnvironmentVariable("SHADOW_AGENT_BRIDGE_TOKEN");
        var token = configuredToken ?? Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return (new(port, origins, token, stateFile, workspace, allowProcesses, maximumFiles, maximumBytes, codexExecutable, acpExecutable, acpArguments), configuredToken is null ? token : null);
    }
}
