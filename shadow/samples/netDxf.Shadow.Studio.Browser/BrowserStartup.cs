using Microsoft.JSInterop;

namespace netDxf.Shadow.Studio.Browser;

/// <summary>Blazor.start completes before an asynchronous application entry point does.
/// This handshake prevents the shared UI from issuing commands before fonts and the
/// drawing engine are initialized, and propagates initialization failures to the shell.</summary>
public static class BrowserStartup
{
    private static readonly TaskCompletionSource Ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal static void Complete() => Ready.TrySetResult();
    internal static void Fail(Exception error) => Ready.TrySetException(error);
    [JSInvokable] public static Task WhenReady() => Ready.Task;
}
