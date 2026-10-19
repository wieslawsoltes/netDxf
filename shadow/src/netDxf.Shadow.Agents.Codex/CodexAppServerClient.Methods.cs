using System.Text.Json;

namespace netDxf.Shadow.Agents.Codex;

public sealed partial class CodexAppServerClient
{
    public Task<JsonElement> ReadAccountAsync(CancellationToken cancellationToken = default) =>
        Invoke("account/read", new { refreshToken = false }, cancellationToken);
    public Task<JsonElement> BeginChatGptLoginAsync(bool deviceCode = false, CancellationToken cancellationToken = default) =>
        Invoke("account/login/start", new { type = deviceCode ? "chatgptDeviceCode" : "chatgpt" }, cancellationToken);
    public Task<JsonElement> CancelLoginAsync(string loginId, CancellationToken cancellationToken = default) =>
        Invoke("account/login/cancel", new { loginId = Required(loginId) }, cancellationToken);
    public Task<JsonElement> LogoutAsync(CancellationToken cancellationToken = default) => CallAsync("account/logout", Empty, cancellationToken);
    public Task<JsonElement> ListModelsAsync(string? cursor = null, CancellationToken cancellationToken = default) =>
        Invoke("model/list", new { cursor, limit = 100, includeHidden = false }, cancellationToken);
    public Task<JsonElement> StartThreadAsync(string model, string cwd, string approvalPolicy = "unlessTrusted", string sandbox = "readOnly",
        CancellationToken cancellationToken = default) =>
        Invoke("thread/start", new { model = Required(model), cwd = Required(cwd), approvalPolicy, sandbox }, cancellationToken);
    public Task<JsonElement> ResumeThreadAsync(string threadId, CancellationToken cancellationToken = default) =>
        Invoke("thread/resume", new { threadId = Required(threadId), excludeTurns = true }, cancellationToken);
    public Task<JsonElement> StartTurnAsync(string threadId, string text, CancellationToken cancellationToken = default) =>
        Invoke("turn/start", new { threadId = Required(threadId), input = new[] { new { type = "text", text = Required(text) } } }, cancellationToken);
    /// <summary>Appends user input to the specified active turn. A stale expected turn is rejected by Codex; this never starts a new turn.</summary>
    public Task<JsonElement> SteerTurnAsync(string threadId, string expectedTurnId, string text, CancellationToken cancellationToken = default) =>
        Invoke("turn/steer", new { threadId = Required(threadId), expectedTurnId = Required(expectedTurnId), input = new[] { new { type = "text", text = Required(text) } } }, cancellationToken);
    /// <summary>Requests manual context compaction. The response acknowledges dispatch; turn/item notifications report completion.</summary>
    public Task<JsonElement> CompactThreadAsync(string threadId, CancellationToken cancellationToken = default) =>
        Invoke("thread/compact/start", new { threadId = Required(threadId) }, cancellationToken);
    public Task<JsonElement> InterruptTurnAsync(string threadId, string turnId, CancellationToken cancellationToken = default) =>
        Invoke("turn/interrupt", new { threadId = Required(threadId), turnId = Required(turnId) }, cancellationToken);
    private Task<JsonElement> Invoke(string method, object parameters, CancellationToken token) => CallAsync(method, JsonSerializer.SerializeToElement(parameters, Json), token);
    private static string Required(string value) { ArgumentException.ThrowIfNullOrWhiteSpace(value); return value; }
}
