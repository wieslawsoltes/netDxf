using Microsoft.JSInterop;

namespace netDxf.Shadow.Studio.Browser;

public static partial class BrowserRuntime
{
    [JSInvokable] public static object AgentMcpSessionOpen(string documentId) => App.Agents.OpenMcpSession(documentId);
    [JSInvokable] public static Task<string?> AgentMcpSessionProcess(string sessionId, string message) => App.Agents.ProcessMcpSession(sessionId, message);
    [JSInvokable] public static Task AgentMcpSessionClose(string sessionId) => App.Agents.CloseMcpSession(sessionId);
}
