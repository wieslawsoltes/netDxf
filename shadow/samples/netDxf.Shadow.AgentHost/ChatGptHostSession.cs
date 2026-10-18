using System.Text.Json;
using netDxf.Shadow.Agents.Auth;

namespace netDxf.Shadow.AgentHost;

public sealed record ChatGptHostStatus(bool Authenticated, DateTimeOffset? ExpiresAt, string? Email, string? Error);
public sealed record ChatGptHostLease(ChatGptCredential Credential, CancellationToken SessionCancellation);

/// <summary>Keeps OAuth credentials in memory while retaining only the nonsecret registration and host identity.</summary>
public sealed class ChatGptHostSession : IDisposable
{
    private readonly AgentHostOptions _options;
    private readonly ChatGptSignInClient _auth;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private HostRegistration _registration;
    private ChatGptAuthorizationAttempt? _pending;
    private ChatGptCredential? _credential;
    private CancellationTokenSource _sessionLifetime = new();
    private ChatGptHostStatus _status = new(false, null, null, null);

    public ChatGptHostSession(AgentHostOptions options, ChatGptSignInClient auth)
    {
        _options = options; _auth = auth;
        if (File.Exists(options.StateFile))
        {
            if (new FileInfo(options.StateFile).Length > 16_384) throw new InvalidDataException("Agent host registration file exceeds its size limit.");
            _registration = JsonSerializer.Deserialize<HostRegistration>(File.ReadAllText(options.StateFile))
                ?? throw new InvalidDataException("Agent host registration file is empty.");
            if (string.IsNullOrWhiteSpace(_registration.HostId) || _registration.HostId.Length > 256)
                throw new InvalidDataException("Agent host registration has no valid host identity.");
        }
        else
        {
            _registration = new("urn:uuid:" + Guid.NewGuid().ToString("D"), null, null, null);
            SaveRegistration();
        }
    }

    public ChatGptHostStatus Status => Volatile.Read(ref _status);

    public async Task<Uri> BeginAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _pending = await _auth.BeginAsync(new()
            {
                AppName = "netDxf Shadow Studio", HostId = _registration.HostId, CallbackUri = _options.CallbackUri,
                ClientId = _registration.ClientId, ExpectedSubject = _registration.Subject,
                LoginHint = _registration.Email,
            }, cancellationToken).ConfigureAwait(false);
            SetStatus(null);
            return _pending.AuthorizationUri;
        }
        catch (Exception error) { SetStatus(SafeError(error)); throw; }
        finally { _gate.Release(); }
    }

    public async Task CompleteAsync(Uri callback, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_pending is null) throw new ChatGptAuthenticationException("no_pending_login", "Start a new sign-in from the studio.");
            var credential = await _auth.CompleteAsync(_pending, callback, cancellationToken).ConfigureAwait(false);
            _pending = null;
            _sessionLifetime.Cancel(); _sessionLifetime.Dispose(); _sessionLifetime = new();
            _credential = credential;
            _registration = new(credential.HostId, credential.ClientId, credential.Subject, credential.Email);
            SaveRegistration();
            SetStatus(null);
        }
        catch (Exception error)
        {
            // OpenAI asks public clients to retain the issued registration even when a one-use code exchange fails.
            // Account identity is persisted only after signed ID-token validation succeeds.
            if (_registration.ClientId is null && _pending?.RegisteredClientId is { } issuedClient)
            {
                _registration = _registration with { ClientId = issuedClient };
                SaveRegistration();
            }
            if (error is not ChatGptAuthenticationException { Code: "invalid_state" or "callback_mismatch" }) _pending = null;
            SetStatus(SafeError(error)); throw;
        }
        finally { _gate.Release(); }
    }

    public async Task<ChatGptHostLease> GetLeaseAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_credential is null) throw new ChatGptAuthenticationException("sign_in_required", "Continue with ChatGPT in the studio before running this provider.");
            if (_credential.ExpiresAt <= DateTimeOffset.UtcNow.AddMinutes(2))
            {
                _credential = await _auth.RefreshAsync(_credential, cancellationToken).ConfigureAwait(false);
                SetStatus(null);
            }
            return new(_credential, _sessionLifetime.Token);
        }
        catch (Exception error)
        {
            if (error is ChatGptAuthenticationException { Code: "invalid_grant" or "missing_refresh_token" or "account_mismatch" })
            {
                _credential = null;
                _sessionLifetime.Cancel();
            }
            SetStatus(SafeError(error)); throw;
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> LogoutAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var credential = _credential;
            _credential = null; _pending = null; _sessionLifetime.Cancel();
            SetStatus(null);
            var revoked = credential is null || await _auth.RevokeAsync(credential, cancellationToken).ConfigureAwait(false);
            if (!revoked) SetStatus("Signed out locally; remote revocation was not confirmed. The app can also be disconnected in ChatGPT settings.");
            return revoked;
        }
        finally { _gate.Release(); }
    }

    private void SetStatus(string? error) => Volatile.Write(ref _status, new(_credential is not null, _credential?.ExpiresAt, _credential?.Email, error));
    private static string SafeError(Exception error) => error is ChatGptAuthenticationException authentication ? authentication.Message
        : error is OperationCanceledException ? "The request was canceled." : "The authentication service could not complete the request. Retry when the connection is available.";

    private void SaveRegistration()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_options.StateFile)!);
        var temporary = _options.StateFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(_registration));
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(temporary, _options.StateFile, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public void Dispose()
    {
        _sessionLifetime.Cancel(); _sessionLifetime.Dispose(); _credential = null; _pending = null; _gate.Dispose();
    }
    private sealed record HostRegistration(string HostId, string? ClientId, string? Subject, string? Email);
}
