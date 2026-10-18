using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace netDxf.Shadow.Agents.Auth;

/// <summary>Configuration for OpenAI's documented public-client registration for open-source local applications.</summary>
public sealed record ChatGptSignInOptions
{
    public string AppName { get; init; } = "netDxf Shadow Studio";
    public string HostId { get; init; } = "";
    public Uri CallbackUri { get; init; } = new("http://127.0.0.1:1455/auth/callback");
    public string? ClientId { get; init; }
    public string? ExpectedSubject { get; init; }
    [JsonIgnore]
    public string? IdTokenHint { get; init; }
    public string? LoginHint { get; init; }
    public override string ToString() => $"{AppName}; HostId={HostId}; ClientId={ClientId ?? "new registration"}";
}

/// <summary>One single-use OAuth transaction. The secret PKCE verifier remains private to the local companion.</summary>
public sealed class ChatGptAuthorizationAttempt
{
    private int _consumed;

    internal ChatGptAuthorizationAttempt(ChatGptSignInOptions options, Uri authorizationUri, string state,
        string nonce, string verifier, DateTimeOffset expiresAt)
    {
        Options = options; AuthorizationUri = authorizationUri; State = state; Nonce = nonce;
        Verifier = verifier; ExpiresAt = expiresAt;
    }

    public Uri AuthorizationUri { get; }
    public Uri CallbackUri => Options.CallbackUri;
    public DateTimeOffset ExpiresAt { get; }
    /// <summary>The nonsecret registration accepted after callback/state validation, retained if a code exchange must be restarted.</summary>
    public string? RegisteredClientId { get; internal set; }
    internal ChatGptSignInOptions Options { get; }
    internal string State { get; }
    internal string Nonce { get; }
    internal string Verifier { get; }
    internal bool Consume() => Interlocked.Exchange(ref _consumed, 1) == 0;
    public override string ToString() => $"ChatGPT authorization; callback={CallbackUri}; expires={ExpiresAt:O}";
}

/// <summary>Validated ChatGPT credentials. Tokens live only in memory; owners decide their storage policy.</summary>
public sealed class ChatGptCredential
{
    internal ChatGptCredential(string clientId, string hostId, string subject, string? email, string accessToken,
        string? refreshToken, string idToken, DateTimeOffset expiresAt, IReadOnlyList<string> scopes)
    {
        ClientId = clientId; HostId = hostId; Subject = subject; Email = email; AccessToken = accessToken;
        RefreshToken = refreshToken; IdToken = idToken; ExpiresAt = expiresAt; Scopes = scopes;
    }

    public string ClientId { get; }
    public string HostId { get; }
    public string Subject { get; }
    public string? Email { get; }
    [JsonIgnore]
    public string AccessToken { get; }
    [JsonIgnore]
    public string? RefreshToken { get; }
    [JsonIgnore]
    public string IdToken { get; }
    public DateTimeOffset ExpiresAt { get; }
    public IReadOnlyList<string> Scopes { get; }
    public bool HasPlanUsage => Scopes.Contains("chatgpt.tokens.use.direct", StringComparer.Ordinal)
        && Scopes.Contains("resource.invoke", StringComparer.Ordinal);
    public override string ToString() => $"ChatGptCredential {{ ClientId={ClientId}, ExpiresAt={ExpiresAt:O}, Tokens=[redacted] }}";
}

/// <summary>
/// Implements official Sign in with ChatGPT: dynamic registration, PKCE, discovery, JWKS signature
/// validation, refresh rotation and revocation. The host must start the exact loopback listener before BeginAsync.
/// </summary>
/// <remarks>https://developers.openai.com/siwc/token-sharing-open-source/sign-in</remarks>
public sealed class ChatGptSignInClient : IDisposable
{
    private const string Issuer = "https://auth.openai.com";
    private const string Resource = "https://api.openai.com/v1";
    private const string InitialClientId = "dynamic_agent_client";
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly ConcurrentDictionary<(string Client, string Subject), ChatGptCredential> _sessions = new();
    private Discovery? _discovery;

    public ChatGptSignInClient(HttpClient? client = null, TimeProvider? timeProvider = null)
    {
        _httpClient = client ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(60) };
        _ownsClient = client is null;
        _time = timeProvider ?? TimeProvider.System;
    }

    public async Task<ChatGptAuthorizationAttempt> BeginAsync(ChatGptSignInOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.AppName);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.HostId);
        ValidateCallbackUri(options.CallbackUri);
        if (options.ClientId == InitialClientId)
            throw new ArgumentException("Retain the issued client ID after registration; leave ClientId empty for initial registration.", nameof(options));
        var discovery = await GetDiscoveryAsync(cancellationToken).ConfigureAwait(false);
        var state = RandomValue(); var nonce = RandomValue(); var verifier = RandomValue();
        var values = new Dictionary<string, string>
        {
            ["client_id"] = options.ClientId ?? InitialClientId,
            ["ext_agent_host_id"] = options.HostId,
            ["response_type"] = "code", ["redirect_uri"] = options.CallbackUri.AbsoluteUri,
            ["scope"] = "openid profile email offline_access resource.invoke chatgpt.tokens.use.direct",
            ["resource"] = Resource, ["state"] = state, ["nonce"] = nonce,
            ["code_challenge_method"] = "S256",
            ["code_challenge"] = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))),
        };
        if (options.ClientId is null) values["agent_name_hint"] = options.AppName;
        else
        {
            if (!string.IsNullOrEmpty(options.IdTokenHint)) values["id_token_hint"] = options.IdTokenHint;
            if (!string.IsNullOrEmpty(options.LoginHint)) values["login_hint"] = options.LoginHint;
        }
        var authorization = new Uri(discovery.AuthorizationEndpoint + "?" + string.Join("&",
            values.Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value))));
        return new(options, authorization, state, nonce, verifier, _time.GetUtcNow().AddMinutes(10));
    }

    public async Task<ChatGptCredential> CompleteAsync(ChatGptAuthorizationAttempt attempt, Uri callback,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentNullException.ThrowIfNull(callback);
        if (!callback.IsAbsoluteUri || callback.GetLeftPart(UriPartial.Path) != attempt.CallbackUri.AbsoluteUri
            || !string.IsNullOrEmpty(callback.Fragment))
            throw new ChatGptAuthenticationException("callback_mismatch", "The sign-in callback does not match this authorization attempt.");
        var query = ParseQuery(callback.Query);
        if (!query.TryGetValue("state", out var state) || !FixedEquals(state, attempt.State))
            throw new ChatGptAuthenticationException("invalid_state", "The sign-in state does not match this authorization attempt.");
        if (_time.GetUtcNow() >= attempt.ExpiresAt || !attempt.Consume())
            throw new ChatGptAuthenticationException("expired_attempt", "This sign-in attempt has expired or was already used. Start again.");
        if (query.TryGetValue("error", out var error))
            throw new ChatGptAuthenticationException(error, error == "access_denied" ? "ChatGPT sign-in was declined." : "ChatGPT sign-in failed.");
        if (!query.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
            throw new ChatGptAuthenticationException("missing_code", "The callback did not contain an authorization code.");
        query.TryGetValue("client_id", out var returnedClient);
        var clientId = attempt.Options.ClientId ?? returnedClient;
        if (string.IsNullOrWhiteSpace(clientId) || clientId == InitialClientId
            || (attempt.Options.ClientId is not null && returnedClient is not null && returnedClient != attempt.Options.ClientId))
            throw new ChatGptAuthenticationException("client_mismatch", "The callback did not contain the expected issued client registration.");
        attempt.RegisteredClientId = clientId;
        using var tokens = await ExchangeAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["client_id"] = clientId, ["code"] = code,
            ["code_verifier"] = attempt.Verifier, ["redirect_uri"] = attempt.CallbackUri.AbsoluteUri, ["resource"] = Resource,
        }, cancellationToken).ConfigureAwait(false);
        var idToken = RequiredString(tokens.RootElement, "id_token");
        var identity = await ValidateIdentityAsync(idToken, clientId, attempt.Nonce, cancellationToken).ConfigureAwait(false);
        if (attempt.Options.ExpectedSubject is { } expected && expected != identity.Subject)
            throw new ChatGptAuthenticationException("account_mismatch", "The signed-in ChatGPT account differs from the selected account.");
        var credential = ReadCredential(tokens.RootElement, clientId, attempt.Options.HostId, identity, idToken, null);
        _sessions[(clientId, identity.Subject)] = credential;
        return credential;
    }

    public async Task<ChatGptCredential> RefreshAsync(ChatGptCredential credential, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credential);
        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_sessions.TryGetValue((credential.ClientId, credential.Subject), out var current) && !ReferenceEquals(current, credential))
                return current;
            if (string.IsNullOrEmpty(credential.RefreshToken))
                throw new ChatGptAuthenticationException("missing_refresh_token", "This account needs to sign in again.");
            using var tokens = await ExchangeAsync(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token", ["client_id"] = credential.ClientId,
                ["refresh_token"] = credential.RefreshToken, ["resource"] = Resource,
            }, cancellationToken).ConfigureAwait(false);
            var idToken = OptionalString(tokens.RootElement, "id_token") ?? credential.IdToken;
            var identity = new Identity(credential.Subject, credential.Email);
            if (idToken != credential.IdToken)
            {
                identity = await ValidateIdentityAsync(idToken, credential.ClientId, null, cancellationToken).ConfigureAwait(false);
                if (identity.Subject != credential.Subject)
                    throw new ChatGptAuthenticationException("account_mismatch", "The refreshed token belongs to another account.");
            }
            var updated = ReadCredential(tokens.RootElement, credential.ClientId, credential.HostId, identity, idToken, credential);
            _sessions[(credential.ClientId, credential.Subject)] = updated;
            return updated;
        }
        finally { _refreshLock.Release(); }
    }

    /// <summary>Revokes the renewable session, then removes its in-memory record. False means remote revocation was not confirmed.</summary>
    public async Task<bool> RevokeAsync(ChatGptCredential credential, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credential);
        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_sessions.TryGetValue((credential.ClientId, credential.Subject), out var current)) credential = current;
            if (string.IsNullOrEmpty(credential.RefreshToken)) return true;
            var discovery = await GetDiscoveryAsync(cancellationToken).ConfigureAwait(false);
            if (discovery.RevocationEndpoint is null) return false;
            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["token"] = credential.RefreshToken, ["token_type_hint"] = "refresh_token", ["client_id"] = credential.ClientId,
            });
            using var response = await _httpClient.PostAsync(discovery.RevocationEndpoint, form, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException) { return false; }
        finally
        {
            _sessions.TryRemove((credential.ClientId, credential.Subject), out _);
            _refreshLock.Release();
        }
    }

    private ChatGptCredential ReadCredential(JsonElement tokens, string clientId, string hostId, Identity identity,
        string idToken, ChatGptCredential? previous)
    {
        if (!string.Equals(RequiredString(tokens, "token_type"), "Bearer", StringComparison.OrdinalIgnoreCase))
            throw new ChatGptAuthenticationException("invalid_token_type", "OpenAI returned an unsupported credential type.");
        var scopeText = OptionalString(tokens, "scope");
        var scopes = scopeText is not null ? Array.AsReadOnly(scopeText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            : previous?.Scopes ?? Array.Empty<string>();
        if (!scopes.Contains("chatgpt.tokens.use.direct", StringComparer.Ordinal) || !scopes.Contains("resource.invoke", StringComparer.Ordinal))
            throw new ChatGptAuthenticationException("plan_usage_not_enabled", "ChatGPT plan usage was not authorized. Enable it in ChatGPT and sign in again.");
        if (!tokens.TryGetProperty("expires_in", out var expires) || !expires.TryGetInt64(out var seconds) || seconds <= 0 || seconds > 31_536_000)
            throw new ChatGptAuthenticationException("invalid_expiry", "OpenAI returned an invalid token lifetime.");
        return new(clientId, hostId, identity.Subject, identity.Email, RequiredString(tokens, "access_token"),
            OptionalString(tokens, "refresh_token") ?? previous?.RefreshToken, idToken, _time.GetUtcNow().AddSeconds(seconds), scopes);
    }

    private async Task<JsonDocument> ExchangeAsync(Dictionary<string, string> values, CancellationToken cancellationToken)
    {
        var discovery = await GetDiscoveryAsync(cancellationToken).ConfigureAwait(false);
        using var form = new FormUrlEncodedContent(values);
        using var response = await _httpClient.PostAsync(discovery.TokenEndpoint, form, cancellationToken).ConfigureAwait(false);
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var document = await JsonDocument.ParseAsync(stream, new JsonDocumentOptions { MaxDepth = 16 }, cancellationToken).ConfigureAwait(false);
        if (response.IsSuccessStatusCode) return document;
        var error = OptionalString(document.RootElement, "error") ?? "token_exchange_failed";
        document.Dispose();
        throw new ChatGptAuthenticationException(error, $"OpenAI token exchange failed (HTTP {(int)response.StatusCode}). Sign in again if the session expired.");
    }

    private async Task<Discovery> GetDiscoveryAsync(CancellationToken cancellationToken)
    {
        if (_discovery is { } cached) return cached;
        using var response = await _httpClient.GetAsync(Issuer + "/.well-known/openid-configuration", cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        if (RequiredString(root, "issuer") != Issuer)
            throw new ChatGptAuthenticationException("invalid_issuer", "The OpenAI discovery document has an unexpected issuer.");
        _discovery = new(TrustedEndpoint(RequiredString(root, "authorization_endpoint")),
            TrustedEndpoint(RequiredString(root, "token_endpoint")), TrustedEndpoint(RequiredString(root, "jwks_uri")),
            OptionalString(root, "revocation_endpoint") is { } revocation ? TrustedEndpoint(revocation) : null);
        return _discovery;
    }

    private async Task<Identity> ValidateIdentityAsync(string token, string audience, string? nonce, CancellationToken cancellationToken)
    {
        if (token.Length > 65_536) throw InvalidIdentity();
        var parts = token.Split('.');
        if (parts.Length != 3) throw InvalidIdentity();
        using var header = JsonDocument.Parse(Decode(parts[0]));
        using var claims = JsonDocument.Parse(Decode(parts[1]));
        var algorithm = RequiredString(header.RootElement, "alg");
        if (algorithm is not ("RS256" or "ES256")) throw InvalidIdentity();
        var keyId = RequiredString(header.RootElement, "kid");
        var discovery = await GetDiscoveryAsync(cancellationToken).ConfigureAwait(false);
        using var response = await _httpClient.GetAsync(discovery.JwksUri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var jwks = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var signedData = Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]);
        var signature = Decode(parts[2]);
        var validSignature = false;
        foreach (var key in jwks.RootElement.GetProperty("keys").EnumerateArray())
        {
            if (OptionalString(key, "kid") != keyId || (OptionalString(key, "use") is { } use && use != "sig")
                || (OptionalString(key, "alg") is { } keyAlgorithm && keyAlgorithm != algorithm)) continue;
            if (algorithm == "RS256" && OptionalString(key, "kty") == "RSA")
            {
                using var rsa = RSA.Create();
                rsa.ImportParameters(new RSAParameters { Modulus = Decode(RequiredString(key, "n")), Exponent = Decode(RequiredString(key, "e")) });
                validSignature = rsa.KeySize >= 2048 && rsa.VerifyData(signedData, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }
            else if (algorithm == "ES256" && OptionalString(key, "kty") == "EC" && OptionalString(key, "crv") == "P-256")
            {
                using var ec = ECDsa.Create(new ECParameters
                {
                    Curve = ECCurve.NamedCurves.nistP256,
                    Q = new ECPoint { X = Decode(RequiredString(key, "x")), Y = Decode(RequiredString(key, "y")) },
                });
                validSignature = ec.VerifyData(signedData, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            }
            if (validSignature) break;
        }
        if (!validSignature) throw InvalidIdentity();
        var payload = claims.RootElement;
        var now = _time.GetUtcNow().ToUnixTimeSeconds();
        if (RequiredString(payload, "iss") != Issuer || !payload.TryGetProperty("aud", out var aud)
            || !AudienceMatches(aud, audience) || (OptionalString(payload, "azp") is { } azp && azp != audience)
            || (aud.ValueKind == JsonValueKind.Array && aud.GetArrayLength() > 1 && OptionalString(payload, "azp") != audience)
            || !payload.TryGetProperty("exp", out var exp) || !exp.TryGetInt64(out var expires) || expires <= now - 60
            || (payload.TryGetProperty("nbf", out var nbf) && (!nbf.TryGetInt64(out var notBefore) || notBefore > now + 60))
            || (payload.TryGetProperty("iat", out var iat) && (!iat.TryGetInt64(out var issuedAt) || issuedAt > now + 60))
            || (nonce is not null && !FixedEquals(OptionalString(payload, "nonce") ?? "", nonce))) throw InvalidIdentity();
        return new(RequiredString(payload, "sub"), OptionalString(payload, "email"));
    }

    private static bool AudienceMatches(JsonElement aud, string expected) => aud.ValueKind switch
    {
        JsonValueKind.String => aud.GetString() == expected,
        JsonValueKind.Array => aud.EnumerateArray().Any(value => value.ValueKind == JsonValueKind.String && value.GetString() == expected),
        _ => false,
    };

    internal static void ValidateCallbackUri(Uri callback)
    {
        if (!callback.IsAbsoluteUri || callback.Scheme != "http" || callback.Host != "127.0.0.1"
            || callback.Port <= 0 || !string.IsNullOrEmpty(callback.UserInfo) || !string.IsNullOrEmpty(callback.Query)
            || !string.IsNullOrEmpty(callback.Fragment) || callback.AbsolutePath == "/")
            throw new ArgumentException("ChatGPT plan sign-in requires an HTTP callback on 127.0.0.1 with a stable path and an active local listener.", nameof(callback));
    }

    private static Uri TrustedEndpoint(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "auth.openai.com"
            || !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ChatGptAuthenticationException("invalid_endpoint", "OpenAI discovery contained an untrusted authentication endpoint.");
        return uri;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            var key = Uri.UnescapeDataString(pair[0].Replace('+', ' '));
            var value = pair.Length > 1 ? Uri.UnescapeDataString(pair[1].Replace('+', ' ')) : "";
            if (!result.TryAdd(key, value)) throw new ChatGptAuthenticationException("duplicate_parameter", "The callback contains duplicate OAuth parameters.");
        }
        return result;
    }

    private static string RandomValue() => Base64Url(RandomNumberGenerator.GetBytes(32));
    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] Decode(string value)
    {
        if (value.Length == 0 || value.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))) throw InvalidIdentity();
        try { return Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + new string('=', (4 - value.Length % 4) % 4)); }
        catch (FormatException) { throw InvalidIdentity(); }
    }
    private static bool FixedEquals(string a, string b) => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
    private static string? OptionalString(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static string RequiredString(JsonElement element, string name) => OptionalString(element, name) is { Length: > 0 } value
        ? value : throw new ChatGptAuthenticationException("invalid_auth_response", $"The authentication response omitted '{name}'.");
    private static ChatGptAuthenticationException InvalidIdentity() => new("invalid_id_token", "OpenAI identity token validation failed.");

    public void Dispose()
    {
        _sessions.Clear();
        if (_ownsClient) _httpClient.Dispose();
        _refreshLock.Dispose();
    }

    private sealed record Discovery(Uri AuthorizationEndpoint, Uri TokenEndpoint, Uri JwksUri, Uri? RevocationEndpoint);
    private sealed record Identity(string Subject, string? Email);
}

public sealed class ChatGptAuthenticationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
