using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
                _validator = new PqJwtValidator(current, Options.TimeProvider);
                _cachedParameters = current;
            }

            return _validator;
        }
    }

    /// <inheritdoc />
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // No header / wrong scheme → "no result" rather than "fail": lets
        // other schemes registered on the same request get a shot at it.
        var authorization = Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(authorization) ||
            !authorization.StartsWith(PostQuantumJwtBearerDefaults.BearerPrefix, StringComparison.Ordinal))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var token = authorization[PostQuantumJwtBearerDefaults.BearerPrefix.Length..];
        if (string.IsNullOrWhiteSpace(token))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        PqJwtValidationResult result;
        try
        {
            result = Validator.Validate(token);
        }
        catch (PqJwtValidationException ex)
        {
            Logger.ValidationFailed(ex);
            return Task.FromResult(AuthenticateResult.Fail(ex));
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
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    /// <inheritdoc />
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var realm = Options.Realm ?? Scheme.Name;
        var header = Options.IncludeErrorDetailsInChallenge
            ? $"Bearer realm=\"{realm}\", error=\"invalid_token\""
            : $"Bearer realm=\"{realm}\"";

        Response.Headers.WWWAuthenticate = header;
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
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
