using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using PostQuantum.Jwt;

namespace PostQuantum.AspNetCore;

/// <summary>
/// Base type for <see cref="PostQuantumJwtBearerEvents"/> contexts — bundles
/// the request, the resolved options, and the authentication scheme.
/// </summary>
public abstract class PostQuantumJwtBearerContext
{
    /// <summary>Creates the context.</summary>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="scheme">The authentication scheme.</param>
    /// <param name="options">The handler's options.</param>
    protected PostQuantumJwtBearerContext(
        HttpContext httpContext,
        AuthenticationScheme scheme,
        PostQuantumJwtBearerOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(scheme);
        ArgumentNullException.ThrowIfNull(options);
        HttpContext = httpContext;
        Scheme = scheme;
        Options = options;
    }

    /// <summary>The current HTTP context.</summary>
    public HttpContext HttpContext { get; }

    /// <summary>The authentication scheme.</summary>
    public AuthenticationScheme Scheme { get; }

    /// <summary>The handler's options.</summary>
    public PostQuantumJwtBearerOptions Options { get; }
}

/// <summary>
/// Context passed to <see cref="PostQuantumJwtBearerEvents.OnMessageReceived"/>.
/// Set <see cref="Token"/> to supply a token from a non-standard carrier
/// (SignalR <c>?access_token=</c>, signed cookie, custom header, etc.).
/// </summary>
public sealed class PostQuantumJwtBearerMessageReceivedContext : PostQuantumJwtBearerContext
{
    /// <summary>Creates the context.</summary>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="scheme">The authentication scheme.</param>
    /// <param name="options">The handler's options.</param>
    public PostQuantumJwtBearerMessageReceivedContext(
        HttpContext httpContext,
        AuthenticationScheme scheme,
        PostQuantumJwtBearerOptions options)
        : base(httpContext, scheme, options)
    {
    }

    /// <summary>
    /// The token to validate. Leave <see langword="null"/> to fall back to
    /// the standard <c>Authorization: Bearer …</c> header lookup. Setting
    /// this to an empty string is treated the same as leaving it null;
    /// setting it to a non-empty value short-circuits the header lookup
    /// entirely.
    /// </summary>
    public string? Token { get; set; }
    /// <summary>
    /// If set, the handler short-circuits and returns this result immediately.
    /// Use this to return <see cref="AuthenticateResult.Fail(Exception)"/> or
    /// <see cref="AuthenticateResult.NoResult"/> directly.
    /// </summary>
    public AuthenticateResult? Result { get; set; }
}

/// <summary>
/// Context passed to <see cref="PostQuantumJwtBearerEvents.OnTokenValidated"/>.
/// The principal is exposed mutably so handlers can enrich it; replacing
/// <see cref="Principal"/> outright replaces the identity for the request.
/// </summary>
public sealed class PostQuantumJwtBearerTokenValidatedContext : PostQuantumJwtBearerContext
{
    /// <summary>Creates the context.</summary>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="scheme">The authentication scheme.</param>
    /// <param name="options">The handler's options.</param>
    /// <param name="principal">The principal built from validated claims.</param>
    /// <param name="validationResult">The validator's full result.</param>
    /// <param name="token">The raw token string.</param>
    public PostQuantumJwtBearerTokenValidatedContext(
        HttpContext httpContext,
        AuthenticationScheme scheme,
        PostQuantumJwtBearerOptions options,
        System.Security.Claims.ClaimsPrincipal principal,
        PqJwtValidationResult validationResult,
        string token)
        : base(httpContext, scheme, options)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(validationResult);
        ArgumentNullException.ThrowIfNull(token);
        Principal = principal;
        ValidationResult = validationResult;
        Token = token;
    }

    /// <summary>The principal constructed from validated claims. Replace or mutate to enrich.</summary>
    public System.Security.Claims.ClaimsPrincipal Principal { get; set; }

    /// <summary>The validator's full result — gives access to the raw <c>JsonElement</c> claim values.</summary>
    public PqJwtValidationResult ValidationResult { get; }

    /// <summary>The raw token string (post-<c>Bearer </c> prefix).</summary>
    public string Token { get; }
    /// <summary>
    /// If set, the handler skips constructing a success ticket and returns this
    /// result instead. Use this to reject a mathematically valid token based on
    /// custom authorization rules by setting <see cref="AuthenticateResult.Fail(Exception)"/>.
    /// </summary>
    public AuthenticateResult? Result { get; set; }
}

/// <summary>
/// Context passed to <see cref="PostQuantumJwtBearerEvents.OnAuthenticationFailed"/>.
/// </summary>
public sealed class PostQuantumJwtBearerAuthenticationFailedContext : PostQuantumJwtBearerContext
{
    /// <summary>Creates the context.</summary>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="scheme">The authentication scheme.</param>
    /// <param name="options">The handler's options.</param>
    /// <param name="exception">The validation exception.</param>
    public PostQuantumJwtBearerAuthenticationFailedContext(
        HttpContext httpContext,
        AuthenticationScheme scheme,
        PostQuantumJwtBearerOptions options,
        Exception exception)
        : base(httpContext, scheme, options)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Exception = exception;
    }

    /// <summary>The exception that token validation threw.</summary>
    public Exception Exception { get; }

    /// <summary>
    /// If set, replaces the default <see cref="AuthenticateResult.Fail(Exception)"/>
    /// outcome — for example, to downgrade a specific exception type to
    /// <see cref="AuthenticateResult.NoResult"/> so another scheme can take a turn.
    /// </summary>
    public AuthenticateResult? Result { get; set; }
}

/// <summary>
/// Context passed to <see cref="PostQuantumJwtBearerEvents.OnChallenge"/>.
/// </summary>
public sealed class PostQuantumJwtBearerChallengeContext : PostQuantumJwtBearerContext
{
    /// <summary>Creates the context.</summary>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="scheme">The authentication scheme.</param>
    /// <param name="options">The handler's options.</param>
    /// <param name="properties">The authentication properties supplied by the challenge.</param>
    public PostQuantumJwtBearerChallengeContext(
        HttpContext httpContext,
        AuthenticationScheme scheme,
        PostQuantumJwtBearerOptions options,
        AuthenticationProperties properties)
        : base(httpContext, scheme, options)
    {
        ArgumentNullException.ThrowIfNull(properties);
        Properties = properties;
    }

    /// <summary>The authentication properties supplied by the challenge.</summary>
    public AuthenticationProperties Properties { get; }

    /// <summary>
    /// Set to <see langword="true"/> to suppress the handler's default
    /// <c>WWW-Authenticate</c> header. The caller is then responsible for
    /// writing any response headers and status code.
    /// </summary>
    public bool Handled { get; set; }
}
