using netDxf.Shadow.AgentHost;

if (args.Contains("--help", StringComparer.Ordinal))
{
    Console.WriteLine("""
        netDxf Shadow Agent Host
          --port 5091
          --origin https://wieslawsoltes.github.io (repeatable)
          --state-file <nonsecret-registration.json>
          --workspace <directory>      Grant native UTF-8 file read/write tools.
          --allow-processes            Also grant native build/test/git/shell execution.
          --max-files 20000            Native workspace file-count budget.
          --max-bytes 134217728        Native workspace total byte budget (128 MiB).
        Optional environment: SHADOW_AGENT_BRIDGE_TOKEN (at least 32 ASCII bytes).
        The host binds only to 127.0.0.1; credentials remain in memory.
        Process execution runs as your OS account and is not a filesystem sandbox.
        """);
    return;
}

var (options, generatedToken) = AgentHostOptions.Parse(args);
await using var app = AgentHostApplication.Build(options);
await app.StartAsync();
Console.WriteLine($"netDxf Shadow Agent Host: http://127.0.0.1:{options.Port}");
Console.WriteLine("Allowed studio origins: " + string.Join(", ", options.AllowedOrigins));
if (generatedToken is not null) Console.WriteLine("Bridge token (shown once): " + generatedToken);
Console.WriteLine("Connect the studio's Agent panel to this address and bridge token. External MCP endpoint: /mcp.");
if (options.Workspace is not null)
    Console.WriteLine($"Native workspace grant: {options.Workspace}; process execution: {(options.AllowProcesses ? "enabled" : "disabled")}. Tools: /workspace/tools.");
await app.WaitForShutdownAsync();
