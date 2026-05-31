using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PostQuantum.AspNetCore.Internal;
using PostQuantum.Jwt;

namespace PostQuantum.AspNetCore;

/// <summary>
/// An <see cref="AuthenticationHandler{TOptions}"/> that validates incoming
/// <c>Authorization: Bearer …</c> tokens with <see cref="PqJwtValidator"/>.
/// Fail-closed: any token that fails validation produces
/// <see cref="AuthenticateResult.Fail(Exception)"/>. Bypasses the standard
/// <c>JwtBearerHandler</c> entirely so consumers don't have to teach
/// <c>Microsoft.IdentityModel</c> about <c>ML-DSA-65</c>.
/// </summary>
public sealed class PostQuantumJwtBearerHandler : AuthenticationHandler<PostQuantumJwtBearerOptions>
{
    private PqJwtValidator? _validator;
    private PqJwtValidationParameters? _cachedParameters;

    /// <summary>Creates the handler.</summary>
    /// <param name="options">The options monitor (DI-supplied).</param>
    /// <param name="logger">A logger factory (DI-supplied).</param>
    /// <param name="encoder">A URL encoder (DI-supplied).</param>
    public PostQuantumJwtBearerHandler(
        IOptionsMonitor<PostQuantumJwtBearerOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
        // Defer validator construction to first use — base.Options is not
        // populated until InitializeAsync runs the named-options lookup.
    }

    private PqJwtValidator Validator
    {
        get
        {
            // Reference-equality short-circuit: as long as the options instance
            // hasn't been swapped by IOptionsMonitor reload, keep the existing
            // validator. PqJwtValidator is thread-safe and reusable.
            var current = Options.ValidationParameters;
            if (_validator is null || !ReferenceEquals(_cachedParameters, current))
            {
                _validator = new PqJwtValidator(
                    ComposeValidationParameters(current, Options.KeyRing),
                    Options.TimeProvider);
                _cachedParameters = current;
            }

            return _validator;
        }
    }

    // If a KeyRing is registered and the user hasn't already wired a
    // SignatureKeyResolver, weave the ring's Resolve method in. We do this by
    // cloning the parameters via `with`-style reconstruction since
    // PqJwtValidationParameters is init-only. An explicit user-supplied
    // resolver always wins.
    private static PqJwtValidationParameters ComposeValidationParameters(
        PqJwtValidationParameters source,
        IPostQuantumJwtKeyRing? keyRing)
    {
        if (keyRing is null || source.SignatureKeyResolver is not null)
        {
            return source;
        }

        return new PqJwtValidationParameters
        {
            SignatureVerificationKey = source.SignatureVerificationKey,
            SignatureKeyResolver = keyRing.Resolve,
            ReplayCache = source.ReplayCache,
            DecryptionKey = source.DecryptionKey,
            ValidIssuer = source.ValidIssuer,
            ValidAudience = source.ValidAudience,
            ValidateLifetime = source.ValidateLifetime,
            RequireExpiration = source.RequireExpiration,
            ClockSkew = source.ClockSkew,
        };
    }

    /// <inheritdoc />
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // First chance: let an OnMessageReceived handler supply a token from
        // a non-standard carrier (SignalR ?access_token=, signed cookie,
        // custom header). If it sets Token, we honour it and skip the
        // Authorization header lookup entirely.
        var messageReceived = new PostQuantumJwtBearerMessageReceivedContext(
            Context, Scheme, Options);
        await Options.Events.OnMessageReceived(messageReceived).ConfigureAwait(false);

        if (messageReceived.Result is not null)
        {
            return messageReceived.Result;
        }

        string token;
        if (!string.IsNullOrEmpty(messageReceived.Token))
        {
            token = messageReceived.Token;
        }
        else
        {
            // No header / wrong scheme / empty token → "no result" rather than
            // "fail": lets other schemes registered on the same request get a
            // shot at it. Bearer prefix match is case-insensitive per RFC 6750.
            var authorization = Request.Headers.Authorization.ToString();
            if (!HeaderEncoding.TryGetBearerToken(authorization, out token))
            {
                return AuthenticateResult.NoResult();
            }
        }

        using var activity = PostQuantumJwtBearerDiagnostics.ActivitySource.StartActivity(
            "PostQuantumJwtBearer.Validate",
            System.Diagnostics.ActivityKind.Internal);
        activity?.SetTag("scheme", Scheme.Name);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        PqJwtValidationResult result;
        try
        {
            result = Validator.Validate(token);
        }
        // Fail-closed on ANY exception out of Validate(), not just the
        // engine's PqJwtException family. The engine wraps most failures,
        // but adversarial inputs can still leak FormatException (bad
        // Base64), CryptographicException, etc. — and a security library
        // must treat all of them as "this token is rejected", not let
        // them surface as 500s that disclose internals to the attacker.
        // We deliberately do NOT catch OutOfMemoryException or
        // StackOverflowException — those are environmental and should
        // crash the host so an operator notices.
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
        {
            stopwatch.Stop();
            var schemeTag = new KeyValuePair<string, object?>("scheme", Scheme.Name);
            PostQuantumJwtBearerDiagnostics.AuthFailure.Add(1,
                schemeTag,
                new KeyValuePair<string, object?>("reason", ex.GetType().Name));
            PostQuantumJwtBearerDiagnostics.AuthLatency.Record(
                stopwatch.Elapsed.TotalMilliseconds,
                schemeTag,
                new KeyValuePair<string, object?>("result", "failure"));
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("result", "failure");
            activity?.SetTag("failure.reason", ex.GetType().Name);

            Logger.ValidationFailed(ex);

            var failed = new PostQuantumJwtBearerAuthenticationFailedContext(
                Context, Scheme, Options, ex);
            await Options.Events.OnAuthenticationFailed(failed).ConfigureAwait(false);

            return failed.Result ?? AuthenticateResult.Fail(ex);
        }

        var identity = new ClaimsIdentity(
            authenticationType: Options.AuthenticationType,
            nameType: Options.NameClaimType,
            roleType: Options.RoleClaimType);

        foreach (var (name, element) in result.Claims)
        {
            AppendClaim(identity, name, element);
        }

        var principal = new ClaimsPrincipal(identity);

        var validated = new PostQuantumJwtBearerTokenValidatedContext(
            Context, Scheme, Options, principal, result, token);

        try
        {
            await Options.Events.OnTokenValidated(validated).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var schemeTag = new KeyValuePair<string, object?>("scheme", Scheme.Name);
            PostQuantumJwtBearerDiagnostics.AuthFailure.Add(1,
                schemeTag,
                new KeyValuePair<string, object?>("reason", ex.GetType().Name));
            PostQuantumJwtBearerDiagnostics.AuthLatency.Record(
                stopwatch.Elapsed.TotalMilliseconds,
                schemeTag,
                new KeyValuePair<string, object?>("result", "failure"));
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("result", "failure");
            activity?.SetTag("failure.reason", ex.GetType().Name);

            Logger.ValidationFailed(ex);

            var failed = new PostQuantumJwtBearerAuthenticationFailedContext(
                Context, Scheme, Options, ex);
            await Options.Events.OnAuthenticationFailed(failed).ConfigureAwait(false);

            return failed.Result ?? AuthenticateResult.Fail(ex);
        }

        stopwatch.Stop();
        var schemeTagSuccess = new KeyValuePair<string, object?>("scheme", Scheme.Name);
        PostQuantumJwtBearerDiagnostics.AuthSuccess.Add(1, schemeTagSuccess);
        PostQuantumJwtBearerDiagnostics.AuthLatency.Record(
            stopwatch.Elapsed.TotalMilliseconds,
            schemeTagSuccess,
            new KeyValuePair<string, object?>("result", "success"));
        activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Ok);
        activity?.SetTag("result", "success");

        if (validated.Result is not null)
        {
            return validated.Result;
        }

        var ticket = new AuthenticationTicket(validated.Principal, Scheme.Name);
        return AuthenticateResult.Success(ticket);
    }

    /// <inheritdoc />
    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var challenge = new PostQuantumJwtBearerChallengeContext(
            Context, Scheme, Options, properties);
        await Options.Events.OnChallenge(challenge).ConfigureAwait(false);

        if (challenge.Handled)
        {
            // Event handler took over — don't overwrite their response.
            return;
        }

        var realm = HeaderEncoding.EscapeForQuotedString(Options.Realm ?? Scheme.Name);

        var authResult = await Context.AuthenticateAsync(Scheme.Name).ConfigureAwait(false);
        var hasAuthenticationError = authResult?.Failure != null;

        var header = Options.IncludeErrorDetailsInChallenge && hasAuthenticationError
            ? $"Bearer realm=\"{realm}\", error=\"invalid_token\""
            : $"Bearer realm=\"{realm}\"";

        Response.Headers.Append(Microsoft.Net.Http.Headers.HeaderNames.WWWAuthenticate, header);
        Response.StatusCode = StatusCodes.Status401Unauthorized;
    }

    private static void AppendClaim(ClaimsIdentity identity, string name, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                identity.AddClaim(new Claim(name, element.GetString() ?? string.Empty));
                break;
            case JsonValueKind.Number:
                identity.AddClaim(new Claim(name, element.GetRawText()));
                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
                identity.AddClaim(new Claim(name, element.GetBoolean() ? "true" : "false"));
                break;
            case JsonValueKind.Array:
                // Flatten arrays into one Claim per item so
                // [Authorize(Roles = "...,...")] matches a typical role-array claim.
                foreach (var item in element.EnumerateArray())
                {
                    AppendClaim(identity, name, item);
                }

                break;
            case JsonValueKind.Object:
                identity.AddClaim(new Claim(name, element.GetRawText(), "application/json"));
                break;
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
            default:
                // Null/undefined claims add no information — skip them rather
                // than emit empty Claim entries that surprise downstream code.
                break;
        }
    }
}
